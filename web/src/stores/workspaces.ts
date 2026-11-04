// The workspace tree (10.2): workspaces → spaces → lists → statuses, the caller's role, and the
// hierarchy mutations. Changes apply at once and roll back on failure (9.1 rule 9); only
// createList waits for the server, because its statuses come back with it.
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, newId, query } from '../api'
import { appRouter } from '../composables/appRouter'
import { useToast } from '../composables/useToast'
import { useWorkspaceSwitch } from '../composables/useWorkspaceSwitch'
import { handled } from '../lib/async'
import { DEFAULT_COLORS } from '../lib/colors'
import { between } from '../lib/positions'
import { readString, userKey, writeString } from '../lib/storage'
import {
  STATUS_CATEGORY_ORDER,
  type ListSummary,
  type Role,
  type Space,
  type Status,
  type StatusCategory,
  type StatusPreset,
  type UpdateListRequest,
  type UpdateSpaceRequest,
  type UpdateStatusRequest,
  type Uuid,
  type Workspace,
} from '../types'
import { useRecents } from './recents'
import { useSelection } from './selection'
import { useSession } from './session'
import { useTasks } from './tasks'

export interface ListLocation {
  list: ListSummary
  space: Space
  workspace: Workspace
}

const EMPTY_STATUSES: Status[] = []

function byPosition<T extends { position: number }>(a: T, b: T): number {
  return a.position - b.position
}

function maxPosition(items: { position: number }[]): number | null {
  let max: number | null = null
  for (const x of items) if (max === null || x.position > max) max = x.position
  return max
}

export const useWorkspaces = defineStore('workspaces', () => {
  const toast = useToast()

  const list = ref<Workspace[]>([])
  const currentId = ref<Uuid | null>(null)
  const loaded = ref(false)
  const error = ref<unknown>(null)
  /** When the tree last came from the server (ms); 0 before the first load. */
  const loadedAt = ref(0)

  let loading: Promise<void> | null = null
  // A refresh that started before a hierarchy write settled, or while one is in flight, is dropped,
  // so it can't overwrite an optimistic change with older data (9.1 rule 7 applied to the tree).
  let writeClock = 0
  let writesInFlight = 0

  // ---- Indexes ----

  const current = computed<Workspace | null>(() => list.value.find((w) => w.id === currentId.value) ?? null)

  const listIndex = computed(() => {
    const m = new Map<Uuid, ListLocation>()
    for (const workspace of list.value)
      for (const space of workspace.spaces) for (const l of space.lists) m.set(l.id, { list: l, space, workspace })
    return m
  })

  const spaceIndex = computed(() => {
    const m = new Map<Uuid, { space: Space; workspace: Workspace }>()
    for (const workspace of list.value) for (const space of workspace.spaces) m.set(space.id, { space, workspace })
    return m
  })

  const statusIndex = computed(() => {
    const m = new Map<Uuid, Status>()
    for (const { list: l } of listIndex.value.values()) for (const s of l.statuses) m.set(s.id, s)
    return m
  })

  /** Per list: statuses by position, and in display order (category, then position). */
  const statusOrders = computed(() => {
    const byPos = new Map<Uuid, Status[]>()
    const display = new Map<Uuid, Status[]>()
    for (const { list: l } of listIndex.value.values()) {
      const sorted = [...l.statuses].sort(byPosition)
      byPos.set(l.id, sorted)
      display.set(
        l.id,
        [...sorted].sort((a, b) => STATUS_CATEGORY_ORDER[a.category] - STATUS_CATEGORY_ORDER[b.category] || a.position - b.position),
      )
    }
    return { byPos, display }
  })

  const allLists = computed<{ list: ListSummary; space: Space }[]>(() => {
    const ws = current.value
    if (!ws) return []
    const out: { list: ListSummary; space: Space }[] = []
    for (const space of [...ws.spaces].sort(byPosition)) for (const l of [...space.lists].sort(byPosition)) out.push({ list: l, space })
    return out
  })

  // ---- Roles ----

  function workspaceOf(wsId?: Uuid): Workspace | null {
    if (!wsId) return current.value
    return list.value.find((w) => w.id === wsId) ?? null
  }

  function role(wsId?: Uuid): Role | null {
    return workspaceOf(wsId)?.role ?? null
  }

  /** Member or above. */
  function canEdit(wsId?: Uuid): boolean {
    const r = role(wsId)
    return r === 'Owner' || r === 'Admin' || r === 'Member'
  }

  /** Admin or above ("canManage" in 9.4). */
  function isAdmin(wsId?: Uuid): boolean {
    const r = role(wsId)
    return r === 'Owner' || r === 'Admin'
  }

  function isOwner(wsId?: Uuid): boolean {
    return role(wsId) === 'Owner'
  }

  // ---- Lookups ----

  /** Searches every workspace the caller is in, so a link into another workspace resolves (8.2). */
  function findList(listId: Uuid): ListLocation | null {
    return listIndex.value.get(listId) ?? null
  }

  function findSpace(spaceId: Uuid): { space: Space; workspace: Workspace } | null {
    return spaceIndex.value.get(spaceId) ?? null
  }

  /** Display order: STATUS_CATEGORY_ORDER, then position. The array is shared: don't mutate it. */
  function statusesFor(listId: Uuid): Status[] {
    return statusOrders.value.display.get(listId) ?? EMPTY_STATUSES
  }

  /** By position (the server's order). The array is shared: don't mutate it. */
  function statusesByPosition(listId: Uuid): Status[] {
    return statusOrders.value.byPos.get(listId) ?? EMPTY_STATUSES
  }

  function statusById(statusId: Uuid): Status | undefined {
    return statusIndex.value.get(statusId)
  }

  /** The first Not started status by position, else the first status by position (the server's create default). */
  function defaultStatusFor(listId: Uuid): Status | undefined {
    const s = statusesByPosition(listId)
    return s.find((x) => x.category === 'NotStarted') ?? s[0]
  }

  /** The first Done status by position ("Mark complete"). */
  function doneStatusFor(listId: Uuid): Status | undefined {
    return statusesByPosition(listId).find((x) => x.category === 'Done')
  }

  /** The first Not started status by position, else the first Active one ("Reopen"). */
  function openStatusFor(listId: Uuid): Status | undefined {
    const s = statusesByPosition(listId)
    return s.find((x) => x.category === 'NotStarted') ?? s.find((x) => x.category === 'Active')
  }

  // ---- Loading ----

  function storageKey(): string | null {
    const userId = useSession().user?.id
    return userId ? userKey('taskloom.workspace', userId) : null
  }

  function persistCurrent(): void {
    const key = storageKey()
    if (key) writeString(key, currentId.value)
  }

  /** Applies a fetched tree and settles the current workspace. */
  function applyTree(tree: Workspace[]): void {
    list.value = tree
    loaded.value = true
    error.value = null
    loadedAt.value = Date.now()
    if (currentId.value && tree.some((w) => w.id === currentId.value)) return
    if (currentId.value) {
      // The current workspace is gone (deleted elsewhere, or we were removed): switch properly.
      const next = tree[0]
      if (next) void useWorkspaceSwitch().switchTo(next.id, { navigate: true })
      else {
        useSelection().clear()
        useTasks().reset()
        currentId.value = null
        persistCurrent()
      }
      return
    }
    // Start-up: the last workspace used, else the first (8.2).
    const key = storageKey()
    const stored = key ? readString(key) : null
    currentId.value = (stored && tree.some((w) => w.id === stored) ? stored : tree[0]?.id) ?? null
    persistCurrent()
  }

  function fetchTree(): Promise<void> {
    if (loading) return loading
    const startedAt = writeClock
    loading = api
      .get<Workspace[]>('/api/workspaces')
      .then(
        (tree) => {
          // A write that started or finished meanwhile owns the tree; the next refresh catches up.
          if (loaded.value && (writesInFlight > 0 || writeClock !== startedAt)) return
          applyTree(tree)
        },
        (err: unknown) => {
          error.value = err
        },
      )
      .finally(() => {
        loading = null
      })
    return loading
  }

  /** Loads the tree once; later calls resolve at once. Never rejects (see `error`). */
  function load(): Promise<void> {
    if (loaded.value) return Promise.resolve()
    return fetchTree()
  }

  /** Refetches the tree; with `ifOlderThan` (ms) only when the data is older (the focus refresh, 8.1). Never rejects. */
  function refresh(o?: { ifOlderThan?: number }): Promise<void> {
    const maxAge = o?.ifOlderThan
    if (maxAge !== undefined && loaded.value && Date.now() - loadedAt.value < maxAge) return Promise.resolve()
    return fetchTree()
  }

  /** Sets the current workspace. Only `useWorkspaceSwitch().switchTo` calls this (8.2). */
  async function select(wsId: Uuid): Promise<void> {
    if (!list.value.some((w) => w.id === wsId)) await fetchTree()
    if (!list.value.some((w) => w.id === wsId)) return
    currentId.value = wsId
    persistCurrent()
  }

  /** Clears everything (sign-out). */
  function reset(): void {
    list.value = []
    currentId.value = null
    loaded.value = false
    error.value = null
    loadedAt.value = 0
  }

  // ---- Writes ----

  /**
   * Runs a hierarchy write: counts it as in flight, rolls back on failure, toasts unless `toast`
   * is false (forms show their errors inline), and rejects with the error. The promise is pre-handled.
   */
  function write<T>(send: () => Promise<T>, rollback: (() => void) | null, o: { toast?: boolean; retry?: () => void } = {}): Promise<T> {
    writesInFlight++
    writeClock++
    const p = send().then(
      (value) => {
        writesInFlight--
        writeClock++
        return value
      },
      (err: unknown) => {
        writesInFlight--
        writeClock++
        rollback?.()
        if (o.toast !== false) toast.error(err, o.retry ? { retry: o.retry } : undefined)
        throw err
      },
    )
    return handled(p)
  }

  function rejectWith(message: string): Promise<never> {
    return handled(Promise.reject(new Error(message)))
  }

  /** Lists that are about to disappear: clear their selection, tasks and recents, and leave their page. */
  function listsRemoved(listIds: Uuid[]): Map<Uuid, boolean> {
    const tasks = useTasks()
    const selection = useSelection()
    const recents = useRecents()
    const wasLoaded = new Map<Uuid, boolean>()
    for (const id of listIds) {
      wasLoaded.set(id, (tasks.listMeta[id]?.loadedAt ?? 0) > 0)
      if (selection.listId === id) selection.clear()
      tasks.forgetList(id)
      recents.forget(id)
    }
    const router = appRouter()
    const route = router?.currentRoute.value
    const openListId = route?.params.listId
    if (router && typeof openListId === 'string' && listIds.includes(openListId)) void router.push('/').catch(() => undefined)
    return wasLoaded
  }

  /** After a failed delete restored lists, reload the tasks of the ones that were loaded before. */
  function reloadLists(wasLoaded: Map<Uuid, boolean>): void {
    const tasks = useTasks()
    for (const [id, was] of wasLoaded) if (was) void tasks.loadList(id, { force: true })
  }

  // Workspaces

  /** POST /api/workspaces. Optimistic; rejects without a toast (the dialog shows the error). */
  function createWorkspace(name: string): Promise<Workspace> {
    const id = newId()
    const trimmed = name.trim()
    list.value.push({ id, name: trimmed, role: 'Owner', spaces: [] })
    const removeLocal = () => {
      list.value = list.value.filter((w) => w.id !== id)
    }
    return handled(
      write(() => api.post<Workspace>('/api/workspaces', { id, name: trimmed }), removeLocal, { toast: false }).then((server) => {
        const live = list.value.find((w) => w.id === id)
        if (!live) return server
        live.name = server.name
        live.role = server.role
        return live
      }),
    )
  }

  /** PATCH /api/workspaces/{id}. Optimistic; rejects without a toast (the settings form shows the error). */
  function renameWorkspace(wsId: Uuid, name: string): Promise<void> {
    const ws = list.value.find((w) => w.id === wsId)
    if (!ws) return rejectWith('This workspace no longer exists.')
    const prev = ws.name
    const trimmed = name.trim()
    ws.name = trimmed
    return write(
      () => api.patch<void>(`/api/workspaces/${wsId}`, { name: trimmed }),
      () => {
        const live = list.value.find((w) => w.id === wsId)
        if (live && live.name === trimmed) live.name = prev
      },
      { toast: false },
    )
  }

  /** DELETE /api/workspaces/{id} (after the confirm). Leaves the workspace at once; restores it on failure. */
  function deleteWorkspace(wsId: Uuid): Promise<void> {
    const index = list.value.findIndex((w) => w.id === wsId)
    if (index === -1) return Promise.resolve()
    const ws = list.value[index]
    list.value = list.value.filter((w) => w.id !== wsId)
    if (currentId.value === wsId) {
      const next = list.value[0]
      if (next) void useWorkspaceSwitch().switchTo(next.id, { navigate: true })
      else {
        useSelection().clear()
        useTasks().reset()
        currentId.value = null
        persistCurrent()
        void appRouter()?.push('/').catch(() => undefined)
      }
    }
    return write(
      () => api.del(`/api/workspaces/${wsId}`),
      () => {
        if (list.value.some((w) => w.id === wsId)) return
        const next = [...list.value]
        next.splice(Math.min(index, next.length), 0, ws)
        list.value = next
        if (currentId.value === null) {
          currentId.value = wsId
          persistCurrent()
        }
      },
    )
  }

  // Spaces

  /** POST /api/workspaces/{id}/spaces in the current workspace. Optimistic; rejects without a toast. */
  function createSpace(r: { name: string; color?: string }): Promise<Space> {
    const ws = current.value
    if (!ws) return rejectWith('Choose a workspace first.')
    const id = newId()
    const name = r.name.trim()
    const color = r.color ?? DEFAULT_COLORS.space
    ws.spaces.push({ id, name, color, position: between(maxPosition(ws.spaces), null), lists: [] })
    const wsId = ws.id
    return handled(
      write(
        () => api.post<Space>(`/api/workspaces/${wsId}/spaces`, { id, name, color }),
        () => {
          const found = findSpace(id)
          if (found) found.workspace.spaces = found.workspace.spaces.filter((s) => s.id !== id)
        },
        { toast: false },
      ).then((server) => {
        const live = findSpace(id)?.space
        if (!live) return server
        live.name = server.name
        live.color = server.color
        live.position = server.position
        return live
      }),
    )
  }

  /** PATCH /api/spaces/{id}: rename, recolour or reorder. Optimistic; toasts and rolls back on failure. */
  function updateSpace(spaceId: Uuid, r: UpdateSpaceRequest): Promise<void> {
    const space = findSpace(spaceId)?.space
    if (!space) return rejectWith('This space no longer exists.')
    const prev: UpdateSpaceRequest = {}
    const body: UpdateSpaceRequest = {}
    if (r.name !== undefined) {
      prev.name = space.name
      body.name = r.name.trim()
      space.name = body.name
    }
    if (r.color !== undefined) {
      prev.color = space.color
      body.color = r.color
      space.color = r.color
    }
    if (r.position !== undefined) {
      prev.position = space.position
      body.position = r.position
      space.position = r.position
    }
    return write(
      () => api.patch<void>(`/api/spaces/${spaceId}`, body),
      () => {
        const live = findSpace(spaceId)?.space
        if (live) Object.assign(live, prev)
      },
      { retry: () => void updateSpace(spaceId, r) },
    )
  }

  /** DELETE /api/spaces/{id} (after the confirm). Optimistic; restores the space on failure. */
  function deleteSpace(spaceId: Uuid): Promise<void> {
    const found = findSpace(spaceId)
    if (!found) return Promise.resolve()
    const { workspace } = found
    const index = workspace.spaces.findIndex((s) => s.id === spaceId)
    const space = workspace.spaces[index]
    workspace.spaces.splice(index, 1)
    const wasLoaded = listsRemoved(space.lists.map((l) => l.id))
    return write(
      () => api.del(`/api/spaces/${spaceId}`),
      () => {
        const ws = list.value.find((w) => w.id === workspace.id)
        if (!ws || ws.spaces.some((s) => s.id === spaceId)) return
        ws.spaces.splice(Math.min(index, ws.spaces.length), 0, space)
        reloadLists(wasLoaded)
      },
    )
  }

  // Lists

  /** POST /api/spaces/{id}/lists. Waits for the server (its statuses come back); rejects without a toast. */
  function createList(spaceId: Uuid, r: { name: string; statusPreset?: StatusPreset; statusesFromListId?: Uuid }): Promise<ListSummary> {
    const id = newId()
    const body = {
      id,
      name: r.name.trim(),
      ...(r.statusPreset ? { statusPreset: r.statusPreset } : {}),
      ...(r.statusesFromListId ? { statusesFromListId: r.statusesFromListId } : {}),
    }
    return handled(
      write(() => api.post<ListSummary>(`/api/spaces/${spaceId}/lists`, body), null, { toast: false }).then((server) => {
        const space = findSpace(spaceId)?.space
        if (space && !space.lists.some((l) => l.id === server.id)) space.lists.push(server)
        return findList(server.id)?.list ?? server
      }),
    )
  }

  /** PATCH /api/lists/{id}: rename, reorder or move to another space (last there unless `position`). */
  function updateList(listId: Uuid, r: UpdateListRequest): Promise<void> {
    const found = findList(listId)
    if (!found) return rejectWith('This list no longer exists.')
    const { list: l, space } = found
    const prevName = l.name
    const prevPosition = l.position
    const prevSpaceId = space.id
    const prevIndex = space.lists.indexOf(l)
    const body: UpdateListRequest = {}
    if (r.name !== undefined) {
      body.name = r.name.trim()
      l.name = body.name
    }
    const target = r.spaceId !== undefined && r.spaceId !== space.id ? findSpace(r.spaceId)?.space : undefined
    if (target) {
      body.spaceId = target.id
      if (r.position !== undefined) body.position = r.position
      space.lists.splice(prevIndex, 1)
      l.position = r.position ?? between(maxPosition(target.lists), null)
      l.spaceId = target.id
      target.lists.push(l)
    } else if (r.position !== undefined) {
      body.position = r.position
      l.position = r.position
    }
    return write(
      () => api.patch<void>(`/api/lists/${listId}`, body),
      () => {
        const now = findList(listId)
        if (!now) return
        if (body.name !== undefined) now.list.name = prevName
        if (body.position !== undefined || body.spaceId !== undefined) now.list.position = prevPosition
        if (body.spaceId !== undefined && now.space.id !== prevSpaceId) {
          const back = findSpace(prevSpaceId)?.space
          if (back) {
            now.space.lists.splice(now.space.lists.indexOf(now.list), 1)
            now.list.spaceId = prevSpaceId
            back.lists.splice(Math.min(prevIndex, back.lists.length), 0, now.list)
          }
        }
      },
      { retry: () => void updateList(listId, r) },
    )
  }

  /** DELETE /api/lists/{id} (after the confirm). Optimistic; leaves the list's page if it is open. */
  function deleteList(listId: Uuid): Promise<void> {
    const found = findList(listId)
    if (!found) return Promise.resolve()
    const { space } = found
    const index = space.lists.indexOf(found.list)
    const removed = found.list
    space.lists.splice(index, 1)
    const wasLoaded = listsRemoved([listId])
    return write(
      () => api.del(`/api/lists/${listId}`),
      () => {
        const sp = findSpace(space.id)?.space
        if (!sp || sp.lists.some((l) => l.id === listId)) return
        sp.lists.splice(Math.min(index, sp.lists.length), 0, removed)
        reloadLists(wasLoaded)
      },
    )
  }

  // Statuses

  /** POST /api/lists/{id}/statuses. Optimistic; toasts and removes it on failure. */
  function createStatus(listId: Uuid, r: { name: string; color?: string; category: StatusCategory }): Promise<Status> {
    const l = findList(listId)?.list
    if (!l) return rejectWith('This list no longer exists.')
    const id = newId()
    const name = r.name.trim()
    const color = r.color ?? DEFAULT_COLORS.todo
    l.statuses.push({ id, listId, name, color, category: r.category, position: between(maxPosition(l.statuses), null) })
    return handled(
      write(
        () => api.post<Status>(`/api/lists/${listId}/statuses`, { id, name, color, category: r.category }),
        () => {
          const live = findList(listId)?.list
          if (live) live.statuses = live.statuses.filter((s) => s.id !== id)
        },
      ).then((server) => {
        const live = statusById(id)
        if (!live) return server
        Object.assign(live, server)
        return live
      }),
    )
  }

  /** PATCH /api/statuses/{id}. A category change also sets or clears completedAt on loaded tasks. */
  function updateStatus(statusId: Uuid, r: UpdateStatusRequest): Promise<Status> {
    const s = statusById(statusId)
    if (!s) return rejectWith('This status no longer exists.')
    const listId = s.listId
    const prev: UpdateStatusRequest = {}
    const body: UpdateStatusRequest = {}
    if (r.name !== undefined) {
      prev.name = s.name
      body.name = r.name.trim()
      s.name = body.name
    }
    if (r.color !== undefined) {
      prev.color = s.color
      body.color = r.color
      s.color = r.color
    }
    if (r.position !== undefined) {
      prev.position = s.position
      body.position = r.position
      s.position = r.position
    }
    const oldCategory = s.category
    const categoryChanged = r.category !== undefined && r.category !== oldCategory
    if (r.category !== undefined) body.category = r.category
    if (categoryChanged && r.category) {
      prev.category = oldCategory
      s.category = r.category
      useTasks().applyCategoryChange(statusId, r.category)
    }
    return handled(
      write(
        () => api.patch<Status>(`/api/statuses/${statusId}`, body),
        () => {
          const live = statusById(statusId)
          if (live) Object.assign(live, prev)
          if (categoryChanged) {
            const tasks = useTasks()
            tasks.applyCategoryChange(statusId, oldCategory)
            // completedAt values were approximated; the server has the real ones.
            if ((tasks.listMeta[listId]?.loadedAt ?? 0) > 0) void tasks.loadList(listId, { force: true })
          }
        },
        { retry: () => void updateStatus(statusId, r) },
      ).then((server) => {
        const live = statusById(statusId)
        if (!live) return server
        Object.assign(live, server)
        return live
      }),
    )
  }

  /**
   * DELETE /api/statuses/{id}?moveTo=. Optimistic: the status disappears and its loaded tasks move
   * to `moveToId`, else to the first remaining status by position (the server's rule, 5.5).
   */
  function deleteStatus(statusId: Uuid, moveToId?: Uuid): Promise<void> {
    const s = statusById(statusId)
    if (!s) return Promise.resolve()
    const l = findList(s.listId)?.list
    if (!l) return Promise.resolve()
    const listId = l.id
    const index = l.statuses.findIndex((x) => x.id === statusId)
    const removed = l.statuses[index]
    const target = moveToId ?? statusesByPosition(listId).find((x) => x.id !== statusId)?.id
    l.statuses.splice(index, 1)
    const tasks = useTasks()
    if (target) tasks.reassignStatus(statusId, target)
    return write(
      () => api.del(`/api/statuses/${statusId}${query({ moveTo: moveToId })}`),
      () => {
        const live = findList(listId)?.list
        if (live && !live.statuses.some((x) => x.id === statusId)) live.statuses.splice(Math.min(index, live.statuses.length), 0, removed)
        if ((tasks.listMeta[listId]?.loadedAt ?? 0) > 0) void tasks.loadList(listId, { force: true })
      },
    )
  }

  return {
    list,
    currentId,
    loaded,
    error,
    loadedAt,
    current,
    allLists,
    role,
    canEdit,
    isAdmin,
    isOwner,
    findList,
    findSpace,
    statusesFor,
    statusesByPosition,
    statusById,
    defaultStatusFor,
    doneStatusFor,
    openStatusFor,
    load,
    refresh,
    select,
    reset,
    createWorkspace,
    renameWorkspace,
    deleteWorkspace,
    createSpace,
    updateSpace,
    deleteSpace,
    createList,
    updateList,
    deleteList,
    createStatus,
    updateStatus,
    deleteStatus,
  }
})

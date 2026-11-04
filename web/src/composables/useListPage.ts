// The state behind ListPage (10.2): it resolves the list (switching workspace first when the list
// is in another one), loads its tasks (and again when the window regains focus with data older
// than 30s), keeps the selection, recents and the remembered view, and builds the ListPageContext
// that the views, the toolbar and the bulk bar read. Filters are saved per user and list, view
// settings per user, list and view; the search is transient and cleared when the list changes.
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { readJson, userKey, writeJson } from '../lib/storage'
import {
  applyFilters,
  filterDefaults,
  loadFilters,
  loadViewConfig,
  saveFilters,
  saveViewConfig,
  type FilterContext,
  type TaskFilters,
  type ViewConfig,
} from '../lib/taskQuery'
import { useRecents } from '../stores/recents'
import { useSelection } from '../stores/selection'
import { useSession } from '../stores/session'
import { useTags } from '../stores/tags'
import { useTasks } from '../stores/tasks'
import { useUi } from '../stores/ui'
import { useWorkspaces } from '../stores/workspaces'
import type { Uuid, ViewKind } from '../types'
import { provideListPageContext, type ListPageContext } from './useListPageContext'
import { useToday } from './useToday'
import { useWorkspaceSwitch } from './useWorkspaceSwitch'

/** 'resolving' while the workspace switch runs; 'failed' when it didn't take. */
export type ListPagePhase = 'resolving' | 'not-found' | 'failed' | 'ready'

/** The only list page renders at a time, so the teleport target can have a fixed id. */
export const TOOLBAR_END_ID = 'list-toolbar-end'

interface LastViews {
  byList?: Record<Uuid, ViewKind>
  last?: ViewKind
}

export function useListPage(listId: Ref<Uuid>, view: Ref<ViewKind>): {
  ctx: ListPageContext
  phase: ComputedRef<ListPagePhase>
  /** A refresh is running while the list's tasks are on screen (the header's loading bar). */
  refreshing: ComputedRef<boolean>
  retrySwitch(): void
} {
  const workspaces = useWorkspaces()
  const tasks = useTasks()
  const tags = useTags()
  const session = useSession()
  const selection = useSelection()
  const recents = useRecents()
  const ui = useUi()
  const switcher = useWorkspaceSwitch()
  const today = useToday()

  const userId = computed(() => session.user?.id ?? '')
  const found = computed(() => workspaces.findList(listId.value))

  // ---- Resolving the list ----

  const switching = ref(false)
  const switchFailedFor = ref<Uuid | null>(null)

  // Ready once the switch has finished (members and tags loaded too), not as soon as the
  // workspace is current, so names and tags never show as unknown for a moment.
  const phase = computed<ListPagePhase>(() => {
    const f = found.value
    if (!f) return 'not-found'
    if (switching.value) return 'resolving'
    if (f.workspace.id === workspaces.currentId) return 'ready'
    return switchFailedFor.value === f.workspace.id ? 'failed' : 'resolving'
  })

  async function switchIfNeeded(): Promise<void> {
    const f = found.value
    if (!f || switching.value || f.workspace.id === workspaces.currentId || switchFailedFor.value === f.workspace.id) return
    const wsId = f.workspace.id
    const openedFor = listId.value
    switching.value = true
    try {
      await switcher.switchTo(wsId, { navigate: false })
    } finally {
      switching.value = false
      if (workspaces.currentId !== wsId) switchFailedFor.value = wsId
    }
    // Another list was opened while this switch ran: switch for that one too.
    if (listId.value !== openedFor) void switchIfNeeded()
  }

  // Only when the list is opened (or its workspace becomes known): a switch made elsewhere while the
  // page is showing (the workspace menu, which then goes Home) must not be switched back.
  watch([listId, () => found.value?.workspace.id], () => void switchIfNeeded(), { immediate: true })

  function retrySwitch(): void {
    switchFailedFor.value = null
    void switchIfNeeded()
  }

  const ready = computed(() => phase.value === 'ready')

  // ---- Preferences: filters (per list), view settings (per list and view), search ----

  const filters = ref<TaskFilters>(loadFilters(userId.value, listId.value))
  let filtersFor = { userId: userId.value, listId: listId.value }
  const config = ref<ViewConfig>(loadViewConfig(userId.value, listId.value, view.value))
  let configFor = { userId: userId.value, listId: listId.value, view: view.value }
  const search = ref('')
  const createdThisSession = ref(new Set<Uuid>())

  watch([listId, userId], ([id, uid]) => {
    filters.value = loadFilters(uid, id)
    filtersFor = { userId: uid, listId: id }
    search.value = ''
    createdThisSession.value = new Set()
  })

  watch([listId, view, userId], ([id, v, uid]) => {
    config.value = loadViewConfig(uid, id, v)
    configFor = { userId: uid, listId: id, view: v }
  })

  watch(
    filters,
    (f) => {
      if (filtersFor.userId) saveFilters(filtersFor.userId, filtersFor.listId, f)
      if (createdThisSession.value.size) createdThisSession.value = new Set()
    },
    { deep: true },
  )

  watch(
    config,
    (c) => {
      if (configFor.userId) saveViewConfig(configFor.userId, configFor.listId, configFor.view, c)
    },
    { deep: true },
  )

  const statuses = computed(() => workspaces.statusesFor(listId.value))

  // Saved filters may name a status or tag that has since been deleted: drop those values.
  watch(
    [() => filters.value.statusIds, () => filters.value.tagIds, statuses, () => tags.current, ready],
    ([statusIds, tagIds, list, known, isReady]) => {
      if (!isReady) return
      const statusSet = new Set(list.map((s) => s.id))
      const keptStatuses = list.length ? statusIds.filter((id) => statusSet.has(id)) : statusIds
      const tagsKnown = !tags.loading && known.length > 0
      const keptTags = tagsKnown ? tagIds.filter((id) => !!tags.get(id)) : tagIds
      if (keptStatuses.length !== statusIds.length || keptTags.length !== tagIds.length) {
        filters.value = { ...filters.value, statusIds: keptStatuses, tagIds: keptTags }
      }
    },
    { immediate: true },
  )

  // ---- Tasks ----

  const allTasks = computed(() => tasks.inList(listId.value))
  const meta = computed(() => tasks.listMeta[listId.value])
  const loading = computed(() => (meta.value?.loadedAt ?? 0) === 0 && !meta.value?.error)
  const error = computed<unknown>(() => (meta.value && meta.value.loadedAt === 0 ? meta.value.error : null) ?? null)
  const refreshing = computed(() => !!meta.value?.loading)

  // Tasks created on this page (inline add, quick create) stay visible until the filters change (8.1).
  watch(allTasks, (list) => {
    const kept = createdThisSession.value
    let next: Set<Uuid> | null = null
    for (const t of list) if (!kept.has(t.id) && tasks.isCreatePending(t.id)) (next ??= new Set(kept)).add(t.id)
    if (next) createdThisSession.value = next
  })

  const filterContext = computed<FilterContext>(() => ({
    meId: userId.value,
    today: today.value,
    statusById: (id: Uuid) => workspaces.statusById(id),
  }))

  const result = computed(() =>
    applyFilters(allTasks.value, filters.value, search.value, filterContext.value, {
      showCompleted: config.value.showCompleted,
      keepVisibleIds: createdThisSession.value,
      heldIds: tasks.recentlyCompleted,
    }),
  )

  const createDefaults = computed(() => filterDefaults(filters.value, filterContext.value))

  function reload(): Promise<void> {
    return tasks.loadList(listId.value, { force: true })
  }

  // ---- Opening the list: load, selection, recents, the remembered view ----

  function rememberView(id: Uuid, v: ViewKind): void {
    if (!userId.value) return
    const key = userKey('taskloom.lastView', userId.value)
    const stored = readJson<LastViews | null>(key, null)
    const byList: Record<Uuid, ViewKind> = {}
    // Lists that no longer exist are dropped as the map is rewritten.
    for (const [lid, lv] of Object.entries(stored?.byList ?? {})) if (workspaces.findList(lid)) byList[lid] = lv
    byList[id] = v
    writeJson(key, { byList, last: v } satisfies LastViews)
  }

  watch(
    [listId, ready],
    ([id, isReady]) => {
      if (!isReady) return
      void tasks.loadList(id)
      selection.setList(id)
      recents.touchList(id)
      recents.setLastList(id)
    },
    { immediate: true },
  )

  watch(
    [listId, view, ready],
    ([id, v, isReady]) => {
      if (isReady) rememberView(id, v)
    },
    { immediate: true },
  )

  watch(
    [listId, createDefaults, ready],
    ([id, d, isReady]) => ui.setCreateDefaults(isReady ? { ...d, listId: id } : null),
    { immediate: true },
  )

  function refreshIfStale(): void {
    if (document.visibilityState === 'hidden' || !ready.value) return
    void tasks.loadList(listId.value)
  }

  function onVisibility(): void {
    if (document.visibilityState === 'visible') refreshIfStale()
  }

  onMounted(() => {
    window.addEventListener('focus', refreshIfStale)
    document.addEventListener('visibilitychange', onVisibility)
  })
  onBeforeUnmount(() => {
    window.removeEventListener('focus', refreshIfStale)
    document.removeEventListener('visibilitychange', onVisibility)
    ui.setCreateDefaults(null)
    selection.setList(null)
  })

  const ctx: ListPageContext = {
    listId: computed(() => listId.value),
    view: computed(() => view.value),
    list: computed(() => found.value?.list ?? null),
    space: computed(() => found.value?.space ?? null),
    statuses,
    canEdit: computed(() => !!found.value && workspaces.canEdit(found.value.workspace.id)),
    allTasks,
    filters,
    search,
    config,
    createdThisSession,
    result,
    createDefaults,
    loading,
    error,
    reload,
    toolbarEndId: TOOLBAR_END_ID,
  }
  provideListPageContext(ctx)

  return { ctx, phase, refreshing, retrySwitch }
}

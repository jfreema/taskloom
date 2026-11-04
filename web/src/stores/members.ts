// Workspace members (10.2): names and emails for pickers and avatars. The caller's own role comes
// from the workspace tree (9.4), never from here.
import { defineStore } from 'pinia'
import { computed, reactive } from 'vue'
import { api } from '../api'
import { useToast } from '../composables/useToast'
import { handled } from '../lib/async'
import type { AddMemberRequest, AssignableRole, Member, Uuid } from '../types'
import { useSession } from './session'
import { useTasks } from './tasks'
import { useWorkspaces } from './workspaces'

interface Meta {
  loadedAt: number
  loading: boolean
  error: unknown
}

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function byName(a: Member, b: Member): number {
  return collator.compare(a.displayName, b.displayName) || (a.userId < b.userId ? -1 : a.userId > b.userId ? 1 : 0)
}

export const useMembers = defineStore('members', () => {
  const toast = useToast()
  const workspaces = useWorkspaces()

  const byWorkspace = reactive<Record<Uuid, Member[]>>({})
  const meta = reactive<Record<Uuid, Meta>>({})
  const inflight = new Map<Uuid, Promise<void>>()
  let generation = 0

  /** Current workspace, by display name. */
  const current = computed<Member[]>(() => {
    const id = workspaces.currentId
    const members = id ? byWorkspace[id] : undefined
    return members ? [...members].sort(byName) : []
  })

  const index = computed(() => new Map(current.value.map((m) => [m.userId, m])))

  /** The last load failure for the current workspace, or null. */
  const loadError = computed<unknown>(() => {
    const id = workspaces.currentId
    return id ? (meta[id]?.error ?? null) : null
  })

  const loading = computed<boolean>(() => {
    const id = workspaces.currentId
    return id ? (meta[id]?.loading ?? false) : false
  })

  function get(userId: Uuid): Member | undefined {
    return index.value.get(userId)
  }

  /** The display name, or "Former member" for someone no longer in the workspace. */
  function nameOf(userId: Uuid): string {
    const m = index.value.get(userId)
    if (m) return m.displayName
    const me = useSession().user
    if (me && me.id === userId) return me.displayName
    return 'Former member'
  }

  function metaFor(wsId: Uuid): Meta {
    if (!meta[wsId]) meta[wsId] = { loadedAt: 0, loading: false, error: null }
    return meta[wsId]
  }

  /** Loads the members once per workspace. Never rejects (see `loadError`). */
  function ensure(wsId?: Uuid): Promise<void> {
    const id = wsId ?? workspaces.currentId
    if (!id) return Promise.resolve()
    if ((meta[id]?.loadedAt ?? 0) > 0) return Promise.resolve()
    return reload(id)
  }

  /** GET /api/workspaces/{id}/members. Never rejects (see `loadError`). */
  function reload(wsId?: Uuid): Promise<void> {
    const id = wsId ?? workspaces.currentId
    if (!id) return Promise.resolve()
    const running = inflight.get(id)
    if (running) return running
    const gen = generation
    const m = metaFor(id)
    m.loading = true
    const p = api
      .get<Member[]>(`/api/workspaces/${id}/members`)
      .then(
        (members) => {
          if (gen !== generation) return
          byWorkspace[id] = members
          const live = metaFor(id)
          live.loadedAt = Date.now()
          live.error = null
        },
        (err: unknown) => {
          if (gen !== generation) return
          metaFor(id).error = err
        },
      )
      .finally(() => {
        inflight.delete(id)
        if (gen === generation) metaFor(id).loading = false
      })
    inflight.set(id, p)
    return p
  }

  function upsertLocal(wsId: Uuid, member: Member): void {
    const members = byWorkspace[wsId] ?? []
    const i = members.findIndex((x) => x.userId === member.userId)
    if (i === -1) byWorkspace[wsId] = [...members, member]
    else byWorkspace[wsId] = members.map((x, j) => (j === i ? member : x))
  }

  /**
   * POST /api/workspaces/{id}/members in the current workspace. Waits for the server (the user id
   * comes from it); rejects without a toast, because the invite dialog lists failures inline.
   */
  function invite(r: AddMemberRequest): Promise<Member> {
    const wsId = workspaces.currentId
    if (!wsId) return handled(Promise.reject(new Error('Choose a workspace first.')))
    const body: AddMemberRequest = { email: r.email.trim(), role: r.role }
    if (r.displayName && r.displayName.trim()) body.displayName = r.displayName.trim()
    return handled(
      api.post<Member>(`/api/workspaces/${wsId}/members`, body).then((member) => {
        upsertLocal(wsId, member)
        return member
      }),
    )
  }

  /** PATCH /api/workspaces/{id}/members/{userId}. Optimistic; toasts and rolls back on failure. */
  function updateRole(userId: Uuid, role: AssignableRole): Promise<void> {
    const wsId = workspaces.currentId
    const member = wsId ? byWorkspace[wsId]?.find((m) => m.userId === userId) : undefined
    if (!wsId || !member) return handled(Promise.reject(new Error('This person is no longer a member.')))
    const prev = member.role
    member.role = role
    return handled(
      api.patch<void>(`/api/workspaces/${wsId}/members/${userId}`, { role }).catch((err: unknown) => {
        const live = byWorkspace[wsId]?.find((m) => m.userId === userId)
        if (live && live.role === role) live.role = prev
        toast.error(err, { retry: () => void updateRole(userId, role) })
        throw err
      }),
    )
  }

  /**
   * DELETE /api/workspaces/{id}/members/{userId} (after the confirm). Optimistic: the member and
   * their assignments in this workspace disappear at once and come back if the request fails.
   */
  function remove(userId: Uuid): Promise<void> {
    const wsId = workspaces.currentId
    const members = wsId ? byWorkspace[wsId] : undefined
    const index = members ? members.findIndex((m) => m.userId === userId) : -1
    if (!wsId || !members || index === -1) return Promise.resolve()
    const removed = members[index]
    byWorkspace[wsId] = members.filter((m) => m.userId !== userId)
    const restoreAssignments = useTasks().stripAssignee(userId)
    return handled(
      api.del(`/api/workspaces/${wsId}/members/${userId}`).catch((err: unknown) => {
        const now = byWorkspace[wsId] ?? []
        if (!now.some((m) => m.userId === userId)) {
          const next = [...now]
          next.splice(Math.min(index, next.length), 0, removed)
          byWorkspace[wsId] = next
        }
        restoreAssignments()
        toast.error(err)
        throw err
      }),
    )
  }

  /** Mirrors a display-name change (the profile page) into every workspace's list. */
  function renameLocal(userId: Uuid, displayName: string): void {
    for (const members of Object.values(byWorkspace)) {
      const m = members.find((x) => x.userId === userId)
      if (m) m.displayName = displayName
    }
  }

  function reset(): void {
    generation++
    for (const key of Object.keys(byWorkspace)) delete byWorkspace[key]
    for (const key of Object.keys(meta)) delete meta[key]
    inflight.clear()
  }

  return { current, loadError, loading, get, nameOf, ensure, reload, invite, updateRole, remove, renameLocal, reset }
})

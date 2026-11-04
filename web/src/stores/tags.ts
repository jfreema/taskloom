// Workspace tags (10.2). Creates are optimistic with a client id; a task edit that uses a tag
// still being created waits for it (`whenCreated`), so the PATCH never names an unknown tag.
import { defineStore } from 'pinia'
import { computed, reactive } from 'vue'
import { api, newId } from '../api'
import { useToast } from '../composables/useToast'
import { handled, noop } from '../lib/async'
import { nextSwatch } from '../lib/colors'
import type { Tag, UpdateTagRequest, Uuid } from '../types'
import { useTasks } from './tasks'
import { useWorkspaces } from './workspaces'

interface Meta {
  loadedAt: number
  loading: boolean
  error: unknown
}

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function byName(a: Tag, b: Tag): number {
  return collator.compare(a.name, b.name) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}

export const useTags = defineStore('tags', () => {
  const toast = useToast()
  const workspaces = useWorkspaces()

  const byWorkspace = reactive<Record<Uuid, Tag[]>>({})
  const meta = reactive<Record<Uuid, Meta>>({})
  const inflight = new Map<Uuid, Promise<void>>()
  /** Pending creates by tag id; they settle (never reject) when the server answers. */
  const creating = new Map<Uuid, Promise<void>>()
  let generation = 0

  /** Current workspace, by name (the server's order). */
  const current = computed<Tag[]>(() => {
    const id = workspaces.currentId
    const tags = id ? byWorkspace[id] : undefined
    return tags ? [...tags].sort(byName) : []
  })

  const index = computed(() => new Map(current.value.map((t) => [t.id, t])))

  const loadError = computed<unknown>(() => {
    const id = workspaces.currentId
    return id ? (meta[id]?.error ?? null) : null
  })

  const loading = computed<boolean>(() => {
    const id = workspaces.currentId
    return id ? (meta[id]?.loading ?? false) : false
  })

  function get(tagId: Uuid): Tag | undefined {
    return index.value.get(tagId)
  }

  function metaFor(wsId: Uuid): Meta {
    if (!meta[wsId]) meta[wsId] = { loadedAt: 0, loading: false, error: null }
    return meta[wsId]
  }

  /** Loads the tags once per workspace. Never rejects (see `loadError`). */
  function ensure(wsId?: Uuid): Promise<void> {
    const id = wsId ?? workspaces.currentId
    if (!id) return Promise.resolve()
    if ((meta[id]?.loadedAt ?? 0) > 0) return Promise.resolve()
    return reload(id)
  }

  /** GET /api/workspaces/{id}/tags. Keeps tags whose create is still pending. Never rejects. */
  function reload(wsId?: Uuid): Promise<void> {
    const id = wsId ?? workspaces.currentId
    if (!id) return Promise.resolve()
    const running = inflight.get(id)
    if (running) return running
    const gen = generation
    metaFor(id).loading = true
    const p = api
      .get<Tag[]>(`/api/workspaces/${id}/tags`)
      .then(
        (tags) => {
          if (gen !== generation) return
          const pending = (byWorkspace[id] ?? []).filter((t) => creating.has(t.id) && !tags.some((x) => x.id === t.id))
          byWorkspace[id] = [...tags, ...pending]
          const m = metaFor(id)
          m.loadedAt = Date.now()
          m.error = null
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

  /**
   * The colour for a new tag (7.2): the swatch after the colour of the most recently created tag,
   * by `createdAt`; Blue when there are none.
   */
  function nextColor(): string {
    let latest: Tag | undefined
    let latestAt = -Infinity
    for (const t of current.value) {
      const at = Date.parse(t.createdAt)
      if (!Number.isNaN(at) && at >= latestAt) {
        latest = t
        latestAt = at
      }
    }
    return nextSwatch(latest?.color)
  }

  /** POST /api/workspaces/{id}/tags. Optimistic with a client id; toasts and removes it on failure. */
  function create(r: { name: string; color?: string }): Promise<Tag> {
    const wsId = workspaces.currentId
    if (!wsId) return handled(Promise.reject(new Error('Choose a workspace first.')))
    const id = newId()
    const name = r.name.trim()
    const color = r.color ?? nextColor()
    const tag: Tag = { id, workspaceId: wsId, name, color, createdAt: new Date().toISOString() }
    byWorkspace[wsId] = [...(byWorkspace[wsId] ?? []), tag]
    const gen = generation
    const p = api.post<Tag>(`/api/workspaces/${wsId}/tags`, { id, name, color }).then(
      (server) => {
        if (gen === generation) byWorkspace[wsId] = (byWorkspace[wsId] ?? []).map((t) => (t.id === id ? server : t))
        return server
      },
      (err: unknown) => {
        if (gen === generation) byWorkspace[wsId] = (byWorkspace[wsId] ?? []).filter((t) => t.id !== id)
        toast.error(err)
        throw err
      },
    )
    const settled = p.then(noop, noop)
    creating.set(id, settled)
    void settled.then(() => creating.delete(id))
    return handled(p)
  }

  /** Resolves once every listed tag that is still being created has been answered by the server. */
  function whenCreated(ids: readonly Uuid[] | undefined): Promise<void> {
    if (!ids || !ids.length || !creating.size) return Promise.resolve()
    const waits = ids.map((id) => creating.get(id)).filter((x): x is Promise<void> => !!x)
    return waits.length ? Promise.all(waits).then(noop) : Promise.resolve()
  }

  /** PATCH /api/tags/{id}: rename or recolour. Optimistic; toasts and rolls back on failure. */
  function update(tagId: Uuid, r: UpdateTagRequest): Promise<Tag> {
    const wsId = workspaces.currentId
    const tag = wsId ? byWorkspace[wsId]?.find((t) => t.id === tagId) : undefined
    if (!wsId || !tag) return handled(Promise.reject(new Error('This tag no longer exists.')))
    const prev: UpdateTagRequest = {}
    const body: UpdateTagRequest = {}
    if (r.name !== undefined) {
      prev.name = tag.name
      body.name = r.name.trim()
      tag.name = body.name
    }
    if (r.color !== undefined) {
      prev.color = tag.color
      body.color = r.color
      tag.color = r.color
    }
    const gen = generation
    return handled(
      whenCreated([tagId])
        .then(() => api.patch<Tag>(`/api/tags/${tagId}`, body))
        .then(
          (server) => {
            if (gen === generation) {
              const live = byWorkspace[wsId]?.find((t) => t.id === tagId)
              if (live) Object.assign(live, server)
            }
            return server
          },
          (err: unknown) => {
            if (gen === generation) {
              const live = byWorkspace[wsId]?.find((t) => t.id === tagId)
              if (live) Object.assign(live, prev)
            }
            toast.error(err, { retry: () => void update(tagId, r) })
            throw err
          },
        ),
    )
  }

  /** DELETE /api/tags/{id} (after the confirm). Optimistic: the tag leaves every loaded task too. */
  function remove(tagId: Uuid): Promise<void> {
    const wsId = workspaces.currentId
    const tags = wsId ? byWorkspace[wsId] : undefined
    const index = tags ? tags.findIndex((t) => t.id === tagId) : -1
    if (!wsId || !tags || index === -1) return Promise.resolve()
    const removed = tags[index]
    byWorkspace[wsId] = tags.filter((t) => t.id !== tagId)
    const restoreTasks = useTasks().stripTag(tagId)
    const gen = generation
    return handled(
      whenCreated([tagId])
        .then(() => api.del(`/api/tags/${tagId}`))
        .catch((err: unknown) => {
          if (gen === generation) {
            const now = byWorkspace[wsId] ?? []
            if (!now.some((t) => t.id === tagId)) {
              const next = [...now]
              next.splice(Math.min(index, next.length), 0, removed)
              byWorkspace[wsId] = next
            }
            restoreTasks()
          }
          toast.error(err)
          throw err
        }),
    )
  }

  function reset(): void {
    generation++
    for (const key of Object.keys(byWorkspace)) delete byWorkspace[key]
    for (const key of Object.keys(meta)) delete meta[key]
    inflight.clear()
    creating.clear()
  }

  return { current, loadError, loading, get, ensure, reload, nextColor, create, whenCreated, update, remove, reset }
})

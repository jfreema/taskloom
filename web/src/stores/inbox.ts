// The Inbox (Features 2): what happened to my tasks, mentions of me and my due-date reminders, in
// the current workspace. The sidebar's badge reads `unread`, refreshed every minute while the tab
// is visible and when it comes back; the Inbox page reads `items`. Reading is optimistic: rows turn
// read at once and turn back if the server refuses.
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, query } from '../api'
import { useToast } from '../composables/useToast'
import { errorStatus, handled } from '../lib/async'
import type { InboxItem, InboxPage, Uuid } from '../types'
import { useWorkspaces } from './workspaces'

export type InboxFilter = 'unread' | 'all'

const PAGE = 50
/** How often the unread count is asked for while the tab is visible. */
export const INBOX_POLL_MS = 60_000
/** Opening the same task again this soon doesn't ask the server to read its rows again. */
const READ_AGAIN_MS = 2_000

export const useInbox = defineStore('inbox', () => {
  const toast = useToast()
  const workspaces = useWorkspaces()

  const items = ref<InboxItem[]>([])
  const unread = ref(0)
  const hasMore = ref(false)
  const filter = ref<InboxFilter>('unread')
  /** The first page for the current filter has arrived. */
  const loaded = ref(false)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref<unknown>(null)
  /** Bumps on every reset, filter change and workspace change: answers to older requests are dropped. */
  let generation = 0
  let countedAt = 0
  let counting: Promise<void> | null = null
  let lastRead: { taskId: Uuid; at: number } | null = null

  const hasUnreadShown = computed(() => items.value.some((n) => !n.readAt))

  function wsId(): Uuid | null {
    return workspaces.currentId ?? null
  }

  function reset(): void {
    generation++
    items.value = []
    unread.value = 0
    hasMore.value = false
    loaded.value = false
    loading.value = false
    loadingMore.value = false
    error.value = null
    countedAt = 0
    counting = null
    lastRead = null
  }

  /** GET …/notifications/count, for the badge. Quiet: a failure keeps the last number. */
  function refreshCount(o: { ifOlderThan?: number } = {}): Promise<void> {
    const id = wsId()
    if (!id) return Promise.resolve()
    if (o.ifOlderThan !== undefined && Date.now() - countedAt < o.ifOlderThan) return Promise.resolve()
    if (counting) return counting
    const gen = generation
    counting = api
      .get<{ unread: number }>(`/api/workspaces/${id}/notifications/count`)
      .then(
        (r) => {
          if (gen !== generation) return
          const changed = r.unread !== unread.value
          unread.value = r.unread
          countedAt = Date.now()
          // New rows while the Inbox is on screen: bring them in.
          if (changed && loaded.value && !loading.value) void load({ quiet: true })
        },
        () => undefined,
      )
      .finally(() => {
        counting = null
      })
    return counting
  }

  /** The first page for the current filter. Never rejects (see `error`). */
  function load(o: { quiet?: boolean } = {}): Promise<void> {
    const id = wsId()
    if (!id) return Promise.resolve()
    const gen = generation
    if (!o.quiet) loading.value = true
    return api
      .get<InboxPage>(`/api/workspaces/${id}/notifications${query({ unread: filter.value === 'unread' ? true : null, limit: PAGE })}`)
      .then(
        (page) => {
          if (gen !== generation) return
          // A quiet refresh keeps rows read during this visit on screen (the Unread view would drop them).
          const kept = o.quiet && filter.value === 'unread' ? items.value.filter((n) => n.readAt && !page.items.some((p) => p.id === n.id)) : []
          items.value = [...page.items, ...kept].sort(newestFirst)
          unread.value = page.unread
          hasMore.value = page.hasMore
          countedAt = Date.now()
          loaded.value = true
          error.value = null
        },
        (err: unknown) => {
          if (gen !== generation) return
          if (!o.quiet) error.value = err
        },
      )
      .finally(() => {
        if (gen === generation && !o.quiet) loading.value = false
      })
  }

  function newestFirst(a: InboxItem, b: InboxItem): number {
    return Date.parse(b.createdAt) - Date.parse(a.createdAt) || (a.id < b.id ? 1 : a.id > b.id ? -1 : 0)
  }

  /** The next page, older than the last row shown. */
  function loadMore(): Promise<void> {
    const id = wsId()
    const last = items.value[items.value.length - 1]
    if (!id || !last || loadingMore.value || !hasMore.value) return Promise.resolve()
    const gen = generation
    loadingMore.value = true
    return handled(
      api
        .get<InboxPage>(
          `/api/workspaces/${id}/notifications${query({ unread: filter.value === 'unread' ? true : null, limit: PAGE, before: last.createdAt })}`,
        )
        .then(
          (page) => {
            if (gen !== generation) return
            const seen = new Set(items.value.map((n) => n.id))
            items.value = [...items.value, ...page.items.filter((n) => !seen.has(n.id))]
            unread.value = page.unread
            hasMore.value = page.hasMore
          },
          (err: unknown) => {
            if (gen === generation) toast.error(err, { retry: () => void loadMore() })
          },
        )
        .finally(() => {
          if (gen === generation) loadingMore.value = false
        }),
    )
  }

  function setFilter(next: InboxFilter): void {
    if (filter.value === next) return
    filter.value = next
    generation++
    items.value = []
    hasMore.value = false
    loaded.value = false
    error.value = null
    void load()
  }

  /** Applies read or unread to rows on screen and to the count; returns how to undo it. */
  function applyLocal(match: (n: InboxItem) => boolean, read: boolean): () => void {
    const before = new Map<Uuid, string | null>()
    const now = new Date().toISOString()
    let delta = 0
    items.value = items.value.map((n) => {
      if (!match(n) || !!n.readAt === read) return n
      before.set(n.id, n.readAt)
      delta += read ? -1 : 1
      return { ...n, readAt: read ? now : null }
    })
    const previous = unread.value
    unread.value = Math.max(0, previous + delta)
    return () => {
      items.value = items.value.map((n) => (before.has(n.id) ? { ...n, readAt: before.get(n.id) ?? null } : n))
      unread.value = previous
    }
  }

  /** Marks rows read, or unread. */
  function mark(ids: readonly Uuid[], read = true): Promise<void> {
    if (!ids.length) return Promise.resolve()
    const set = new Set(ids)
    const undo = applyLocal((n) => set.has(n.id), read)
    return handled(
      api.post<void>('/api/notifications/read', { ids: [...set], read }).catch((err: unknown) => {
        undo()
        if (errorStatus(err) !== 401) toast.error(err, { retry: () => void mark(ids, read) })
        throw err
      }),
    )
  }

  /**
   * Opening a task reads everything about it. Asked only while something is unread; the count is
   * then fetched again, because rows about the task may not be on screen.
   */
  function markTaskRead(taskId: Uuid): void {
    if (unread.value === 0 && !items.value.some((n) => n.taskId === taskId && !n.readAt)) return
    // Clicking an Inbox row and the task opening both ask: once is enough.
    const now = Date.now()
    if (lastRead?.taskId === taskId && now - lastRead.at < READ_AGAIN_MS) return
    lastRead = { taskId, at: now }
    const gen = generation
    applyLocal((n) => n.taskId === taskId, true)
    api.post<void>('/api/notifications/read', { taskId }).then(
      () => {
        if (gen === generation) void refreshCount()
      },
      () => {
        if (gen === generation) void refreshCount()
      },
    )
  }

  /** Marks everything up to the newest row shown as read. */
  function markAllRead(): Promise<void> {
    const id = wsId()
    if (!id) return Promise.resolve()
    const gen = generation
    const newest = items.value[0]?.createdAt
    const undo = applyLocal(() => true, true)
    unread.value = 0
    return handled(
      api.post<void>(`/api/workspaces/${id}/notifications/read-all`, newest ? { before: newest } : {}).then(
        () => {
          if (gen === generation) void refreshCount()
        },
        (err: unknown) => {
          if (gen !== generation) return
          undo()
          if (errorStatus(err) !== 401) toast.error(err, { retry: () => void markAllRead() })
          throw err
        },
      ),
    )
  }

  return {
    items, unread, hasMore, filter, loaded, loading, loadingMore, error, hasUnreadShown,
    reset, refreshCount, load, loadMore, setFilter, mark, markTaskRead, markAllRead,
  }
})

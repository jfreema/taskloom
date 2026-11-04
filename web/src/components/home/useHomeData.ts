// Home's data (8.5): the "mine" load with its fresh-data refresh (8.1), the persisted scope and
// collapsed sections, and the sections computed from the tasks store.
//
// What Home last loaded is kept for the app's life (not per mount), so coming back to Home renders
// the store at once and refreshes silently when the data is older than 30s. Any reset of the tasks
// store (a workspace switch, sign-out) forgets it, so a fresh store always gets a full load.
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch, type ComputedRef, type Ref } from 'vue'
import { useToday } from '../../composables/useToday'
import { isAbortError } from '../../lib/async'
import { addDaysIso } from '../../lib/dates'
import { readJson, readString, userKey, writeJson, writeString } from '../../lib/storage'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task, Uuid } from '../../types'
import {
  DEFAULT_COLLAPSED,
  HOME_SECTIONS,
  buildSections,
  completedThisWeek,
  isMine,
  type HomeScope,
  type HomeSectionKey,
  type HomeSections,
} from './homeSections'

/** Data on screen older than this is refetched when the window regains focus or Home mounts again (8.1). */
const STALE_MS = 30_000

/** The last complete Home load: which workspace and user, and when. */
const lastLoad = shallowRef<{ wsId: Uuid; userId: Uuid; at: number } | null>(null)
let watchingResets = false

export interface HomeData {
  /** The first load for the current workspace is running (nothing to show yet). */
  loading: ComputedRef<boolean>
  /** The first load failed (a failed refresh keeps the old content). */
  error: Ref<unknown>
  /** The current workspace's tasks are loaded. */
  ready: ComputedRef<boolean>
  load(o?: { silent?: boolean }): Promise<void>
  scope: Ref<HomeScope>
  collapsed: Ref<Record<HomeSectionKey, boolean>>
  toggleSection(key: HomeSectionKey): void
  expand(key: HomeSectionKey): void
  sections: ComputedRef<HomeSections>
  /** Any task of mine (All mine) that is open or was completed this week: none means first run. */
  hasAnyTask: ComputedRef<boolean>
  /** Open tasks in the current scope. */
  openCount: ComputedRef<number>
  /** Open tasks due today and overdue, in the current scope, for the subline. */
  dueTodayCount: ComputedRef<number>
  overdueCount: ComputedRef<number>
}

export function useHomeData(): HomeData {
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const session = useSession()
  const today = useToday()

  if (!watchingResets) {
    watchingResets = true
    // Detached: it outlives this mount, like `lastLoad`.
    tasks.$onAction(({ name }) => {
      if (name === 'reset') lastLoad.value = null
    }, true)
  }

  const meId = computed(() => session.user?.id ?? null)

  // ---- Loading ----

  const running = ref(false)
  const error = ref<unknown>(null)
  let controller: AbortController | null = null

  const ready = computed(() => {
    const l = lastLoad.value
    return !!l && l.wsId === workspaces.currentId && l.userId === meId.value
  })
  const loading = computed(() => running.value && !ready.value && !error.value)

  async function load(o: { silent?: boolean } = {}): Promise<void> {
    const wsId = workspaces.currentId
    const userId = meId.value
    if (!wsId || !userId) return
    controller?.abort()
    const c = new AbortController()
    controller = c
    running.value = true
    if (!o.silent) error.value = null
    try {
      await tasks.loadWorkspaceTasks(
        wsId,
        { scope: 'mine', includeDone: false, doneSince: addDaysIso(today.value, -7) },
        { signal: c.signal },
      )
      if (controller !== c || workspaces.currentId !== wsId) return
      lastLoad.value = { wsId, userId, at: Date.now() }
      error.value = null
    } catch (err) {
      if (controller !== c || isAbortError(err)) return
      // A silent refresh keeps what is on screen; only a page without data shows the error.
      if (!o.silent || !ready.value) error.value = err
    } finally {
      if (controller === c) {
        controller = null
        running.value = false
      }
    }
  }

  function isStale(): boolean {
    const l = lastLoad.value
    return !l || Date.now() - l.at > STALE_MS
  }

  // A workspace (or user) Home hasn't loaded yet gets a full load; the same one, a silent refresh
  // when it is stale. `immediate` covers the mount.
  watch(
    [() => workspaces.currentId, meId],
    () => {
      if (!ready.value) void load()
      else if (isStale()) void load({ silent: true })
    },
    { immediate: true },
  )
  // A new day moves the "completed this week" window.
  watch(today, () => void load({ silent: true }))

  function refreshIfStale(): void {
    if (document.visibilityState === 'hidden' || !ready.value || running.value) return
    if (isStale()) void load({ silent: true })
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
    controller?.abort()
    controller = null
  })

  // ---- Scope and collapsed sections (per user) ----

  const scope = ref<HomeScope>('mine')
  const collapsed = ref<Record<HomeSectionKey, boolean>>({ ...DEFAULT_COLLAPSED })

  function readPrefs(userId: Uuid | null): void {
    if (!userId) return
    scope.value = readString(userKey('taskloom.home.scope', userId)) === 'assigned' ? 'assigned' : 'mine'
    const stored = readJson<unknown>(userKey('taskloom.home.collapsed', userId), null)
    const next = { ...DEFAULT_COLLAPSED }
    if (stored && typeof stored === 'object') {
      for (const key of HOME_SECTIONS) {
        const v = (stored as Record<string, unknown>)[key]
        if (typeof v === 'boolean') next[key] = v
      }
    }
    collapsed.value = next
  }

  watch(meId, readPrefs, { immediate: true })

  watch(scope, (s) => {
    if (meId.value) writeString(userKey('taskloom.home.scope', meId.value), s)
  })

  function setCollapsed(key: HomeSectionKey, value: boolean): void {
    if (collapsed.value[key] === value) return
    collapsed.value = { ...collapsed.value, [key]: value }
    if (meId.value) writeJson(userKey('taskloom.home.collapsed', meId.value), collapsed.value)
  }

  function toggleSection(key: HomeSectionKey): void {
    setCollapsed(key, !collapsed.value[key])
  }

  function expand(key: HomeSectionKey): void {
    setCollapsed(key, false)
  }

  // ---- Sections ----

  const listIds = computed(() => new Set(workspaces.allLists.map((x) => x.list.id)))

  /** All mine (open, or completed this week) in the current workspace, pending deletes left out. */
  const mine = computed<Task[]>(() => {
    const me = meId.value
    if (!me || !ready.value) return []
    const lists = listIds.value
    const held = tasks.recentlyCompleted
    const out: Task[] = []
    for (const t of Object.values(tasks.byId)) {
      if (!lists.has(t.listId) || tasks.isPendingDelete(t.id) || !isMine(t, me, 'mine')) continue
      if (tasks.isDone(t) && !held.has(t.id) && !completedThisWeek(t, today.value)) continue
      out.push(t)
    }
    return out
  })

  const sections = computed<HomeSections>(() => {
    const me = meId.value
    const scoped = scope.value === 'assigned' && me ? mine.value.filter((t) => isMine(t, me, 'assigned')) : mine.value
    return buildSections({ tasks: scoped, today: today.value, isDone: (t) => tasks.isDone(t), held: tasks.recentlyCompleted })
  })

  const hasAnyTask = computed(() => mine.value.length > 0)
  const countOpen = (list: Task[]) => list.reduce((n, t) => (tasks.isDone(t) ? n : n + 1), 0)
  const dueTodayCount = computed(() => countOpen(sections.value.today))
  const overdueCount = computed(() => countOpen(sections.value.overdue))
  const openCount = computed(() =>
    HOME_SECTIONS.reduce((n, key) => (key === 'completed' ? n : n + countOpen(sections.value[key])), 0),
  )

  return {
    loading,
    error,
    ready,
    load,
    scope,
    collapsed,
    toggleSection,
    expand,
    sections,
    hasAnyTask,
    openCount,
    dueTodayCount,
    overdueCount,
  }
}

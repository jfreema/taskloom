// Home's data (8.5): the "mine" load with its fresh-data refresh (8.1), the persisted scope and
// collapsed sections, and the sections computed from the tasks store.
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
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

/** Data on screen older than this is refetched when the window regains focus (8.1). */
const STALE_MS = 30_000

export interface HomeData {
  /** The first load for the current workspace is running. */
  loading: ComputedRef<boolean>
  /** The first load failed (a failed refresh keeps the old content). */
  error: Ref<unknown>
  /** The current workspace's tasks are loaded. */
  ready: ComputedRef<boolean>
  load(o?: { silent?: boolean }): Promise<void>
  scope: Ref<HomeScope>
  collapsed: Ref<Record<HomeSectionKey, boolean>>
  toggleSection(key: HomeSectionKey): void
  sections: ComputedRef<HomeSections>
  /** Every task of mine (All mine) that is open or was completed this week: none means first run. */
  hasAnyTask: ComputedRef<boolean>
  /** Open tasks due today and overdue, in the current scope, for the subline. */
  dueTodayCount: ComputedRef<number>
  overdueCount: ComputedRef<number>
}

export function useHomeData(): HomeData {
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const session = useSession()
  const today = useToday()

  const meId = computed(() => session.user?.id ?? null)

  // ---- Loading ----

  const running = ref(false)
  const error = ref<unknown>(null)
  const loadedWs = ref<Uuid | null>(null)
  let loadedAt = 0
  let controller: AbortController | null = null

  const ready = computed(() => !!workspaces.currentId && loadedWs.value === workspaces.currentId)
  const loading = computed(() => running.value && !ready.value && !error.value)

  async function load(o: { silent?: boolean } = {}): Promise<void> {
    const wsId = workspaces.currentId
    if (!wsId) return
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
      if (controller !== c) return
      loadedWs.value = wsId
      loadedAt = Date.now()
      error.value = null
    } catch (err) {
      if (controller !== c || isAbortError(err)) return
      // A silent refresh keeps what is on screen; only a page without data shows the error.
      if (!o.silent || loadedWs.value !== wsId) error.value = err
    } finally {
      if (controller === c) {
        controller = null
        running.value = false
      }
    }
  }

  watch(() => workspaces.currentId, () => void load(), { immediate: true })
  // A new day moves the "completed this week" window.
  watch(today, () => void load({ silent: true }))

  function refreshIfStale(): void {
    if (document.visibilityState === 'hidden' || !ready.value || running.value) return
    if (Date.now() - loadedAt > STALE_MS) void load({ silent: true })
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

  function toggleSection(key: HomeSectionKey): void {
    collapsed.value = { ...collapsed.value, [key]: !collapsed.value[key] }
    if (meId.value) writeJson(userKey('taskloom.home.collapsed', meId.value), collapsed.value)
  }

  // ---- Sections ----

  const listIds = computed(() => new Set(workspaces.allLists.map((x) => x.list.id)))

  /** All mine (open, or completed this week) in the current workspace, pending deletes left out. */
  const mine = computed<Task[]>(() => {
    const me = meId.value
    if (!me || !ready.value) return []
    const lists = listIds.value
    const out: Task[] = []
    for (const t of Object.values(tasks.byId)) {
      if (!lists.has(t.listId) || tasks.isPendingDelete(t.id) || !isMine(t, me, 'mine')) continue
      if (tasks.isDone(t) && !tasks.recentlyCompleted.has(t.id) && !completedThisWeek(t, today.value)) continue
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
  const openCount = (list: Task[]) => list.filter((t) => !tasks.isDone(t)).length
  const dueTodayCount = computed(() => openCount(sections.value.today))
  const overdueCount = computed(() => openCount(sections.value.overdue))

  return { loading, error, ready, load, scope, collapsed, toggleSection, sections, hasAnyTask, dueTodayCount, overdueCount }
}

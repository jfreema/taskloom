// Loads the task detail behind TaskDetail (8.10). The title and properties come from the task store;
// this holds the rest: description, creator, comments, attachments and activity.
// - A task seen before renders at once from the cache, then refreshes (unless TaskPage fetched it
//   moments ago).
// - Fresh activity: every store write to the task bumps tasks.changedAt[id]; the detail refetches
//   400ms after the last bump (fetchDetail queues behind the task's own requests, so it sees them).
// - Back on the tab (focus or visibility) after more than 30s, it refetches (8.1).
// - A 404 with no pending create marks the task missing.
import { onBeforeUnmount, onMounted, ref, shallowRef, watch, type Ref, type ShallowRef } from 'vue'
import { errorStatus } from '../../lib/async'
import { useRecents } from '../../stores/recents'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { TaskDetail, Uuid } from '../../types'
import { cacheDetail, cachedDetail, forgetDetail, settleLocal } from './taskDetailState'

const REFRESH_MS = 400
const STALE_MS = 30_000
/** A cached detail this young (TaskPage has just fetched it) needs no fetch on mount. */
const FRESH_MS = 3_000

export interface TaskDetailData {
  /** The latest detail of the current task, or null until one arrives. */
  detail: ShallowRef<TaskDetail | null>
  /** performance.now() when the request behind `detail` was queued (the editor ignores older ones). */
  requestedAt: Ref<number>
  /** A request is running. */
  loading: Ref<boolean>
  /** The server answered 404 and no create is pending. */
  missing: Ref<boolean>
  /** The last request failed (and the task isn't missing). */
  error: Ref<unknown>
  reload(): void
}

export function useTaskDetailData(taskId: Ref<Uuid>): TaskDetailData {
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const session = useSession()
  const recents = useRecents()

  const detail = shallowRef<TaskDetail | null>(null)
  const requestedAt = ref(0)
  const loading = ref(false)
  const missing = ref(false)
  const error = ref<unknown>(null)

  /** Bumps when the task changes, so answers for the previous task are dropped. */
  let gen = 0
  /** The generation whose request is running, or -1. */
  let running = -1
  let again = false
  let timer: ReturnType<typeof setTimeout> | undefined
  let fetchedAt = 0

  function clearTimer(): void {
    if (timer !== undefined) clearTimeout(timer)
    timer = undefined
  }

  function run(): void {
    clearTimer()
    if (running === gen) {
      again = true
      return
    }
    const id = taskId.value
    const mine = gen
    running = mine
    again = false
    loading.value = true
    const at = performance.now()
    tasks.fetchDetail(id).then(
      (d) => {
        if (mine !== gen) return
        fetchedAt = Date.now()
        cacheDetail(session.user?.id, d)
        settleLocal(d)
        detail.value = d
        requestedAt.value = at
        missing.value = false
        error.value = null
        if (d.workspaceId === workspaces.currentId) recents.touchTask(tasks.get(id) ?? d.task)
      },
      (err: unknown) => {
        if (mine !== gen) return
        if (errorStatus(err) === 404 && !tasks.isCreatePending(id)) {
          forgetDetail(session.user?.id, id)
          missing.value = true
          detail.value = null
        } else {
          error.value = err
        }
      },
    ).finally(() => {
      if (running === mine) running = -1
      if (mine !== gen) return
      loading.value = false
      if (again) run()
    })
  }

  function schedule(): void {
    clearTimer()
    timer = setTimeout(run, REFRESH_MS)
  }

  watch(
    taskId,
    (id) => {
      gen++
      again = false
      clearTimer()
      const cached = cachedDetail(session.user?.id, id)
      detail.value = cached?.detail ?? null
      requestedAt.value = 0
      missing.value = false
      error.value = null
      loading.value = false
      fetchedAt = cached?.at ?? 0
      if (cached && Date.now() - cached.at < FRESH_MS) {
        settleLocal(cached.detail)
        if (cached.detail.workspaceId === workspaces.currentId) recents.touchTask(tasks.get(id) ?? cached.detail.task)
        return
      }
      run()
    },
    { immediate: true },
  )

  // Any write to this task (its PATCH, a move, a bulk change, comment and attachment counts).
  watch(
    () => [taskId.value, tasks.changedAt[taskId.value]] as const,
    ([id, at], [prevId, prevAt]) => {
      if (id !== prevId || at === undefined || at === prevAt || missing.value) return
      schedule()
    },
  )

  function onReturn(): void {
    if (document.visibilityState !== 'visible' || missing.value) return
    if (Date.now() - fetchedAt > STALE_MS) run()
  }

  onMounted(() => {
    window.addEventListener('focus', onReturn)
    document.addEventListener('visibilitychange', onReturn)
  })
  onBeforeUnmount(() => {
    gen++
    clearTimer()
    window.removeEventListener('focus', onReturn)
    document.removeEventListener('visibilitychange', onReturn)
  })

  return { detail, requestedAt, loading, missing, error, reload: run }
}

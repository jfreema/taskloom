// The task store (10.2; rules in 9.1 and 9.2).
// - Every change applies locally first; the request runs in the background and a failure rolls
//   the change back (guarded by a per-task `seq`) and toasts with Retry.
// - Requests for one task run in order through a per-task queue, so anything sent for a task
//   waits for its pending create. Creates for one (list, status, parent) are sent one at a time.
// - Refreshes merge by id and never overwrite a task with a request in flight or a newer local write.
// - The store alone keeps parents' subtaskCount and subtaskDoneCount current.
// - Deletes are delayed with Undo (useDelayedDelete) and committed in chunks of 500.
import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'
import { api, newId, query } from '../api'
import { useDelayedDelete } from '../composables/useDelayedDelete'
import { errorMessage, useToast } from '../composables/useToast'
import { errorStatus, handled, noop } from '../lib/async'
import { pluralize } from '../lib/format'
import { isEmptyHtml } from '../lib/html'
import { between, bottomOf, topOf } from '../lib/positions'
import type {
  BulkTaskUpdate,
  BulkUpdateResponse,
  CreateTaskRequest,
  MoveTaskRequest,
  Status,
  StatusCategory,
  Task,
  TaskDetail,
  TaskPatch,
  TaskRef,
  Uuid,
  WorkspaceTasksQuery,
  WorkspaceTasksResponse,
} from '../types'
import { useRecents } from './recents'
import { useSelection } from './selection'
import { useSession } from './session'
import { useTags } from './tags'
import { useWorkspaces } from './workspaces'

export interface ListMeta {
  /** When the list's tasks last came from the server (ms); 0 before the first load. */
  loadedAt: number
  loading: boolean
  /** The last load failure, or null. A failed refresh keeps the tasks already shown. */
  error: unknown
}

/** An unsaved description (see `descriptionDrafts`). */
export interface DescriptionDraft {
  html: string
  /** False after a failure that sending again can't fix (400, 403): only the user can resolve it. */
  retryable: boolean
}

/** Bulk calls take at most this many items (5.1). */
export const BULK_CHUNK = 500
/** The completion hold (8.1). */
const HOLD_MS = 1000
/** loadList skips a list loaded this recently, unless forced (8.1 "Fresh data"). */
const FRESH_MS = 30_000

const EMPTY_TASKS: Task[] = []

function sameIds(a: readonly Uuid[], b: readonly Uuid[]): boolean {
  return a.length === b.length && a.every((id, i) => id === b[i])
}

/** A queued request skipped because the task's create failed first (its row is already gone). */
class SkippedError extends Error {
  constructor() {
    super('The task was not created.')
    this.name = 'SkippedError'
  }
}

function chunk<T>(items: T[], size: number): T[][] {
  const out: T[][] = []
  for (let i = 0; i < items.length; i += size) out.push(items.slice(i, i + size))
  return out
}

function timeOf(ts: string): number {
  const t = Date.parse(ts)
  return Number.isNaN(t) ? 0 : t
}

/** The server's task order: position, then createdAt, then id (5.2). */
function compareTasks(a: Task, b: Task): number {
  return a.position - b.position || timeOf(a.createdAt) - timeOf(b.createdAt) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}

function nowIso(): string {
  return new Date().toISOString()
}

function copyValue<T>(v: T): T {
  return (Array.isArray(v) ? [...v] : v) as T
}

export const useTasks = defineStore('tasks', () => {
  const toast = useToast()

  // ---- State ----

  const byId = reactive<Record<Uuid, Task>>({})
  const refs = reactive<Record<Uuid, TaskRef>>({})
  const listMeta = reactive<Record<Uuid, ListMeta>>({})
  /** Bumped on every write to a task (8.10 refetch); loads don't bump it. */
  const changedAt = reactive<Record<Uuid, number>>({})
  /** Tasks inside the 1s completion hold (8.1). Replaced, never mutated, so watchers see changes. */
  const recentlyCompleted = ref<Set<Uuid>>(new Set())
  const pendingDeletes = reactive(new Set<Uuid>())
  const creating = reactive(new Set<Uuid>())
  /**
   * Description HTML that the server hasn't accepted yet, by task: a save failed, or the editor
   * closed with an edit not yet saved. The editor restores it when the task opens again, and a
   * draft that failed for want of a network is sent again when the browser comes back online.
   * Kept across `reset()`, so a workspace switch doesn't lose typing.
   */
  const descriptionDrafts = reactive(new Map<Uuid, DescriptionDraft>())

  // Bookkeeping that nothing renders.
  const seq: Record<Uuid, number> = {}
  const inflight: Record<Uuid, number> = {}
  const lastWrite: Record<Uuid, number> = {}
  let clock = 0
  let generation = 0
  const queues = new Map<Uuid, Promise<void>>()
  const createChains = new Map<string, Promise<void>>()
  const failedCreates = new Set<Uuid>()
  const retryToasts = new Map<string, string>()
  const holdTimers = new Map<Uuid, ReturnType<typeof setTimeout>>()
  const listLoads = new Map<Uuid, Promise<void>>()
  /** Tasks with a description editor on screen, which sends its own draft again. */
  const descriptionHolders = new Map<Uuid, number>()

  // ---- Indexes ----

  /** Pending deletes and their subtasks (9.1 rule 12). */
  const hidden = computed(() => {
    const out = new Set<Uuid>()
    if (pendingDeletes.size === 0) return out
    for (const id of pendingDeletes) out.add(id)
    for (const t of Object.values(byId)) if (t.parentTaskId && pendingDeletes.has(t.parentTaskId)) out.add(t.id)
    return out
  })

  const indexes = computed(() => {
    const all = new Map<Uuid, Task[]>()
    const top = new Map<Uuid, Task[]>()
    const children = new Map<Uuid, Task[]>()
    const h = hidden.value
    const push = (m: Map<Uuid, Task[]>, key: Uuid, t: Task) => {
      const arr = m.get(key)
      if (arr) arr.push(t)
      else m.set(key, [t])
    }
    for (const t of Object.values(byId)) {
      if (h.has(t.id)) continue
      push(all, t.listId, t)
      if (t.parentTaskId) push(children, t.parentTaskId, t)
      else push(top, t.listId, t)
    }
    for (const m of [all, top, children]) for (const arr of m.values()) arr.sort(compareTasks)
    return { all, top, children }
  })

  // ---- Reads ----

  function get(id: Uuid): Task | undefined {
    return byId[id]
  }

  /** True for a task with a pending delete, and for the subtasks of one. */
  function isPendingDelete(id: Uuid): boolean {
    if (pendingDeletes.has(id)) return true
    const parentId = byId[id]?.parentTaskId
    return !!parentId && pendingDeletes.has(parentId)
  }

  function statusOf(statusId: Uuid): Status | undefined {
    return useWorkspaces().statusById(statusId)
  }

  function isDoneTask(t: Task): boolean {
    const s = statusOf(t.statusId)
    return s ? s.category === 'Done' : t.completedAt !== null
  }

  /** Its status's category is Done (not completedAt). */
  function isDone(t: Task | Uuid): boolean {
    const task = typeof t === 'string' ? byId[t] : t
    return task ? isDoneTask(task) : false
  }

  function isCreatePending(id: Uuid): boolean {
    return creating.has(id)
  }

  /** Every task of the list, all levels, in server order; pending deletes and their subtasks excluded. */
  function inList(listId: Uuid): Task[] {
    return indexes.value.all.get(listId) ?? EMPTY_TASKS
  }

  function topLevelInList(listId: Uuid): Task[] {
    return indexes.value.top.get(listId) ?? EMPTY_TASKS
  }

  /** By position, createdAt, id. */
  function subtasksOf(parentId: Uuid): Task[] {
    return indexes.value.children.get(parentId) ?? EMPTY_TASKS
  }

  // ---- Write bookkeeping ----

  function begin(id: Uuid): number {
    inflight[id] = (inflight[id] ?? 0) + 1
    lastWrite[id] = ++clock
    changedAt[id] = Date.now()
    seq[id] = (seq[id] ?? 0) + 1
    return seq[id]
  }

  function settle(id: Uuid): void {
    inflight[id] = Math.max(0, (inflight[id] ?? 1) - 1)
    lastWrite[id] = ++clock
    changedAt[id] = Date.now()
  }

  /** A refresh started at `since` must not overwrite this task. */
  function isProtected(id: Uuid, since: number): boolean {
    return (inflight[id] ?? 0) > 0 || (lastWrite[id] ?? 0) > since || creating.has(id)
  }

  /** Parents whose server counts can't be trusted yet: a child has a request in flight or a pending delete. */
  function parentsWithBusyChildren(): Set<Uuid> {
    const out = new Set<Uuid>()
    for (const t of Object.values(byId)) {
      if (t.parentTaskId && ((inflight[t.id] ?? 0) > 0 || creating.has(t.id) || pendingDeletes.has(t.id))) out.add(t.parentTaskId)
    }
    return out
  }

  /**
   * Writes a server row. `keepCounts` keeps the local subtask counts (a child is still busy). Id
   * lists that haven't changed keep their arrays, so a refresh re-renders only the tasks it changed
   * (reactive writes of equal plain values trigger nothing).
   */
  function writeRow(t: Task, keepCounts = false): void {
    const existing = byId[t.id]
    if (existing) {
      const count = existing.subtaskCount
      const done = existing.subtaskDoneCount
      const assigneeIds = sameIds(existing.assigneeIds, t.assigneeIds) ? existing.assigneeIds : [...t.assigneeIds]
      const tagIds = sameIds(existing.tagIds, t.tagIds) ? existing.tagIds : [...t.tagIds]
      Object.assign(existing, t, { assigneeIds, tagIds })
      if (keepCounts) {
        existing.subtaskCount = count
        existing.subtaskDoneCount = done
      }
    } else {
      byId[t.id] = { ...t, assigneeIds: [...t.assigneeIds], tagIds: [...t.tagIds] }
    }
  }

  function clearHold(id: Uuid): void {
    const timer = holdTimers.get(id)
    if (timer !== undefined) {
      clearTimeout(timer)
      holdTimers.delete(id)
    }
    if (recentlyCompleted.value.has(id)) {
      const next = new Set(recentlyCompleted.value)
      next.delete(id)
      recentlyCompleted.value = next
    }
  }

  /** Removes a row and its bookkeeping. Parent counts are the caller's business. */
  function dropRow(id: Uuid): void {
    delete byId[id]
    pendingDeletes.delete(id)
    creating.delete(id)
    delete inflight[id]
    delete lastWrite[id]
    clearHold(id)
  }

  /**
   * Removes a task (and its subtasks) that no longer exists. `adjustParent` takes it off its
   * parent's counts; refresh merges don't, because the parent's fresh row already has them.
   */
  function removeLocal(id: Uuid, o: { adjustParent: boolean; forget?: boolean }): void {
    const t = byId[id]
    if (!t) return
    for (const s of Object.values(byId)) if (s.parentTaskId === id) dropRow(s.id)
    if (o.adjustParent && t.parentTaskId && !pendingDeletes.has(id)) {
      const p = byId[t.parentTaskId]
      if (p) {
        p.subtaskCount = Math.max(0, p.subtaskCount - 1)
        if (t.completedAt !== null) p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount - 1)
      }
    }
    dropRow(id)
    if (o.forget) useRecents().forget(id)
  }

  // ---- Snapshots ----

  /** Records the fields it changes so the change can be undone exactly; parent counts as deltas. */
  class Mutation {
    readonly snaps = new Map<Uuid, Partial<Task>>()
    readonly deltas: { childId: Uuid; parentId: Uuid; count: number; done: number }[] = []

    set<K extends keyof Task>(t: Task, key: K, value: Task[K]): void {
      let snap = this.snaps.get(t.id)
      if (!snap) {
        snap = {}
        this.snaps.set(t.id, snap)
      }
      if (!(key in snap)) snap[key] = copyValue(t[key])
      t[key] = value
    }

    counts(childId: Uuid, parentId: Uuid, count: number, done: number): void {
      const p = byId[parentId]
      if (!p || (count === 0 && done === 0)) return
      p.subtaskCount = Math.max(0, p.subtaskCount + count)
      p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount + done)
      this.deltas.push({ childId, parentId, count, done })
    }

    /** Restores every snapshot and count delta, except those of tasks `skip` returns true for. */
    revert(skip?: (id: Uuid) => boolean): void {
      for (const [id, snap] of this.snaps) {
        if (skip?.(id)) continue
        const t = byId[id]
        if (t) Object.assign(t, snap)
      }
      for (const d of this.deltas) {
        if (skip?.(d.childId)) continue
        const p = byId[d.parentId]
        if (!p) continue
        p.subtaskCount = Math.max(0, p.subtaskCount - d.count)
        p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount - d.done)
      }
    }
  }

  // ---- Local rules (mirroring the server, 5.5) ----

  function siblingsOf(listId: Uuid, statusId: Uuid, parentId: Uuid | null, exceptId: Uuid): Task[] {
    const out: Task[] = []
    for (const t of Object.values(byId)) {
      if (t.id !== exceptId && t.listId === listId && t.statusId === statusId && t.parentTaskId === parentId) out.push(t)
    }
    return out.sort(compareTasks)
  }

  function positionsOf(tasks: Task[]): number[] {
    return tasks.map((t) => t.position)
  }

  /** The completion hold: the task keeps its place for 1s after becoming done (8.1). */
  function hold(id: Uuid): void {
    const next = new Set(recentlyCompleted.value)
    next.add(id)
    recentlyCompleted.value = next
    const timer = holdTimers.get(id)
    if (timer !== undefined) clearTimeout(timer)
    holdTimers.set(
      id,
      setTimeout(() => {
        holdTimers.delete(id)
        const rest = new Set(recentlyCompleted.value)
        rest.delete(id)
        recentlyCompleted.value = rest
      }, HOLD_MS),
    )
  }

  /**
   * A status change: the top of the new status unless `position` is given; completedAt is set when
   * moving into Done and cleared when moving out; the parent's done count follows.
   */
  function applyStatus(m: Mutation, t: Task, statusId: Uuid, position?: number): void {
    const wasDoneCategory = isDoneTask(t)
    const wasCompleted = t.completedAt !== null
    const target = statusOf(statusId)
    const nowDone = target ? target.category === 'Done' : wasDoneCategory
    if (t.statusId !== statusId) {
      const pos = position ?? topOf(positionsOf(siblingsOf(t.listId, statusId, t.parentTaskId, t.id)))
      m.set(t, 'statusId', statusId)
      m.set(t, 'position', pos)
    } else if (position !== undefined) {
      m.set(t, 'position', position)
    }
    completedFor(m, t, nowDone, wasDoneCategory)
    const isCompleted = t.completedAt !== null
    if (t.parentTaskId && wasCompleted !== isCompleted) m.counts(t.id, t.parentTaskId, 0, isCompleted ? 1 : -1)
    if (isCompleted && !wasCompleted) hold(t.id)
  }

  /** The server's status mapping for a list move: same name, else same category, else the first status. */
  function mapStatus(fromStatusId: Uuid, toListId: Uuid): Uuid | undefined {
    const targets = useWorkspaces().statusesByPosition(toListId)
    if (!targets.length) return undefined
    const from = statusOf(fromStatusId)
    if (from) {
      const name = from.name.toLocaleLowerCase()
      const sameName = targets.find((s) => s.name.toLocaleLowerCase() === name)
      if (sameName) return sameName.id
      const sameCategory = targets.find((s) => s.category === from.category)
      if (sameCategory) return sameCategory.id
    }
    return targets[0].id
  }

  /** completedAt like the server: set when entering Done from another category, cleared outside Done. */
  function completedFor(m: Mutation, t: Task, nowDone: boolean, wasDoneCategory: boolean): void {
    if (nowDone) {
      if (!wasDoneCategory || t.completedAt === null) m.set(t, 'completedAt', nowIso())
    } else if (t.completedAt !== null) {
      m.set(t, 'completedAt', null)
    }
  }

  /**
   * A list move of a top-level task: statuses mapped by the server's rule (unless `statusId` is
   * given), the bottom of the target status, and the subtasks moved and mapped too.
   */
  function applyList(m: Mutation, t: Task, listId: Uuid, statusId?: Uuid, position?: number): void {
    if (t.listId === listId) {
      if (statusId !== undefined) applyStatus(m, t, statusId, position)
      else if (position !== undefined) m.set(t, 'position', position)
      return
    }
    const subs = Object.values(byId).filter((s) => s.parentTaskId === t.id)
    const subTargets = subs.map((s) => mapStatus(s.statusId, listId) ?? s.statusId)
    const subWasDone = subs.map((s) => isDoneTask(s))
    const target = statusId ?? mapStatus(t.statusId, listId) ?? t.statusId
    const wasDoneCategory = isDoneTask(t)
    const pos = position ?? bottomOf(positionsOf(siblingsOf(listId, target, null, t.id)))
    m.set(t, 'listId', listId)
    m.set(t, 'statusId', target)
    m.set(t, 'position', pos)
    completedFor(m, t, statusOf(target)?.category === 'Done', wasDoneCategory)
    subs.forEach((s, i) => {
      const wasCompleted = s.completedAt !== null
      m.set(s, 'listId', listId)
      m.set(s, 'statusId', subTargets[i])
      completedFor(m, s, statusOf(subTargets[i])?.category === 'Done', subWasDone[i])
      const isCompleted = s.completedAt !== null
      if (wasCompleted !== isCompleted) m.counts(s.id, t.id, 0, isCompleted ? 1 : -1)
    })
  }

  /** The server's assignee order: kept assignees first, new ones after, by id (5.2). */
  function orderAssignees(existing: Uuid[], next: Uuid[]): Uuid[] {
    const want = new Set(next)
    const kept = existing.filter((id, i) => want.has(id) && existing.indexOf(id) === i)
    const keptSet = new Set(kept)
    const added = [...want].filter((id) => !keptSet.has(id)).sort()
    return [...kept, ...added]
  }

  /** The server's tag order: by name, ignoring case (5.2); unknown tags last. */
  function orderTags(ids: Uuid[]): Uuid[] {
    const tags = useTags()
    return [...new Set(ids)].sort((a, b) => {
      const ta = tags.get(a)
      const tb = tags.get(b)
      if (ta && tb) return ta.name.localeCompare(tb.name, undefined, { sensitivity: 'base' }) || (a < b ? -1 : 1)
      if (ta) return -1
      if (tb) return 1
      return a < b ? -1 : a > b ? 1 : 0
    })
  }

  function applyPatch(m: Mutation, t: Task, patch: TaskPatch): void {
    if (patch.title !== undefined) m.set(t, 'title', patch.title.trim())
    if (patch.description !== undefined) m.set(t, 'hasDescription', !isEmptyHtml(patch.description))
    if (patch.priority !== undefined) m.set(t, 'priority', patch.priority ?? 'None')
    if (patch.startDate !== undefined) m.set(t, 'startDate', patch.startDate)
    if (patch.dueDate !== undefined) m.set(t, 'dueDate', patch.dueDate)
    if (patch.assigneeIds !== undefined) m.set(t, 'assigneeIds', orderAssignees(t.assigneeIds, patch.assigneeIds))
    if (patch.tagIds !== undefined) m.set(t, 'tagIds', orderTags(patch.tagIds))
    if (patch.listId !== undefined && patch.listId !== t.listId) applyList(m, t, patch.listId, patch.statusId, patch.position)
    else if (patch.statusId !== undefined) applyStatus(m, t, patch.statusId, patch.position)
    else if (patch.position !== undefined) m.set(t, 'position', patch.position)
  }

  /** The patch that puts the task back as it is now (undo). Descriptions have none. */
  function inversePatch(t: Task, patch: TaskPatch): TaskPatch {
    const inv: TaskPatch = {}
    if (patch.title !== undefined) inv.title = t.title
    if (patch.priority !== undefined) inv.priority = t.priority
    if (patch.startDate !== undefined) inv.startDate = t.startDate
    if (patch.dueDate !== undefined) inv.dueDate = t.dueDate
    if (patch.assigneeIds !== undefined) inv.assigneeIds = [...t.assigneeIds]
    if (patch.tagIds !== undefined) inv.tagIds = [...t.tagIds]
    if (patch.listId !== undefined && patch.listId !== t.listId) {
      inv.listId = t.listId
      inv.statusId = t.statusId
      inv.position = t.position
    } else if (patch.statusId !== undefined || patch.position !== undefined) {
      inv.statusId = t.statusId
      inv.position = t.position
    }
    return inv
  }

  function applyBulk(m: Mutation, t: Task, u: BulkTaskUpdate): void {
    if (u.priority !== undefined) m.set(t, 'priority', u.priority ?? 'None')
    if (u.startDate !== undefined) m.set(t, 'startDate', u.startDate)
    if (u.dueDate !== undefined) m.set(t, 'dueDate', u.dueDate)
    if (u.assigneeIds !== undefined) m.set(t, 'assigneeIds', orderAssignees(t.assigneeIds, u.assigneeIds))
    else if (u.addAssigneeIds || u.removeAssigneeIds) {
      const remove = new Set(u.removeAssigneeIds ?? [])
      const next = t.assigneeIds.filter((id) => !remove.has(id))
      for (const id of u.addAssigneeIds ?? []) if (!next.includes(id)) next.push(id)
      m.set(t, 'assigneeIds', orderAssignees(t.assigneeIds, next))
    }
    if (u.tagIds !== undefined) m.set(t, 'tagIds', orderTags(u.tagIds))
    else if (u.addTagIds || u.removeTagIds) {
      const remove = new Set(u.removeTagIds ?? [])
      const next = t.tagIds.filter((id) => !remove.has(id))
      for (const id of u.addTagIds ?? []) if (!next.includes(id)) next.push(id)
      m.set(t, 'tagIds', orderTags(next))
    }
    if (u.listId !== undefined && u.listId !== t.listId) applyList(m, t, u.listId, u.statusId, u.position)
    else if (u.statusId !== undefined) applyStatus(m, t, u.statusId, u.position)
    else if (u.position !== undefined) m.set(t, 'position', u.position)
  }

  function inverseBulk(t: Task, u: BulkTaskUpdate): BulkTaskUpdate {
    const inv: BulkTaskUpdate = { id: t.id }
    if (u.priority !== undefined) inv.priority = t.priority
    if (u.startDate !== undefined) inv.startDate = t.startDate
    if (u.dueDate !== undefined) inv.dueDate = t.dueDate
    if (u.assigneeIds !== undefined || u.addAssigneeIds || u.removeAssigneeIds) inv.assigneeIds = [...t.assigneeIds]
    if (u.tagIds !== undefined || u.addTagIds || u.removeTagIds) inv.tagIds = [...t.tagIds]
    if (u.listId !== undefined && u.listId !== t.listId) {
      inv.listId = t.listId
      inv.statusId = t.statusId
      inv.position = t.position
    } else if (u.statusId !== undefined || u.position !== undefined) {
      inv.statusId = t.statusId
      inv.position = t.position
    }
    return inv
  }

  /** Where a move puts the task locally, mirroring the server's true-sibling rule (5.5). */
  function movePosition(t: Task, r: MoveTaskRequest): number {
    const sibs = siblingsOf(t.listId, r.statusId, t.parentTaskId, t.id)
    if (r.beforeTaskId) {
      const i = sibs.findIndex((s) => s.id === r.beforeTaskId)
      if (i >= 0) return between(sibs[i].position, sibs[i + 1]?.position ?? null)
    } else if (r.afterTaskId) {
      const i = sibs.findIndex((s) => s.id === r.afterTaskId)
      if (i >= 0) return between(sibs[i - 1]?.position ?? null, sibs[i].position)
    } else {
      return bottomOf(positionsOf(sibs))
    }
    const above = r.beforeTaskId ? byId[r.beforeTaskId] : undefined
    const below = r.afterTaskId ? byId[r.afterTaskId] : undefined
    return between(above?.position ?? null, below?.position ?? null)
  }

  // ---- Queues ----

  /** Runs `op` after every earlier request for the task has settled. */
  function enqueue<T>(id: Uuid, op: () => Promise<T>): Promise<T> {
    const prev = queues.get(id) ?? Promise.resolve()
    const run = prev.then(op)
    const tail = run.then(noop, noop)
    queues.set(id, tail)
    void tail.then(() => {
      if (queues.get(id) === tail) queues.delete(id)
    })
    return run
  }

  /** Runs `op` after every earlier request for any of the tasks has settled (bulk calls). */
  function enqueueMany<T>(ids: Uuid[], op: () => Promise<T>): Promise<T> {
    const prevs = ids.map((id) => queues.get(id)).filter((p): p is Promise<void> => !!p)
    const run = (prevs.length ? Promise.all(prevs) : Promise.resolve()).then(op)
    const tail = run.then(noop, noop)
    for (const id of ids) queues.set(id, tail)
    void tail.then(() => {
      for (const id of ids) if (queues.get(id) === tail) queues.delete(id)
    })
    return run
  }

  function whenSettled(ids: Uuid[]): Promise<void> {
    const waits = ids.map((id) => queues.get(id)).filter((p): p is Promise<void> => !!p)
    return waits.length ? Promise.all(waits).then(noop) : Promise.resolve()
  }

  // ---- Error toasts ----

  function showRetry(id: Uuid, fields: string[], err: unknown, retry: () => void): void {
    const toastId = toast.error(err, { retry })
    if (!toastId) return
    for (const f of fields) retryToasts.set(`${id}:${f}`, toastId)
  }

  /** A newer change to the same field succeeded: its old Retry toast is moot (9.1 rule 2). */
  function clearRetry(id: Uuid, fields: string[]): void {
    for (const f of fields) {
      const key = `${id}:${f}`
      const toastId = retryToasts.get(key)
      if (toastId === undefined) continue
      retryToasts.delete(key)
      toast.dismiss(toastId)
    }
  }

  function rejectQuietly<T>(message: string): Promise<T> {
    return handled(Promise.reject(new Error(message)))
  }

  /** Handles a failed single-task request: 404 removes the task, otherwise roll back or resync. */
  function failSingle(id: Uuid, s: number, m: Mutation, err: unknown, fields: string[], retry: () => void): void {
    if (err instanceof SkippedError) return
    if (errorStatus(err) === 404 && !creating.has(id)) {
      removeLocal(id, { adjustParent: true, forget: true })
      toast.error(err)
      return
    }
    if (seq[id] === s) m.revert()
    else void fetchDetail(id).catch(noop)
    showRetry(id, fields, err, retry)
  }

  // ---- Loads ----

  function metaFor(listId: Uuid): ListMeta {
    if (!listMeta[listId]) listMeta[listId] = { loadedAt: 0, loading: false, error: null }
    return listMeta[listId]
  }

  /** GET /api/lists/{id}/tasks, merged by id (9.1 rule 7). Skips a list loaded in the last 30s unless forced. Never rejects. */
  function loadList(listId: Uuid, o: { force?: boolean } = {}): Promise<void> {
    const running = listLoads.get(listId)
    if (running) return o.force ? running.then(() => loadList(listId, { force: true })) : running
    const meta = listMeta[listId]
    if (!o.force && meta && meta.loadedAt > 0 && Date.now() - meta.loadedAt < FRESH_MS) return Promise.resolve()
    const gen = generation
    const startedAt = clock
    metaFor(listId).loading = true
    const p = api
      .get<Task[]>(`/api/lists/${listId}/tasks`)
      .then(
        (rows) => {
          if (gen !== generation) return
          const busyParents = parentsWithBusyChildren()
          const seen = new Set<Uuid>()
          for (const t of rows) {
            seen.add(t.id)
            if (!isProtected(t.id, startedAt)) writeRow(t, busyParents.has(t.id))
          }
          for (const t of Object.values(byId)) {
            if (t.listId === listId && !seen.has(t.id) && !isProtected(t.id, startedAt) && !pendingDeletes.has(t.id)) {
              removeLocal(t.id, { adjustParent: false })
            }
          }
          const live = metaFor(listId)
          live.loadedAt = Date.now()
          live.error = null
        },
        (err: unknown) => {
          if (gen === generation) metaFor(listId).error = err
        },
      )
      .finally(() => {
        listLoads.delete(listId)
        if (gen === generation) metaFor(listId).loading = false
      })
    listLoads.set(listId, p)
    return p
  }

  /** Whether a local task belongs to a workspace query's result (used to drop tasks the server no longer returns). */
  function matchesWorkspaceQuery(t: Task, q: WorkspaceTasksQuery, meId: Uuid): boolean {
    if ((q.scope ?? 'mine') === 'mine') {
      const mine = t.assigneeIds.includes(meId) || (t.createdById === meId && t.assigneeIds.length === 0)
      if (!mine) return false
    }
    if (!q.includeDone && t.completedAt !== null) {
      if (!q.doneSince) return false
      if (timeOf(t.completedAt) < Date.parse(`${q.doneSince}T00:00:00Z`)) return false
    }
    return true
  }

  /**
   * GET /api/workspaces/{id}/tasks: merges the tasks and parent refs and returns the ids in server
   * order. Results for a workspace that is no longer current are dropped. Rejects on failure
   * (pre-handled); an aborted request rejects with the AbortError.
   */
  function loadWorkspaceTasks(wsId: Uuid, q: WorkspaceTasksQuery, o: { signal?: AbortSignal } = {}): Promise<Uuid[]> {
    const gen = generation
    const startedAt = clock
    const url = `/api/workspaces/${wsId}/tasks${query({
      scope: q.scope,
      q: q.q,
      includeDone: q.includeDone,
      doneSince: q.doneSince,
      limit: q.limit,
    })}`
    const p = api.get<WorkspaceTasksResponse>(url, { signal: o.signal }).then((res) => {
      if (gen !== generation || useWorkspaces().currentId !== wsId) return []
      const busyParents = parentsWithBusyChildren()
      const seen = new Set<Uuid>()
      for (const t of res.tasks) {
        seen.add(t.id)
        if (!isProtected(t.id, startedAt)) writeRow(t, busyParents.has(t.id))
      }
      for (const r of res.parents) refs[r.id] = { id: r.id, title: r.title, listId: r.listId }
      // A complete answer (no search, under the limit) also tells us which matching tasks are gone.
      const limit = q.limit ?? (q.q ? 20 : 500)
      const meId = useSession().user?.id
      if (!q.q && meId && res.tasks.length < limit) {
        for (const t of Object.values(byId)) {
          if (seen.has(t.id) || isProtected(t.id, startedAt) || pendingDeletes.has(t.id)) continue
          if (matchesWorkspaceQuery(t, q, meId)) removeLocal(t.id, { adjustParent: false })
        }
      }
      return res.tasks.map((t) => t.id)
    })
    return handled(p)
  }

  /**
   * GET /api/tasks/{id}, queued behind the task's pending requests (its create first). Upserts the
   * task and its subtasks when the task is in the current workspace. A 404 removes the task.
   */
  function fetchDetail(id: Uuid): Promise<TaskDetail> {
    const gen = generation
    const p = enqueue(id, async () => {
      const startedAt = clock
      const d = await api.get<TaskDetail>(`/api/tasks/${id}`)
      if (gen !== generation || d.workspaceId !== useWorkspaces().currentId) return d
      const busyParents = parentsWithBusyChildren()
      if (!isProtected(d.task.id, startedAt)) writeRow(d.task, busyParents.has(d.task.id))
      const seen = new Set<Uuid>()
      for (const s of d.subtasks) {
        seen.add(s.id)
        if (!isProtected(s.id, startedAt)) writeRow(s)
      }
      for (const s of Object.values(byId)) {
        if (s.parentTaskId === id && !seen.has(s.id) && !isProtected(s.id, startedAt) && !pendingDeletes.has(s.id)) {
          removeLocal(s.id, { adjustParent: false })
        }
      }
      if (d.parent) refs[d.parent.id] = { ...d.parent }
      return d
    }).catch((err: unknown) => {
      if (gen === generation && errorStatus(err) === 404 && !creating.has(id)) removeLocal(id, { adjustParent: true, forget: true })
      throw err
    })
    return handled(p)
  }

  /** Puts server rows in the store, except tasks with a request in flight or a pending create. */
  function upsert(t: Task | Task[]): void {
    const rows = Array.isArray(t) ? t : [t]
    const busyParents = parentsWithBusyChildren()
    for (const row of rows) {
      if ((inflight[row.id] ?? 0) > 0 || creating.has(row.id)) continue
      writeRow(row, busyParents.has(row.id))
    }
  }

  // ---- Create ----

  /**
   * POST /api/lists/{id}/tasks. The full task is in the store at once (pass `r.id` to know its id
   * before the promise settles). Creates for one list, status and parent are sent one at a time.
   * On failure the row goes and a toast offers Retry with the same id; a 409 on that retry means
   * the first attempt succeeded, so the list is refetched. Like update, the POST waits for any of
   * its tags still being created (tags.whenCreated), so it never names an unknown tag.
   */
  function create(listId: Uuid, r: CreateTaskRequest): Promise<Task> {
    const workspaces = useWorkspaces()
    const id = r.id ?? newId()
    if (byId[id]) return rejectQuietly('This task already exists.')
    const gen = generation
    const statusId = r.statusId ?? workspaces.defaultStatusFor(listId)?.id ?? ''
    const parentTaskId = r.parentTaskId ?? null
    const sibs = positionsOf(siblingsOf(listId, statusId, parentTaskId, id))
    const position = r.placement === 'Top' ? topOf(sibs) : bottomOf(sibs)
    const done = statusOf(statusId)?.category === 'Done'
    const now = nowIso()
    const title = r.title.trim()
    const task: Task = {
      id,
      listId,
      statusId,
      parentTaskId,
      title,
      hasDescription: !isEmptyHtml(r.description),
      priority: r.priority ?? 'None',
      assigneeIds: orderAssignees([], r.assigneeIds ?? []),
      tagIds: orderTags(r.tagIds ?? []),
      startDate: r.startDate ?? null,
      dueDate: r.dueDate ?? null,
      position,
      createdById: useSession().user?.id ?? '',
      createdAt: now,
      updatedAt: now,
      completedAt: done ? now : null,
      subtaskCount: 0,
      subtaskDoneCount: 0,
      commentCount: 0,
      attachmentCount: 0,
    }
    byId[id] = task
    creating.add(id)
    failedCreates.delete(id)
    const m = new Mutation()
    if (parentTaskId) m.counts(id, parentTaskId, 1, done ? 1 : 0)
    const s = begin(id)
    const body: CreateTaskRequest = { ...r, id, title }
    if (statusId) body.statusId = statusId
    const key = `${listId}|${statusId}|${parentTaskId ?? ''}`
    const prevChain = createChains.get(key) ?? Promise.resolve()
    const run = enqueue(id, () =>
      prevChain.then(async () => {
        if (body.tagIds?.length) await useTags().whenCreated(body.tagIds)
        return api.post<Task>(`/api/lists/${listId}/tasks`, body)
      }),
    )
    const chainTail = run.then(noop, noop)
    createChains.set(key, chainTail)
    void chainTail.then(() => {
      if (createChains.get(key) === chainTail) createChains.delete(key)
    })
    const p = run.then(
      (server) => {
        if (gen !== generation) return server
        creating.delete(id)
        settle(id)
        if (seq[id] === s) writeRow(server)
        else {
          // A newer local write owns the rest; take what only the server knows.
          const live = byId[id]
          if (live) {
            live.createdAt = server.createdAt
            live.createdById = server.createdById
          }
        }
        if (parentTaskId) changedAt[parentTaskId] = Date.now()
        return byId[id] ?? server
      },
      async (err: unknown): Promise<Task> => {
        if (gen !== generation) throw err
        creating.delete(id)
        settle(id)
        if (errorStatus(err) === 409) {
          await loadList(listId, { force: true })
          const live = byId[id]
          if (live) return live
          throw err
        }
        failedCreates.add(id)
        if (byId[id]) {
          m.revert()
          dropRow(id)
        }
        if (errorStatus(err) !== 401) {
          toast.show({
            message: `Couldn't create “${title}”.`,
            tone: 'danger',
            action: { label: 'Retry', run: () => void create(listId, { ...r, id }) },
          })
        }
        throw err
      },
    )
    return handled(p)
  }

  // ---- Update and move ----

  /** PATCH /api/tasks/{id} (9.1 rule 2). No toast unless `undo` is a message; undo restores the old values. */
  function update(id: Uuid, patch: TaskPatch, o: { undo?: string | false } = {}): Promise<Task> {
    const t = byId[id]
    if (!t) return rejectQuietly('This task is not loaded.')
    if (isPendingDelete(id)) return rejectQuietly('This task is being deleted.')
    const fields = (Object.keys(patch) as (keyof TaskPatch)[]).filter((k) => patch[k] !== undefined)
    if (!fields.length) return Promise.resolve(t)
    const gen = generation
    const inverse = inversePatch(t, patch)
    const m = new Mutation()
    applyPatch(m, t, patch)
    m.set(t, 'updatedAt', nowIso())
    const s = begin(id)
    const undoToast =
      typeof o.undo === 'string' ? toast.undoable(o.undo, () => update(id, inverse, { undo: false }).then(noop)) : ''
    const body: TaskPatch = {}
    for (const k of fields) (body as Record<string, unknown>)[k] = patch[k]
    const p = enqueue(id, async () => {
      if (failedCreates.has(id)) throw new SkippedError()
      if (patch.tagIds) await useTags().whenCreated(patch.tagIds)
      return api.patch<Task>(`/api/tasks/${id}`, body)
    }).then(
      (server) => {
        if (typeof patch.description === 'string') clearDescriptionDraft(id, patch.description)
        if (gen !== generation) return server
        settle(id)
        if (seq[id] === s) writeRow(server, parentsWithBusyChildren().has(id))
        clearRetry(id, fields)
        return byId[id] ?? server
      },
      (err: unknown) => {
        if (typeof patch.description === 'string') noteDescriptionFailure(id, patch.description, err)
        if (gen !== generation) throw err
        settle(id)
        if (undoToast) toast.dismiss(undoToast)
        failSingle(id, s, m, err, fields, () => void update(id, patch, { undo: false }))
        throw err
      },
    )
    return handled(p)
  }

  // ---- Description drafts ----

  /** Records (or replaces) the task's unsaved description. */
  function setDescriptionDraft(id: Uuid, html: string, retryable = true): void {
    descriptionDrafts.set(id, { html, retryable })
  }

  /** Forgets the task's draft; with `html`, only while the draft still holds exactly that HTML. */
  function clearDescriptionDraft(id: Uuid, html?: string): void {
    const draft = descriptionDrafts.get(id)
    if (draft && (html === undefined || draft.html === html)) descriptionDrafts.delete(id)
  }

  function noteDescriptionFailure(id: Uuid, html: string, err: unknown): void {
    const status = errorStatus(err)
    // Gone, signed out, or never created: there is nothing left to save the text to.
    if (err instanceof SkippedError || status === 404 || status === 401) {
      clearDescriptionDraft(id, html)
      return
    }
    setDescriptionDraft(id, html, errorMessage(err).retryable)
  }

  /** A description editor for the task is on screen; it sends its own draft again. Returns the release. */
  function holdDescription(id: Uuid): () => void {
    descriptionHolders.set(id, (descriptionHolders.get(id) ?? 0) + 1)
    let released = false
    return () => {
      if (released) return
      released = true
      const n = (descriptionHolders.get(id) ?? 1) - 1
      if (n > 0) descriptionHolders.set(id, n)
      else descriptionHolders.delete(id)
    }
  }

  /**
   * Sends the task's draft (an editor that closed with an unsaved edit, or a retry once back
   * online). Success forgets it; failure keeps it, and a task still in the store toasts as usual.
   */
  function sendDescriptionDraft(id: Uuid): void {
    const draft = descriptionDrafts.get(id)
    if (!draft) return
    const html = draft.html
    if (byId[id]) {
      if (isPendingDelete(id)) return
      void update(id, { description: html }, { undo: false }).catch(noop)
      return
    }
    // The task left the store (a workspace switch): send the text directly.
    void api.patch<Task>(`/api/tasks/${id}`, { description: html }).then(
      () => clearDescriptionDraft(id, html),
      (err: unknown) => noteDescriptionFailure(id, html, err),
    )
  }

  /** Back online: sends every draft that failed for want of a network, except those an editor shows. */
  function retryDescriptionDrafts(): void {
    for (const [id, draft] of [...descriptionDrafts]) {
      if (draft.retryable && !descriptionHolders.has(id)) sendDescriptionDraft(id)
    }
  }

  if (typeof window !== 'undefined') {
    window.addEventListener('online', retryDescriptionDrafts)
    // The drafts live only in memory: warn before a reload or a closed tab drops one.
    window.addEventListener('beforeunload', (e) => {
      if (descriptionDrafts.size === 0) return
      e.preventDefault()
      e.returnValue = ''
    })
  }

  /**
   * POST /api/tasks/{id}/move (9.1 rule 5). A status change toasts "Moved to <status>" with Undo,
   * unless `undo` is false; a reorder within the status shows nothing.
   */
  function move(id: Uuid, r: MoveTaskRequest, o: { undo?: string | false } = {}): Promise<Task> {
    const t = byId[id]
    if (!t) return rejectQuietly('This task is not loaded.')
    if (isPendingDelete(id)) return rejectQuietly('This task is being deleted.')
    const gen = generation
    const prevStatusId = t.statusId
    const prevPosition = t.position
    const statusChanged = r.statusId !== t.statusId
    const m = new Mutation()
    const position = movePosition(t, r)
    if (statusChanged) applyStatus(m, t, r.statusId, position)
    else m.set(t, 'position', position)
    m.set(t, 'updatedAt', nowIso())
    const s = begin(id)
    let undoToast = ''
    if (o.undo !== false && (statusChanged || typeof o.undo === 'string')) {
      const message = typeof o.undo === 'string' ? o.undo : `Moved to ${statusOf(r.statusId)?.name ?? 'another status'}`
      undoToast = toast.undoable(message, () =>
        update(id, { statusId: prevStatusId, position: prevPosition }, { undo: false }).then(noop),
      )
    }
    const body: MoveTaskRequest = { statusId: r.statusId, beforeTaskId: r.beforeTaskId ?? null, afterTaskId: r.afterTaskId ?? null }
    const fields = ['statusId', 'position']
    const p = enqueue(id, async () => {
      if (failedCreates.has(id)) throw new SkippedError()
      return api.post<Task>(`/api/tasks/${id}/move`, body)
    }).then(
      (server) => {
        if (gen !== generation) return server
        settle(id)
        if (seq[id] === s) writeRow(server, parentsWithBusyChildren().has(id))
        clearRetry(id, fields)
        return byId[id] ?? server
      },
      (err: unknown) => {
        if (gen !== generation) throw err
        settle(id)
        if (undoToast) toast.dismiss(undoToast)
        failSingle(id, s, m, err, fields, () => void move(id, r, { undo: false }))
        throw err
      },
    )
    return handled(p)
  }

  /** Moves the task to its list's first Done status, with the undo toast and the hold. */
  function complete(id: Uuid): Promise<void> {
    const t = byId[id]
    if (!t || isDoneTask(t)) return Promise.resolve()
    const done = useWorkspaces().doneStatusFor(t.listId)
    if (!done) return Promise.resolve()
    return handled(update(id, { statusId: done.id }, { undo: `Completed “${t.title}”` }).then(noop))
  }

  /** Moves the task to the first Not started status (else the first Active), with the undo toast. */
  function reopen(id: Uuid): Promise<void> {
    const t = byId[id]
    if (!t || !isDoneTask(t)) return Promise.resolve()
    const open = useWorkspaces().openStatusFor(t.listId)
    if (!open) return Promise.resolve()
    return handled(update(id, { statusId: open.id }, { undo: `Reopened “${t.title}”` }).then(noop))
  }

  function toggleComplete(id: Uuid): Promise<void> {
    const t = byId[id]
    if (!t) return Promise.resolve()
    return isDoneTask(t) ? reopen(id) : complete(id)
  }

  // ---- Bulk ----

  /**
   * POST /api/tasks/bulk in chunks of 500, sent one after another (9.1 rule 6). Toasts
   * "Updated N tasks" with an Undo that restores every task's previous values, unless `undo` is
   * false (or a message of its own). A failed chunk rolls back its tasks and shows the server's detail.
   */
  function bulkUpdate(updates: BulkTaskUpdate[], o: { undo?: string | false } = {}): Promise<void> {
    const seen = new Set<Uuid>()
    const items = updates.filter((u) => {
      if (seen.has(u.id) || !byId[u.id] || isPendingDelete(u.id)) return false
      seen.add(u.id)
      return true
    })
    if (!items.length) return Promise.resolve()
    const gen = generation
    const inverse: BulkTaskUpdate[] = []
    const seqs = new Map<Uuid, number>()
    const parts = chunk(items, BULK_CHUNK).map((part) => {
      const m = new Mutation()
      for (const u of part) {
        const t = byId[u.id]
        inverse.push(inverseBulk(t, u))
        applyBulk(m, t, u)
        m.set(t, 'updatedAt', nowIso())
        seqs.set(u.id, begin(u.id))
      }
      return { part, m }
    })
    let undoToast = ''
    if (o.undo !== false) {
      const message = typeof o.undo === 'string' ? o.undo : `Updated ${pluralize(items.length, 'task')}`
      undoToast = toast.undoable(message, () => bulkUpdate(inverse, { undo: false }))
    }
    let failures = 0
    let firstError: unknown = null
    const send = ({ part, m }: { part: BulkTaskUpdate[]; m: Mutation }): Promise<void> => {
      const ids = part.map((u) => u.id)
      const tagIds = part.flatMap((u) => [...(u.tagIds ?? []), ...(u.addTagIds ?? [])])
      return enqueueMany(ids, async () => {
        if (tagIds.length) await useTags().whenCreated(tagIds)
        return api.post<BulkUpdateResponse>('/api/tasks/bulk', { updates: part })
      }).then(
        (res) => {
          if (gen !== generation) return
          for (const id of ids) settle(id)
          const busyParents = parentsWithBusyChildren()
          for (const row of res.tasks) if (seq[row.id] === seqs.get(row.id)) writeRow(row, busyParents.has(row.id))
        },
        (err: unknown) => {
          if (gen !== generation) return
          for (const id of ids) settle(id)
          failures++
          if (firstError === null) firstError = err
          const stale = (id: Uuid) => seqs.has(id) && seq[id] !== seqs.get(id)
          m.revert(stale)
          for (const id of ids) if (stale(id)) void fetchDetail(id).catch(noop)
          toast.error(err, { retry: () => void bulkUpdate(part, { undo: false }) })
        },
      )
    }
    const p = parts
      .reduce<Promise<void>>((prev, part) => prev.then(() => send(part)), Promise.resolve())
      .then(() => {
        if (failures === 0) return
        if (failures === parts.length && undoToast) toast.dismiss(undoToast)
        throw firstError
      })
    return handled(p)
  }

  /** Moves top-level tasks (and their subtasks) to another list, with the "Moved N tasks to <list>" undo toast. */
  function moveToList(ids: Uuid[], listId: Uuid): Promise<void> {
    useSelection().remove(ids)
    const top = [...new Set(ids)].filter((id) => {
      const t = byId[id]
      return !!t && t.parentTaskId === null && t.listId !== listId
    })
    if (!top.length) return Promise.resolve()
    const name = useWorkspaces().findList(listId)?.list.name ?? 'another list'
    return bulkUpdate(
      top.map((id) => ({ id, listId })),
      { undo: `Moved ${pluralize(top.length, 'task')} to ${name}` },
    )
  }

  // ---- Delete ----

  /**
   * Hides the tasks at once and shows "Deleted “<title>”" (or "Deleted N tasks") with Undo; the
   * DELETE (or bulk delete in chunks of 500) is sent when the toast closes (9.2).
   */
  function remove(ids: Uuid[], o: { message?: string } = {}): void {
    const unique = [...new Set(ids)]
    const idSet = new Set(unique)
    const targets = unique.filter((id) => {
      const t = byId[id]
      if (!t || isPendingDelete(id)) return false
      return !(t.parentTaskId && idSet.has(t.parentTaskId))
    })
    if (!targets.length) return
    useSelection().remove(targets)
    const first = byId[targets[0]]
    const message = o.message ?? (targets.length === 1 ? `Deleted “${first.title}”` : `Deleted ${targets.length} tasks`)
    const gen = generation
    /** Parent counts taken off at hide time, so restore can put them back. */
    const counted = new Map<Uuid, { parentId: Uuid; done: boolean }>()

    const restoreIds = (restore: Uuid[]) => {
      if (gen !== generation) return
      for (const id of restore) {
        pendingDeletes.delete(id)
        const c = counted.get(id)
        if (!c) continue
        counted.delete(id)
        const p = byId[c.parentId]
        if (!p) continue
        p.subtaskCount++
        if (c.done) p.subtaskDoneCount++
      }
    }

    const parts = chunk(targets, BULK_CHUNK)
    const beacons = parts.map((part) =>
      part.length === 1 && targets.length === 1
        ? { method: 'DELETE' as const, url: `/api/tasks/${part[0]}` }
        : { method: 'POST' as const, url: '/api/tasks/bulk-delete', body: { ids: part } },
    )

    useDelayedDelete().schedule({
      key: `tasks:${newId()}`,
      message,
      hide: () => {
        for (const id of targets) {
          const t = byId[id]
          if (!t) continue
          pendingDeletes.add(id)
          if (t.parentTaskId) {
            const p = byId[t.parentTaskId]
            if (p) {
              const done = t.completedAt !== null
              p.subtaskCount = Math.max(0, p.subtaskCount - 1)
              if (done) p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount - 1)
              counted.set(id, { parentId: p.id, done })
            }
          }
        }
      },
      restore: () => restoreIds(targets),
      commit: async () => {
        await whenSettled(targets)
        let firstError: unknown = null
        for (const part of parts) {
          try {
            if (targets.length === 1) await api.del(`/api/tasks/${part[0]}`)
            else await api.post<void>('/api/tasks/bulk-delete', { ids: part })
          } catch (err) {
            // A 404 means the task is already gone, which is what we wanted (9.2).
            if (errorStatus(err) !== 404) {
              if (errorStatus(err) === 401) return
              if (firstError === null) firstError = err
              restoreIds(part)
              continue
            }
          }
          if (gen !== generation) continue
          for (const id of part) {
            counted.delete(id)
            removeLocal(id, { adjustParent: false, forget: true })
          }
        }
        if (firstError !== null) toast.error(firstError)
      },
      beacon: beacons[0],
      beacons: beacons.slice(1),
    })
  }

  // ---- Status and count maintenance ----

  /**
   * Tasks of a deleted status move to `toStatusId` the way the server moves them (5.5). Nothing is
   * sent; like every store write it bumps changedAt, so an open TaskDetail refetches (8.10).
   */
  function reassignStatus(fromStatusId: Uuid, toStatusId: Uuid): void {
    if (fromStatusId === toStatusId) return
    const toDone = statusOf(toStatusId)?.category === 'Done'
    for (const t of Object.values(byId)) {
      if (t.statusId !== fromStatusId) continue
      const wasCompleted = t.completedAt !== null
      t.statusId = toStatusId
      if (toDone && !wasCompleted) t.completedAt = nowIso()
      else if (!toDone && wasCompleted) t.completedAt = null
      const isCompleted = t.completedAt !== null
      if (t.parentTaskId && wasCompleted !== isCompleted) {
        const p = byId[t.parentTaskId]
        if (p) {
          p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount + (isCompleted ? 1 : -1))
          changedAt[p.id] = Date.now()
        }
      }
      lastWrite[t.id] = ++clock
      changedAt[t.id] = Date.now()
    }
  }

  /**
   * A status changed category: set or clear completedAt on its loaded tasks like the server (5.5).
   * Nothing is sent; changedAt is bumped as for reassignStatus.
   */
  function applyCategoryChange(statusId: Uuid, category: StatusCategory): void {
    const done = category === 'Done'
    for (const t of Object.values(byId)) {
      if (t.statusId !== statusId) continue
      const wasCompleted = t.completedAt !== null
      if (done && !wasCompleted) t.completedAt = nowIso()
      else if (!done && wasCompleted) t.completedAt = null
      else continue
      if (t.parentTaskId) {
        const p = byId[t.parentTaskId]
        if (p) {
          p.subtaskDoneCount = Math.max(0, p.subtaskDoneCount + (done ? 1 : -1))
          changedAt[p.id] = Date.now()
        }
      }
      lastWrite[t.id] = ++clock
      changedAt[t.id] = Date.now()
    }
  }

  /** Comment and attachment counts (B4). Subtask counts are the store's own job. */
  function adjustCounts(id: Uuid, d: Partial<Record<'commentCount' | 'attachmentCount', number>>): void {
    const t = byId[id]
    if (!t) return
    if (d.commentCount) t.commentCount = Math.max(0, t.commentCount + d.commentCount)
    if (d.attachmentCount) t.attachmentCount = Math.max(0, t.attachmentCount + d.attachmentCount)
    lastWrite[id] = ++clock
    changedAt[id] = Date.now()
  }

  /** Takes a removed member off every loaded task; returns a function that puts them back. */
  function stripAssignee(userId: Uuid): () => void {
    const changed: { id: Uuid; prev: Uuid[] }[] = []
    for (const t of Object.values(byId)) {
      if (!t.assigneeIds.includes(userId)) continue
      changed.push({ id: t.id, prev: [...t.assigneeIds] })
      t.assigneeIds = t.assigneeIds.filter((x) => x !== userId)
      lastWrite[t.id] = ++clock
    }
    return () => {
      for (const c of changed) {
        const t = byId[c.id]
        if (t && !t.assigneeIds.includes(userId)) t.assigneeIds = orderAssignees(t.assigneeIds, [...t.assigneeIds, userId])
      }
    }
  }

  /** Takes a deleted tag off every loaded task; returns a function that puts it back. */
  function stripTag(tagId: Uuid): () => void {
    const changed: Uuid[] = []
    for (const t of Object.values(byId)) {
      if (!t.tagIds.includes(tagId)) continue
      changed.push(t.id)
      t.tagIds = t.tagIds.filter((x) => x !== tagId)
      lastWrite[t.id] = ++clock
    }
    return () => {
      for (const id of changed) {
        const t = byId[id]
        if (t && !t.tagIds.includes(tagId)) t.tagIds = orderTags([...t.tagIds, tagId])
      }
    }
  }

  /** Drops a list's tasks (the list was deleted or left). */
  function forgetList(listId: Uuid): void {
    for (const t of Object.values(byId)) if (t.listId === listId) dropRow(t.id)
    delete listMeta[listId]
    listLoads.delete(listId)
    const selection = useSelection()
    if (selection.listId === listId) selection.clear()
  }

  /** Empties the store (workspace switch, sign-out). Responses to earlier requests are ignored. */
  function reset(): void {
    generation++
    for (const key of Object.keys(byId)) delete byId[key]
    for (const key of Object.keys(refs)) delete refs[key]
    for (const key of Object.keys(listMeta)) delete listMeta[key]
    for (const key of Object.keys(changedAt)) delete changedAt[key]
    for (const timer of holdTimers.values()) clearTimeout(timer)
    holdTimers.clear()
    recentlyCompleted.value = new Set()
    pendingDeletes.clear()
    creating.clear()
    for (const key of Object.keys(seq)) delete seq[key]
    for (const key of Object.keys(inflight)) delete inflight[key]
    for (const key of Object.keys(lastWrite)) delete lastWrite[key]
    queues.clear()
    createChains.clear()
    failedCreates.clear()
    retryToasts.clear()
    listLoads.clear()
  }

  return {
    byId,
    refs,
    listMeta,
    changedAt,
    recentlyCompleted,
    descriptionDrafts,
    get,
    isPendingDelete,
    isDone,
    isCreatePending,
    inList,
    topLevelInList,
    subtasksOf,
    loadList,
    loadWorkspaceTasks,
    fetchDetail,
    upsert,
    create,
    update,
    move,
    moveToList,
    complete,
    reopen,
    toggleComplete,
    bulkUpdate,
    remove,
    reassignStatus,
    applyCategoryChange,
    adjustCounts,
    stripAssignee,
    stripTag,
    forgetList,
    reset,
    setDescriptionDraft,
    clearDescriptionDraft,
    sendDescriptionDraft,
    holdDescription,
  }
})

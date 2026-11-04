// Filtering, sorting and grouping of a list's tasks (10.2, 8.1, 8.7), and the per-user view
// settings stored in localStorage. Pure functions: callers pass the statuses and "today".
import {
  PRIORITIES,
  PRIORITY_LABEL,
  PRIORITY_ORDER,
  type CreateTaskRequest,
  type IsoDate,
  type Priority,
  type Status,
  type Task,
  type Uuid,
  type ViewKind,
} from '../types'
import { DUE_BUCKET_LABEL, addDaysIso, dueBucket, endOfWeekIso, type DueBucket } from './dates'
import { readJson, userKey, writeJson } from './storage'

export type GroupBy = 'status' | 'priority' | 'due' | 'none'
export type SortField = 'manual' | 'dueDate' | 'priority' | 'title' | 'createdAt'
export type ColumnKey = 'status' | 'assignees' | 'dueDate' | 'priority' | 'tags'
export type DueFilter = 'overdue' | 'today' | 'thisWeek' | 'next7' | 'none'

export interface ViewConfig {
  groupBy: GroupBy
  sort: { field: SortField; dir: 'asc' | 'desc' }
  columns: Record<ColumnKey, boolean>
  showCompleted: boolean
  subtasks: 'collapsed' | 'expanded'
  collapsedGroups: Record<string, boolean>
  /** Calendar tray. */
  showToSchedule: boolean
}

export interface TaskFilters {
  statusIds: Uuid[]
  assignees: (Uuid | 'me' | 'unassigned')[]
  priorities: Priority[]
  tagIds: Uuid[]
  due: DueFilter[]
}

export interface FilterContext {
  meId: Uuid
  today: IsoDate
  statusById: (id: Uuid) => Status | undefined
}

export interface FilterResult {
  /** Every task to render, top-level and subtasks, including context parents and kept tasks. */
  visibleIds: Set<Uuid>
  /** Parents rendered only because a subtask matches. */
  contextIds: Set<Uuid>
  /** Tasks rendered only because they are in keepVisibleIds ("Hidden by filters"). */
  keptIds: Set<Uuid>
  /** Top-level tasks hidden by filters or search (not by "show completed"). */
  hiddenCount: number
}

export interface TaskGroup {
  /** `status:<id>`, `priority:<value>`, `due:<bucket>`, `completed` or `all`. */
  key: string
  label: string
  kind: GroupBy | 'completed'
  status?: Status
  priority?: Priority
  bucket?: string
  tasks: Task[]
  /** What an add row in this group sets; null where the group has no add row (Overdue, Completed). */
  createDefaults: Partial<CreateTaskRequest> | null
}

export const GROUP_BYS: GroupBy[] = ['status', 'priority', 'due', 'none']
export const SORT_FIELDS: SortField[] = ['manual', 'dueDate', 'priority', 'title', 'createdAt']
export const COLUMN_KEYS: ColumnKey[] = ['status', 'assignees', 'dueDate', 'priority', 'tags']
export const DUE_FILTERS: DueFilter[] = ['overdue', 'today', 'thisWeek', 'next7', 'none']
export const DUE_FILTER_LABEL: Record<DueFilter, string> = {
  overdue: 'Overdue',
  today: 'Today',
  thisWeek: 'This week',
  next7: 'Next 7 days',
  none: 'No due date',
}

// ---- Defaults ----

export function defaultViewConfig(view: ViewKind): ViewConfig {
  void view // every view starts from the same settings; the key keeps them apart
  return {
    groupBy: 'status',
    sort: { field: 'manual', dir: 'asc' },
    columns: { status: true, assignees: true, dueDate: true, priority: true, tags: true },
    showCompleted: true,
    subtasks: 'collapsed',
    collapsedGroups: {},
    showToSchedule: true,
  }
}

export function emptyFilters(): TaskFilters {
  return { statusIds: [], assignees: [], priorities: [], tagIds: [], due: [] }
}

/** The number of selected filter values ("Filter, 2 active"); 'me' counts once. */
export function activeFilterCount(f: TaskFilters): number {
  return f.statusIds.length + new Set(f.assignees).size + f.priorities.length + f.tagIds.length + f.due.length
}

// ---- Matching ----

function fold(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLocaleLowerCase()
}

function isDoneIn(t: Task, ctx: FilterContext): boolean {
  const s = ctx.statusById(t.statusId)
  return s ? s.category === 'Done' : t.completedAt !== null
}

function matchesDue(t: Task, option: DueFilter, ctx: FilterContext): boolean {
  const due = t.dueDate
  switch (option) {
    case 'none':
      return !due
    case 'overdue':
      return !!due && due < ctx.today && !isDoneIn(t, ctx)
    case 'today':
      return due === ctx.today
    case 'thisWeek':
      return !!due && due >= ctx.today && due <= endOfWeekIso(ctx.today)
    case 'next7':
      return !!due && due >= addDaysIso(ctx.today, 1) && due <= addDaysIso(ctx.today, 7)
  }
}

/** Every facet must pass (AND); within a facet any value passes (OR). Empty facets pass. */
export function matchesFilters(t: Task, f: TaskFilters, ctx: FilterContext): boolean {
  if (f.statusIds.length && !f.statusIds.includes(t.statusId)) return false
  if (
    f.assignees.length &&
    !f.assignees.some((a) =>
      a === 'me' ? t.assigneeIds.includes(ctx.meId) : a === 'unassigned' ? t.assigneeIds.length === 0 : t.assigneeIds.includes(a),
    )
  )
    return false
  if (f.priorities.length && !f.priorities.includes(t.priority)) return false
  if (f.tagIds.length && !f.tagIds.some((id) => t.tagIds.includes(id))) return false
  if (f.due.length && !f.due.some((d) => matchesDue(t, d, ctx))) return false
  return true
}

const EMPTY_SET: ReadonlySet<Uuid> = new Set()

/**
 * Which tasks to render (10.2). A task matches when it passes every facet and the search (title
 * only, ignoring case and accents). "Show completed" off hides Done tasks except `heldIds`. A
 * top-level task shows when it matches, or as context when one of its subtasks matches; a subtask
 * shows when it passes "show completed" and it or its parent matches. `keepVisibleIds` always show.
 */
export function applyFilters(
  tasks: Task[],
  f: TaskFilters,
  search: string,
  ctx: FilterContext,
  o: { showCompleted: boolean; keepVisibleIds?: Set<Uuid>; heldIds?: Set<Uuid> },
): FilterResult {
  const q = fold(search.trim())
  const held = o.heldIds ?? EMPTY_SET
  const keep = o.keepVisibleIds ?? EMPTY_SET
  const byId = new Map<Uuid, Task>()
  for (const t of tasks) byId.set(t.id, t)
  const children = new Map<Uuid, Task[]>()
  const top: Task[] = []
  for (const t of tasks) {
    if (!t.parentTaskId) top.push(t)
    else if (byId.has(t.parentTaskId)) {
      const arr = children.get(t.parentTaskId)
      if (arr) arr.push(t)
      else children.set(t.parentTaskId, [t])
    }
  }

  const passesCompleted = (t: Task) => o.showCompleted || held.has(t.id) || !isDoneIn(t, ctx)
  const matches = (t: Task) => matchesFilters(t, f, ctx) && (q === '' || fold(t.title).includes(q))

  const visibleIds = new Set<Uuid>()
  const contextIds = new Set<Uuid>()
  const keptIds = new Set<Uuid>()

  for (const t of top) {
    const parentShown = passesCompleted(t)
    const parentMatches = parentShown && matches(t)
    const subs = children.get(t.id) ?? []
    const matchingSubs: Task[] = []
    const shownSubs: Task[] = []
    for (const s of subs) {
      if (!passesCompleted(s)) continue
      const m = matches(s)
      if (m) matchingSubs.push(s)
      if (m || parentMatches) shownSubs.push(s)
    }
    if (parentMatches) {
      visibleIds.add(t.id)
      for (const s of shownSubs) visibleIds.add(s.id)
    } else if (matchingSubs.length) {
      visibleIds.add(t.id)
      contextIds.add(t.id)
      for (const s of matchingSubs) visibleIds.add(s.id)
    }
  }

  for (const id of keep) {
    const t = byId.get(id)
    if (!t || visibleIds.has(id)) continue
    visibleIds.add(id)
    keptIds.add(id)
    const parentId = t.parentTaskId
    if (parentId && byId.has(parentId) && !visibleIds.has(parentId)) {
      visibleIds.add(parentId)
      contextIds.add(parentId)
    }
  }

  let hiddenCount = 0
  for (const t of top) if (!visibleIds.has(t.id) && passesCompleted(t)) hiddenCount++

  return { visibleIds, contextIds, keptIds, hiddenCount }
}

/**
 * The filter values a new task inherits (8.1): "Assigned to me" or exactly one assignee, exactly
 * one status, one priority or one tag, and Due = Today only.
 */
export function filterDefaults(f: TaskFilters, ctx: FilterContext): Partial<CreateTaskRequest> {
  const d: Partial<CreateTaskRequest> = {}
  const assignees = [...new Set(f.assignees)]
  if (assignees.length === 1 && assignees[0] !== 'unassigned') d.assigneeIds = [assignees[0] === 'me' ? ctx.meId : assignees[0]]
  if (f.statusIds.length === 1) d.statusId = f.statusIds[0]
  if (f.priorities.length === 1) d.priority = f.priorities[0]
  if (f.tagIds.length === 1) d.tagIds = [f.tagIds[0]]
  if (f.due.length === 1 && f.due[0] === 'today') d.dueDate = ctx.today
  return d
}

// ---- Sorting ----

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function timeOf(ts: string): number {
  const t = Date.parse(ts)
  return Number.isNaN(t) ? 0 : t
}

/** The server's order (5.2): position, then createdAt, then id. */
export function compareManual(a: Task, b: Task): number {
  return a.position - b.position || timeOf(a.createdAt) - timeOf(b.createdAt) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}

/**
 * Sorts a copy. Empty values (no due date, priority None) always sort last; ties fall back to
 * position, then createdAt, then id.
 */
export function sortTasks(tasks: Task[], sort: ViewConfig['sort']): Task[] {
  const dir = sort.dir === 'desc' ? -1 : 1
  const out = [...tasks]
  switch (sort.field) {
    case 'manual':
      return out.sort((a, b) => dir * compareManual(a, b))
    case 'dueDate':
      return out.sort((a, b) => {
        if (!a.dueDate || !b.dueDate) return a.dueDate === b.dueDate ? compareManual(a, b) : a.dueDate ? -1 : 1
        return (a.dueDate < b.dueDate ? -dir : a.dueDate > b.dueDate ? dir : 0) || compareManual(a, b)
      })
    case 'priority':
      return out.sort((a, b) => {
        const pa = a.priority === 'None'
        const pb = b.priority === 'None'
        if (pa || pb) return pa === pb ? compareManual(a, b) : pa ? 1 : -1
        return dir * (PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority]) || compareManual(a, b)
      })
    case 'title':
      return out.sort((a, b) => {
        const ea = !a.title.trim()
        const eb = !b.title.trim()
        if (ea || eb) return ea === eb ? compareManual(a, b) : ea ? 1 : -1
        return dir * collator.compare(a.title, b.title) || compareManual(a, b)
      })
    case 'createdAt':
      return out.sort((a, b) => dir * (timeOf(a.createdAt) - timeOf(b.createdAt)) || compareManual(a, b))
  }
}

// ---- Grouping ----

const DUE_GROUPS: DueBucket[] = ['overdue', 'today', 'next7', 'later', 'none']

/**
 * Groups tasks in their given order (8.7). Status: every status in the given (display) order, even
 * empty ones; tasks with an unknown status go to the first group. Priority: Urgent to No priority.
 * Due: Overdue (only when it has tasks, no add row), Today, Next 7 days, Later, No due date. None:
 * one group, key 'all'. Except for status, done tasks collect in a final "Completed" group.
 */
export function groupTasks(tasks: Task[], groupBy: GroupBy, ctx: FilterContext & { statuses: Status[] }): TaskGroup[] {
  if (groupBy === 'status') {
    const groups: TaskGroup[] = ctx.statuses.map((status) => ({
      key: `status:${status.id}`,
      label: status.name,
      kind: 'status',
      status,
      tasks: [],
      createDefaults: { statusId: status.id },
    }))
    const byStatus = new Map(groups.map((g) => [g.status?.id ?? '', g]))
    for (const t of tasks) {
      const g = byStatus.get(t.statusId) ?? groups[0]
      g?.tasks.push(t)
    }
    return groups
  }

  const completed: Task[] = []
  const open: Task[] = []
  for (const t of tasks) (isDoneIn(t, ctx) ? completed : open).push(t)
  let groups: TaskGroup[]

  if (groupBy === 'priority') {
    groups = PRIORITIES.map((p) => ({
      key: `priority:${p}`,
      label: PRIORITY_LABEL[p],
      kind: 'priority',
      priority: p,
      tasks: open.filter((t) => t.priority === p),
      createDefaults: { priority: p },
    }))
  } else if (groupBy === 'due') {
    const defaults: Record<DueBucket, Partial<CreateTaskRequest> | null> = {
      overdue: null,
      today: { dueDate: ctx.today },
      next7: { dueDate: addDaysIso(ctx.today, 1) },
      later: { dueDate: addDaysIso(ctx.today, 8) },
      none: { dueDate: null },
    }
    const buckets = new Map<DueBucket, Task[]>(DUE_GROUPS.map((b) => [b, []]))
    for (const t of open) buckets.get(dueBucket(t, ctx.today))?.push(t)
    groups = DUE_GROUPS.filter((b) => b !== 'overdue' || (buckets.get(b)?.length ?? 0) > 0).map((b) => ({
      key: `due:${b}`,
      label: DUE_BUCKET_LABEL[b],
      kind: 'due',
      bucket: b,
      tasks: buckets.get(b) ?? [],
      createDefaults: defaults[b],
    }))
  } else {
    groups = [{ key: 'all', label: '', kind: 'none', tasks: open, createDefaults: {} }]
  }

  if (completed.length) {
    groups.push({ key: 'completed', label: 'Completed', kind: 'completed', tasks: completed, createDefaults: null })
  }
  return groups
}

// ---- Stored settings ----

function viewKey(userId: Uuid, listId: Uuid, view: ViewKind): string {
  return userKey('taskloom.view', userId, listId, view)
}

function filtersKey(userId: Uuid, listId: Uuid): string {
  return userKey('taskloom.filters', userId, listId)
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}

function pick<T extends string>(v: unknown, allowed: readonly T[], fallback: T): T {
  return typeof v === 'string' && (allowed as readonly string[]).includes(v) ? (v as T) : fallback
}

function strings(v: unknown): string[] {
  return Array.isArray(v) ? [...new Set(v.filter((x): x is string => typeof x === 'string' && x.length > 0))] : []
}

/** Validates a stored ViewConfig field by field; unknown keys are dropped, bad values defaulted. */
export function sanitizeViewConfig(raw: unknown, view: ViewKind): ViewConfig {
  const d = defaultViewConfig(view)
  if (!isRecord(raw)) return d
  const sort = isRecord(raw.sort) ? raw.sort : {}
  const columns = isRecord(raw.columns) ? raw.columns : {}
  const collapsed: Record<string, boolean> = {}
  if (isRecord(raw.collapsedGroups)) {
    for (const [k, v] of Object.entries(raw.collapsedGroups)) if (typeof v === 'boolean') collapsed[k] = v
  }
  const cols = { ...d.columns }
  for (const k of COLUMN_KEYS) if (typeof columns[k] === 'boolean') cols[k] = columns[k] as boolean
  return {
    groupBy: pick(raw.groupBy, GROUP_BYS, d.groupBy),
    sort: { field: pick(sort.field, SORT_FIELDS, d.sort.field), dir: pick(sort.dir, ['asc', 'desc'] as const, d.sort.dir) },
    columns: cols,
    showCompleted: typeof raw.showCompleted === 'boolean' ? raw.showCompleted : d.showCompleted,
    subtasks: pick(raw.subtasks, ['collapsed', 'expanded'] as const, d.subtasks),
    collapsedGroups: collapsed,
    showToSchedule: typeof raw.showToSchedule === 'boolean' ? raw.showToSchedule : d.showToSchedule,
  }
}

/** Validates stored filters; unknown keys and values are dropped. */
export function sanitizeFilters(raw: unknown): TaskFilters {
  if (!isRecord(raw)) return emptyFilters()
  return {
    statusIds: strings(raw.statusIds),
    assignees: strings(raw.assignees),
    priorities: strings(raw.priorities).filter((p): p is Priority => (PRIORITIES as string[]).includes(p)),
    tagIds: strings(raw.tagIds),
    due: strings(raw.due).filter((x): x is DueFilter => (DUE_FILTERS as string[]).includes(x)),
  }
}

/** `taskloom.view.<userId>.<listId>.<view>` (10.1). */
export function loadViewConfig(userId: Uuid, listId: Uuid, view: ViewKind): ViewConfig {
  return sanitizeViewConfig(readJson<unknown>(viewKey(userId, listId, view), null), view)
}

export function saveViewConfig(userId: Uuid, listId: Uuid, view: ViewKind, config: ViewConfig): void {
  writeJson(viewKey(userId, listId, view), sanitizeViewConfig(config, view))
}

/** `taskloom.filters.<userId>.<listId>` (10.1). */
export function loadFilters(userId: Uuid, listId: Uuid): TaskFilters {
  return sanitizeFilters(readJson<unknown>(filtersKey(userId, listId), null))
}

export function saveFilters(userId: Uuid, listId: Uuid, filters: TaskFilters): void {
  writeJson(filtersKey(userId, listId), sanitizeFilters(filters))
}

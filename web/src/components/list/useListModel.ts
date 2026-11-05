// The List view's model (8.7): the groups in order, the rows each one renders, and the session
// state behind them (expanded tasks, "Show N more" limits, the open add row).
// - Top-level tasks that pass the filters are sorted, then grouped; subtasks are sorted within their
//   parent. Context parents (shown only for matching subtasks) are expanded.
// - Completion hold (8.1): a task completed in the last second stays where it was rendered, in its
//   old group, until the hold ends; then it moves to its real group (often collapsed or hidden).
// - "Show completed" off hides the Done status groups and the Completed group.
// - Pins: positions are per status (5.5), so under Manual sort a task added from an add row could
//   land mid-group in a priority, due or none group, or mid-block among subtasks of mixed statuses.
//   Until the grouping, the sort or the list changes, such a task stays where it was added: at the
//   end (or the top, for the header "+") of its group, and at the end of its parent's subtasks.
// - Rows carry only what changes rarely; the components read the task itself, so an edit to one
//   task re-renders one row.
// - Progressive mounting: when many rows appear at once (opening a long list, clearing a search,
//   "Show N more", Expand all), one batch of task rows more renders per frame, so no single long
//   task builds hundreds of rows (S10). The rest follow frame by frame, in display order. The first
//   render mounts a screenful; later batches follow what a row costs on this device (lib/mountBudget),
//   timing each frame's step.
import { computed, nextTick, onScopeDispose, reactive, ref, shallowRef, watch, type ComputedRef, type Ref } from 'vue'
import { useListPageContext } from '../../composables/useListPageContext'
import { useToday } from '../../composables/useToday'
import { listRowBudget } from '../../lib/mountBudget'
import { groupTasks, sortTasks, type TaskGroup } from '../../lib/taskQuery'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { CreateTaskRequest, Task, Uuid } from '../../types'

/** Top-level rows each group renders before "Show N more", and how many more each click shows. */
export const GROUP_LIMIT = 100

/** Task rows a list's first render may mount, about a screenful, so its first paint is complete. */
const FIRST_ROWS = 20

export interface TaskRowItem {
  kind: 'task'
  /** `t:<id>` */
  key: string
  task: Task
  level: 1 | 2
  groupKey: string
  parentId: Uuid | null
  hasChildren: boolean
  expanded: boolean
  /** Shown only because a subtask matches the filters (8.7): greyed and expanded. */
  context: boolean
  /** Kept visible after creation although the filters hide it ("Hidden by filters"). */
  kept: boolean
  /** The last row of its parent's block (the thread line ends here). */
  last: boolean
}

export interface AddRowItem {
  kind: 'add'
  /** `a:<group>` (end of the group), `at:<group>` (the header "+"), `as:<parent id>` (subtasks). */
  key: string
  groupKey: string
  parentId: Uuid | null
  placement: 'Top' | 'Bottom'
}

export interface MoreRowItem {
  kind: 'more'
  /** `m:<group>` */
  key: string
  groupKey: string
  /** How many more one click shows. */
  count: number
}

export type RowItem = TaskRowItem | AddRowItem | MoreRowItem

export interface GroupView {
  key: string
  group: TaskGroup
  /** False for Group by None, which has no header row. */
  hasHeader: boolean
  collapsed: boolean
  /** Every visible top-level task of the group, in order (the "Select all in" set). */
  topIds: Uuid[]
  /** The rows to render; empty while collapsed. */
  rows: RowItem[]
  /** What its add rows set; null where the group has no add row (Overdue, Completed, guests). */
  addDefaults: Partial<CreateTaskRequest> | null
}

export interface ListModel {
  groups: ComputedRef<GroupView[]>
  /** Header rows and rows, as their keys (`h:<group>` for headers), in display order. */
  navKeys: ComputedRef<string[]>
  rowByKey: ComputedRef<Map<string, RowItem>>
  groupByKey: ComputedRef<Map<string, GroupView>>
  /** Every rendered task row (top-level and subtask), in order: the panel's J/K order. */
  taskOrder: ComputedRef<Uuid[]>
  /** The rendered top-level tasks in order (range selection, Mod+A). */
  renderedTopIds: ComputedRef<Uuid[]>
  /** True when any rendered task with subtasks is expanded (the toolbar's Collapse all). */
  anyExpanded: ComputedRef<boolean>
  /** Visible top-level tasks of the whole view. */
  visibleCount: ComputedRef<number>
  openAdd: Ref<string | null>
  isCollapsed(groupKey: string): boolean
  toggleGroup(groupKey: string, collapsed?: boolean): void
  toggleExpanded(taskId: Uuid, expanded?: boolean): void
  expandAll(): void
  collapseAll(): void
  showMore(groupKey: string): void
  /** The visible top-level siblings of a task in its group (Alt+arrows, the Move items, drops). */
  siblingIds(taskId: Uuid): Uuid[]
  groupOf(taskId: Uuid): GroupView | null
  /** Keeps a task just added from an add row where it was added (see Pins above). */
  pin(taskId: Uuid, where: { groupKey: string; placement: 'Top' | 'Bottom'; parentId: Uuid | null }): void
}

export function headerKey(groupKey: string): string {
  return `h:${groupKey}`
}

export function taskKey(id: Uuid): string {
  return `t:${id}`
}

export function useListModel(canEdit: ComputedRef<boolean>): ListModel {
  const ctx = useListPageContext()
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const session = useSession()
  const today = useToday()

  // ---- Session state ----

  /** Per-task expansion chosen this session (8.7: single-task changes last only for the session). */
  const overrides = reactive(new Map<Uuid, boolean>())
  /** Context parents collapsed by hand; cleared whenever the filters or the search change. */
  const contextCollapsed = shallowRef(new Set<Uuid>())
  const limits = reactive<Record<string, number>>({})
  const openAdd = ref<string | null>(null)
  /** Where each task was last rendered (group and index), for the completion hold. Not reactive. */
  const placements = new Map<Uuid, { groupKey: string; index: number }>()
  /** Tasks added from an add row, kept where they were added (see Pins above), by add order. */
  const pins = reactive(new Map<Uuid, { groupKey: string; top: boolean; parentId: Uuid | null; seq: number }>())
  let pinSeq = 0
  /** Task rows the last `groups` evaluation rendered; the next may render one batch more. Not reactive. */
  let renderedRows = 0

  watch([() => ctx.config.value.groupBy, () => ctx.config.value.sort.field], () => pins.clear())

  watch(
    [() => ctx.filters.value, () => ctx.search.value],
    () => {
      if (contextCollapsed.value.size) contextCollapsed.value = new Set()
    },
    { deep: true },
  )

  // A list change starts over (the page stays mounted between lists).
  watch(
    () => ctx.listId.value,
    () => {
      overrides.clear()
      contextCollapsed.value = new Set()
      for (const k of Object.keys(limits)) delete limits[k]
      openAdd.value = null
      placements.clear()
      pins.clear()
      renderedRows = 0
    },
  )

  function defaultCollapsed(g: TaskGroup): boolean {
    return g.kind === 'completed' || g.status?.category === 'Done'
  }

  function isCollapsed(groupKey: string): boolean {
    const stored = ctx.config.value.collapsedGroups[groupKey]
    if (stored !== undefined) return stored
    const g = baseGroups.value.find((x) => x.key === groupKey)
    return g ? defaultCollapsed(g) : false
  }

  function isExpandedTask(t: Task, context: boolean): boolean {
    if (context) return !contextCollapsed.value.has(t.id)
    const own = overrides.get(t.id)
    return own ?? ctx.config.value.subtasks === 'expanded'
  }

  // ---- Groups ----

  /** The tasks with the pinned ones moved to the top (latest first) or the end (in add order). */
  function pinned(list: Task[], applies: (t: Task) => boolean): Task[] {
    const tops: Task[] = []
    const ends: Task[] = []
    const rest: Task[] = []
    for (const t of list) {
      const p = applies(t) ? pins.get(t.id) : undefined
      if (!p) rest.push(t)
      else (p.top ? tops : ends).push(t)
    }
    if (!tops.length && !ends.length) return list
    const seq = (t: Task) => pins.get(t.id)?.seq ?? 0
    return [...tops.sort((a, b) => seq(b) - seq(a)), ...rest, ...ends.sort((a, b) => seq(a) - seq(b))]
  }

  const filterContext = computed(() => ({
    meId: session.user?.id ?? '',
    today: today.value,
    statusById: (id: Uuid) => workspaces.statusById(id),
    statuses: ctx.statuses.value,
  }))

  /** The groups with their sorted, visible top-level tasks, before rows are built. */
  const baseGroups = computed<TaskGroup[]>(() => {
    const cfg = ctx.config.value
    const visible = ctx.result.value.visibleIds
    const top = ctx.allTasks.value.filter((t) => !t.parentTaskId && visible.has(t.id))
    const groups = groupTasks(sortTasks(top, cfg.sort), cfg.groupBy, filterContext.value)
    const held = tasks.recentlyCompleted

    // Pins: a status group already has them in place (the server's placement is per status).
    if (pins.size && cfg.sort.field === 'manual' && cfg.groupBy !== 'status') {
      for (const g of groups) g.tasks = pinned(g.tasks, (t) => pins.get(t.id)?.groupKey === g.key)
    }

    // Completion hold: a task completed a moment ago stays in its old group and place.
    if (held.size) {
      for (const id of held) {
        const memo = placements.get(id)
        if (!memo) continue
        const from = groups.find((g) => g.tasks.some((t) => t.id === id))
        if (!from || from.key === memo.groupKey) continue
        const to = groups.find((g) => g.key === memo.groupKey)
        if (!to) continue
        const t = from.tasks.find((x) => x.id === id)
        if (!t) continue
        from.tasks = from.tasks.filter((x) => x.id !== id)
        const at = Math.min(memo.index, to.tasks.length)
        to.tasks = [...to.tasks.slice(0, at), t, ...to.tasks.slice(at)]
      }
    }
    for (const g of groups) {
      g.tasks.forEach((t, index) => {
        if (!held.has(t.id)) placements.set(t.id, { groupKey: g.key, index })
      })
    }

    if (cfg.showCompleted) return groups
    return groups.filter((g) => g.kind !== 'completed' && g.status?.category !== 'Done')
  })

  /** Every group with all the rows it shows (before progressive mounting). */
  const fullGroups = computed<GroupView[]>(() => {
    const cfg = ctx.config.value
    const res = ctx.result.value
    const visible = res.visibleIds
    const edit = canEdit.value
    const open = openAdd.value

    return baseGroups.value.map((g) => {
      // Only Group by None's single group has no header; its Completed group has one, like any other.
      const hasHeader = g.kind !== 'none'
      const collapsed = hasHeader && (cfg.collapsedGroups[g.key] ?? defaultCollapsed(g))
      const addDefaults = edit && g.createDefaults ? g.createDefaults : null
      const rows: RowItem[] = []
      if (!collapsed) {
        if (addDefaults && open === `at:${g.key}`) {
          rows.push({ kind: 'add', key: `at:${g.key}`, groupKey: g.key, parentId: null, placement: 'Top' })
        }
        const limit = limits[g.key] ?? GROUP_LIMIT
        const shown = g.tasks.length > limit ? g.tasks.slice(0, limit) : g.tasks
        for (const t of shown) {
          const context = res.contextIds.has(t.id)
          const kids = tasks.subtasksOf(t.id).filter((s) => visible.has(s.id))
          const hasChildren = t.subtaskCount > 0 || kids.length > 0
          const expanded = hasChildren && isExpandedTask(t, context)
          // The add row also opens under a task without subtasks yet (the row menu's "Add subtask").
          const showAddSub = edit && (expanded || open === `as:${t.id}`)
          let sortedKids = expanded ? sortTasks(kids, cfg.sort) : []
          if (pins.size && cfg.sort.field === 'manual') sortedKids = pinned(sortedKids, (s) => pins.get(s.id)?.parentId === t.id)
          rows.push({
            kind: 'task',
            key: taskKey(t.id),
            task: t,
            level: 1,
            groupKey: g.key,
            parentId: null,
            hasChildren,
            expanded,
            context,
            kept: res.keptIds.has(t.id),
            last: false,
          })
          sortedKids.forEach((s, i) => {
            rows.push({
              kind: 'task',
              key: taskKey(s.id),
              task: s,
              level: 2,
              groupKey: g.key,
              parentId: t.id,
              hasChildren: false,
              expanded: false,
              context: false,
              kept: res.keptIds.has(s.id),
              last: !showAddSub && i === sortedKids.length - 1,
            })
          })
          if (showAddSub) rows.push({ kind: 'add', key: `as:${t.id}`, groupKey: g.key, parentId: t.id, placement: 'Bottom' })
        }
        if (g.tasks.length > shown.length) {
          rows.push({ kind: 'more', key: `m:${g.key}`, groupKey: g.key, count: Math.min(GROUP_LIMIT, g.tasks.length - shown.length) })
        }
        if (addDefaults) rows.push({ kind: 'add', key: `a:${g.key}`, groupKey: g.key, parentId: null, placement: 'Bottom' })
      }
      return { key: g.key, group: g, hasHeader, collapsed, topIds: g.tasks.map((t) => t.id), rows, addDefaults }
    })
  })

  // ---- Progressive mounting ----

  /** Rows were held back: the next step follows on the next frame. */
  let behind = false
  const step = ref(0)
  let frame = 0
  /** Task rows the last `groups` evaluation added. Not reactive. */
  let added = 0

  /** The groups as rendered: fullGroups, cut after the task rows this frame may mount. */
  const groups = computed<GroupView[]>(() => {
    void step.value
    const batch = listRowBudget.batch()
    const limit = renderedRows + (renderedRows === 0 ? Math.max(FIRST_ROWS, batch) : batch)
    let n = 0
    let cut = false
    const out = fullGroups.value.map((g) => {
      if (cut) return g.rows.length ? { ...g, rows: [] } : g
      let tasksIn = 0
      for (const r of g.rows) if (r.kind === 'task') tasksIn++
      if (n + tasksIn <= limit) {
        n += tasksIn
        return g
      }
      // The cut falls in this group: its rows up to the limit, and none of its later rows.
      const rows: RowItem[] = []
      for (const r of g.rows) {
        if (r.kind === 'task') {
          if (n >= limit) break
          n++
        }
        rows.push(r)
      }
      cut = true
      return { ...g, rows }
    })
    added = n - renderedRows
    renderedRows = n
    behind = cut
    return out
  })

  /** One frame's step: one batch more, timed until its render is done. */
  function nextStep(): void {
    frame = 0
    const at = performance.now()
    added = 0
    step.value++
    void nextTick(() => listRowBudget.record(added, performance.now() - at))
  }

  watch(
    groups,
    () => {
      if (behind && !frame) frame = requestAnimationFrame(nextStep)
    },
    { flush: 'post', immediate: true },
  )
  onScopeDispose(() => {
    if (frame) cancelAnimationFrame(frame)
  })

  const navKeys = computed(() => {
    const out: string[] = []
    for (const g of groups.value) {
      if (g.hasHeader) out.push(headerKey(g.key))
      for (const r of g.rows) out.push(r.key)
    }
    return out
  })

  const rowByKey = computed(() => {
    const m = new Map<string, RowItem>()
    for (const g of groups.value) for (const r of g.rows) m.set(r.key, r)
    return m
  })

  const groupByKey = computed(() => new Map(groups.value.map((g) => [g.key, g])))

  // These two count rows still being mounted (progressive mounting) as rendered.
  const taskOrder = computed(() => {
    const out: Uuid[] = []
    for (const g of fullGroups.value) for (const r of g.rows) if (r.kind === 'task') out.push(r.task.id)
    return out
  })

  const renderedTopIds = computed(() => {
    const out: Uuid[] = []
    for (const g of fullGroups.value) for (const r of g.rows) if (r.kind === 'task' && r.level === 1) out.push(r.task.id)
    return out
  })

  /** Some visible parent shows its subtasks, rendered or not (in a collapsed group, past "Show N more"). */
  const anyExpanded = computed(() => {
    const context = ctx.result.value.contextIds
    for (const g of baseGroups.value) {
      for (const t of g.tasks) {
        const isContext = context.has(t.id)
        if ((t.subtaskCount > 0 || isContext) && isExpandedTask(t, isContext)) return true
      }
    }
    return false
  })

  const visibleCount = computed(() => groups.value.reduce((n, g) => n + g.topIds.length, 0))

  // ---- Actions ----

  function pin(taskId: Uuid, where: { groupKey: string; placement: 'Top' | 'Bottom'; parentId: Uuid | null }): void {
    pins.set(taskId, { groupKey: where.groupKey, top: where.placement === 'Top', parentId: where.parentId, seq: ++pinSeq })
  }

  function toggleGroup(groupKey: string, collapsed?: boolean): void {
    const next = collapsed ?? !isCollapsed(groupKey)
    ctx.config.value.collapsedGroups = { ...ctx.config.value.collapsedGroups, [groupKey]: next }
  }

  function toggleExpanded(taskId: Uuid, expanded?: boolean): void {
    const context = ctx.result.value.contextIds.has(taskId)
    const t = tasks.get(taskId)
    if (!t) return
    const current = isExpandedTask(t, context)
    const next = expanded ?? !current
    if (context) {
      const set = new Set(contextCollapsed.value)
      if (next) set.delete(taskId)
      else set.add(taskId)
      contextCollapsed.value = set
    } else {
      overrides.set(taskId, next)
    }
    if (!next && openAdd.value === `as:${taskId}`) openAdd.value = null
  }

  function expandAll(): void {
    ctx.config.value.subtasks = 'expanded'
    overrides.clear()
    contextCollapsed.value = new Set()
  }

  function collapseAll(): void {
    ctx.config.value.subtasks = 'collapsed'
    overrides.clear()
    contextCollapsed.value = new Set(ctx.result.value.contextIds)
    if (openAdd.value?.startsWith('as:')) openAdd.value = null
  }

  function showMore(groupKey: string): void {
    limits[groupKey] = (limits[groupKey] ?? GROUP_LIMIT) + GROUP_LIMIT
  }

  function groupOf(taskId: Uuid): GroupView | null {
    for (const g of groups.value) if (g.topIds.includes(taskId)) return g
    return null
  }

  function siblingIds(taskId: Uuid): Uuid[] {
    const g = groupOf(taskId)
    if (!g) return []
    // The rendered top-level rows of the group (the visible siblings, 9.3).
    const out: Uuid[] = []
    for (const r of g.rows) if (r.kind === 'task' && r.level === 1) out.push(r.task.id)
    return out
  }

  return {
    groups,
    navKeys,
    rowByKey,
    groupByKey,
    taskOrder,
    renderedTopIds,
    anyExpanded,
    visibleCount,
    openAdd,
    isCollapsed,
    toggleGroup,
    toggleExpanded,
    expandAll,
    collapseAll,
    showMore,
    siblingIds,
    groupOf,
    pin,
  }
}

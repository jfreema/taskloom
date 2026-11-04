// The Calendar view's state and rules (spec 8.9), shared by its header, grid, tray and agenda.
// - The visible month is session-only and starts at the current month.
// - Which tasks: every task of the list (top-level and subtasks) that matches the filters and the
//   search on its own; Done tasks only while "Show completed tasks" is on (a task completed a
//   moment ago stays until the store lets it go). Tasks created on this page always show (8.1).
// - Each day lists open tasks first, then by position. Overdue (open, due before today) is oldest
//   first; No due date (open) is by position.
// - Drops reschedule with one undoable update: day to day shifts the start date by the same number
//   of days; a task without a due date pulls a later start back to the new day; "No due date"
//   clears the due date and keeps the start.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useListPageContext } from '../../composables/useListPageContext'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useToday } from '../../composables/useToday'
import {
  addDaysIso,
  addMonthsIso,
  diffDays,
  formatDateMedium,
  monthGrid,
  startOfMonthIso,
  weekdayNames,
} from '../../lib/dates'
import { compareManual, matchesFilters, type FilterContext } from '../../lib/taskQuery'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { IsoDate, Status, Task, TaskPatch, Uuid } from '../../types'

/** Case- and accent-insensitive, like the toolbar search everywhere else (taskQuery's fold). */
function fold(s: string): string {
  return s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase()
}

export interface CalendarModel {
  listId: ComputedRef<Uuid>
  today: Readonly<Ref<IsoDate>>
  /** The first day of the visible month. */
  month: Ref<IsoDate>
  weeks: ComputedRef<IsoDate[][]>
  weekdays: { short: string; long: string; narrow: string }[]
  canEdit: ComputedRef<boolean>
  loading: ComputedRef<boolean>
  /** Visible tasks with a due date, by day, in display order. */
  byDay: ComputedRef<Map<IsoDate, Task[]>>
  /** Any visible task has a due date (in any month). */
  hasDated: ComputedRef<boolean>
  overdue: ComputedRef<Task[]>
  noDue: ComputedRef<Task[]>
  trayCount: ComputedRef<number>
  inMonth(iso: IsoDate): boolean
  inGrid(iso: IsoDate): boolean
  status(t: Task): Status | undefined
  isDone(t: Task): boolean
  parentTitle(t: Task): string | null
  goToMonth(iso: IsoDate): void
  shiftMonth(n: number): void
  goToToday(): void
  reschedule(id: Uuid, day: IsoDate | null): void
  createOn(day: IsoDate): void
  open(id: Uuid, o?: { focus?: boolean }): void
  openId: ComputedRef<Uuid | null>
}

export function useCalendarModel(): CalendarModel {
  const ctx = useListPageContext()
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const session = useSession()
  const ui = useUi()
  const panel = useTaskPanel()
  const today = useToday()

  const month = ref<IsoDate>(startOfMonthIso(today.value))
  const weeks = computed(() => monthGrid(month.value))
  const weekdays = weekdayNames()
  const gridDays = computed(() => new Set(weeks.value.flat()))

  const filterContext = computed<FilterContext>(() => ({
    meId: session.user?.id ?? '',
    today: today.value,
    statusById: (id: Uuid) => workspaces.statusById(id),
  }))

  function status(t: Task): Status | undefined {
    return workspaces.statusById(t.statusId)
  }

  function isDone(t: Task): boolean {
    return tasks.isDone(t)
  }

  const visible = computed<Task[]>(() => {
    const f = ctx.filters.value
    const q = fold(ctx.search.value.trim())
    const kept = ctx.createdThisSession.value
    const held = tasks.recentlyCompleted
    const showCompleted = ctx.config.value.showCompleted
    const fc = filterContext.value
    return ctx.allTasks.value.filter((t) => {
      if (!showCompleted && isDone(t) && !held.has(t.id)) return false
      if (kept.has(t.id)) return true
      return matchesFilters(t, f, fc) && (q === '' || fold(t.title).includes(q))
    })
  })

  const byDay = computed(() => {
    const map = new Map<IsoDate, Task[]>()
    for (const t of visible.value) {
      if (!t.dueDate) continue
      const day = map.get(t.dueDate)
      if (day) day.push(t)
      else map.set(t.dueDate, [t])
    }
    for (const list of map.values()) {
      list.sort((a, b) => Number(isDone(a)) - Number(isDone(b)) || compareManual(a, b))
    }
    return map
  })

  const hasDated = computed(() => byDay.value.size > 0)

  const overdue = computed(() =>
    visible.value
      .filter((t) => t.dueDate !== null && t.dueDate < today.value && !isDone(t))
      .sort((a, b) => (a.dueDate! < b.dueDate! ? -1 : a.dueDate! > b.dueDate! ? 1 : compareManual(a, b))),
  )

  const noDue = computed(() => visible.value.filter((t) => t.dueDate === null && !isDone(t)).sort(compareManual))

  const trayCount = computed(() => overdue.value.length + noDue.value.length)

  function inMonth(iso: IsoDate): boolean {
    return iso.slice(0, 7) === month.value.slice(0, 7)
  }

  function inGrid(iso: IsoDate): boolean {
    return gridDays.value.has(iso)
  }

  function parentTitle(t: Task): string | null {
    const id = t.parentTaskId
    return id ? (tasks.get(id)?.title ?? tasks.refs[id]?.title ?? null) : null
  }

  function goToMonth(iso: IsoDate): void {
    const first = startOfMonthIso(iso)
    if (first !== month.value) month.value = first
  }

  function shiftMonth(n: number): void {
    month.value = startOfMonthIso(addMonthsIso(month.value, n))
  }

  function goToToday(): void {
    goToMonth(today.value)
  }

  function reschedule(id: Uuid, day: IsoDate | null): void {
    const t = tasks.get(id)
    if (!t || !ctx.canEdit.value) return
    if (day === null) {
      if (t.dueDate === null) return
      void tasks.update(id, { dueDate: null }, { undo: 'Removed due date' }).catch(() => undefined)
      return
    }
    if (t.dueDate === day) return
    const patch: TaskPatch = { dueDate: day }
    if (t.startDate) {
      if (t.dueDate) patch.startDate = addDaysIso(t.startDate, diffDays(day, t.dueDate))
      else if (t.startDate > day) patch.startDate = day
    }
    void tasks
      .update(id, patch, { undo: `Rescheduled to ${formatDateMedium(day, today.value)}` })
      .catch(() => undefined)
  }

  function createOn(day: IsoDate): void {
    if (!ctx.canEdit.value) return
    ui.openQuickCreate({ ...ctx.createDefaults.value, listId: ctx.listId.value, dueDate: day })
  }

  function open(id: Uuid, o?: { focus?: boolean }): void {
    panel.open(id, o)
  }

  return {
    listId: ctx.listId,
    today,
    month,
    weeks,
    weekdays,
    canEdit: ctx.canEdit,
    loading: ctx.loading,
    byDay,
    hasDated,
    overdue,
    noDue,
    trayCount,
    inMonth,
    inGrid,
    status,
    isDone,
    parentTitle,
    goToMonth,
    shiftMonth,
    goToToday,
    reschedule,
    createOn,
    open,
    openId: panel.openId,
  }
}

/** "3 tasks", "1 task", "0 tasks". */
export function taskCount(n: number): string {
  return `${n} ${n === 1 ? 'task' : 'tasks'}`
}

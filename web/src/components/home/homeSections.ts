// Home's sections (8.5): which of my tasks show, in which section, and in what order. Pure, so the
// rules read in one place; HomeView feeds it the tasks store (never the load's response), so a task
// created in quick create or completed elsewhere moves at once.
import { addDaysIso, dueBucket, toIso, type DueBucket } from '../../lib/dates'
import { PRIORITY_ORDER, type IsoDate, type Task, type Uuid } from '../../types'

/** The scope control: "All mine" (the API's scope=mine) or "Assigned to me". */
export type HomeScope = 'mine' | 'assigned'

export type HomeSectionKey = DueBucket | 'completed'

export const HOME_SECTIONS: readonly HomeSectionKey[] = ['overdue', 'today', 'next7', 'later', 'none', 'completed']

export const HOME_SECTION_LABEL: Readonly<Record<HomeSectionKey, string>> = {
  overdue: 'Overdue',
  today: 'Today',
  next7: 'Next 7 days',
  later: 'Later',
  none: 'No due date',
  completed: 'Completed in the last 7 days',
}

/** Overdue, Today and Next 7 days start expanded; the rest start collapsed. */
export const DEFAULT_COLLAPSED: Readonly<Record<HomeSectionKey, boolean>> = {
  overdue: false,
  today: false,
  next7: false,
  later: true,
  none: true,
  completed: true,
}

export type HomeSections = Record<HomeSectionKey, Task[]>

/**
 * "All mine" is the API's scope=mine: assigned to me, or created by me with nobody assigned.
 * "Assigned to me" keeps only the tasks whose assignees include me.
 */
export function isMine(t: Task, meId: Uuid, scope: HomeScope = 'mine'): boolean {
  if (t.assigneeIds.includes(meId)) return true
  return scope === 'mine' && t.createdById === meId && t.assigneeIds.length === 0
}

/** Completed on a local day from today − 7 on: the window the page loads with `doneSince`. */
export function completedThisWeek(t: Task, today: IsoDate): boolean {
  if (!t.completedAt) return false
  const at = new Date(t.completedAt)
  return !Number.isNaN(at.getTime()) && toIso(at) >= addDaysIso(today, -7)
}

type Compare = (a: Task, b: Task) => number

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function timeOf(ts: string | null): number {
  const t = ts ? Date.parse(ts) : Number.NaN
  return Number.isNaN(t) ? 0 : t
}

const byDue: Compare = (a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? '')
const byPriority: Compare = (a, b) => PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority]
const byTitle: Compare = (a, b) => collator.compare(a.title, b.title)
const byCreatedNewest: Compare = (a, b) => timeOf(b.createdAt) - timeOf(a.createdAt)
const byCompletedNewest: Compare = (a, b) => timeOf(b.completedAt) - timeOf(a.completedAt)
const byId: Compare = (a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)

const ORDER: Record<HomeSectionKey, Compare[]> = {
  overdue: [byDue, byPriority, byTitle],
  today: [byPriority, byTitle],
  next7: [byDue, byPriority, byTitle],
  later: [byDue, byPriority, byTitle],
  none: [byPriority, byCreatedNewest],
  completed: [byCompletedNewest],
}

function compareFor(key: HomeSectionKey): Compare {
  const steps = [...ORDER[key], byId]
  return (a, b) => {
    for (const step of steps) {
      const d = step(a, b)
      if (d !== 0) return d
    }
    return 0
  }
}

export interface SectionInput {
  /** My tasks in the current workspace and scope, pending deletes left out. */
  tasks: Iterable<Task>
  today: IsoDate
  isDone: (t: Task) => boolean
  /** Inside the 1s completion hold (8.1): still in its due section, with the done look. */
  held: ReadonlySet<Uuid>
}

/** Open tasks by due bucket, done ones (once their hold ends) in Completed when done this week. */
export function buildSections({ tasks, today, isDone, held }: SectionInput): HomeSections {
  const out: HomeSections = { overdue: [], today: [], next7: [], later: [], none: [], completed: [] }
  for (const t of tasks) {
    if (isDone(t) && !held.has(t.id)) {
      if (completedThisWeek(t, today)) out.completed.push(t)
      continue
    }
    out[dueBucket(t, today)].push(t)
  }
  for (const key of HOME_SECTIONS) out[key].sort(compareFor(key))
  return out
}

/** "Good morning" from 05:00 to 11:59, "Good afternoon" to 17:59, else "Good evening", then the first name. */
export function greetingFor(hour: number, displayName: string | null | undefined): string {
  const part = hour >= 5 && hour < 12 ? 'morning' : hour >= 12 && hour < 18 ? 'afternoon' : 'evening'
  const first = displayName?.trim().split(/\s+/)[0]
  return first ? `Good ${part}, ${first}` : `Good ${part}`
}

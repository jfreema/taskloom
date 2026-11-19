// Folding the activity feed (Look 2). A run of small changes by one person, each within ten minutes
// of the one before and with no comment between them, shows as one line ("Ana updated the status,
// priority and due date · 3 changes"), which opens to the single lines. Runs of fewer than three
// changes stay as they are, and "completed this and created the next one" always stands alone.
import type { Activity, ActivityKind, Comment, IsoTimestamp, User } from '../../types'

export const RUN_GAP_MS = 10 * 60 * 1000
export const RUN_MIN = 3

export type FeedEntry =
  | { kind: 'created'; key: string; at: IsoTimestamp; actor: User }
  | { kind: 'system'; key: string; at: IsoTimestamp; activity: Activity }
  | { kind: 'comment'; key: string; at: IsoTimestamp; comment: Comment }
  /** `at` is the newest change's time; `key` the first change's id, so it stays put as the run grows. */
  | { kind: 'run'; key: string; at: IsoTimestamp; actor: User; items: Activity[] }

/** What each kind of change is called in a run's summary. Kinds without a name never join a run. */
const FIELD: Partial<Record<ActivityKind, string>> = {
  Renamed: 'name',
  StatusChanged: 'status',
  AssigneeAdded: 'assignees',
  AssigneeRemoved: 'assignees',
  DueDateChanged: 'due date',
  StartDateChanged: 'start date',
  PriorityChanged: 'priority',
  TagAdded: 'tags',
  TagRemoved: 'tags',
  DescriptionChanged: 'description',
  MovedList: 'list',
  AttachmentAdded: 'attachments',
  AttachmentRemoved: 'attachments',
  SubtaskAdded: 'subtasks',
  RepeatChanged: 'repeat',
}

function joins(prev: Activity, next: Activity): boolean {
  return (
    FIELD[next.kind] !== undefined &&
    next.actor.id === prev.actor.id &&
    Date.parse(next.createdAt) - Date.parse(prev.createdAt) <= RUN_GAP_MS
  )
}

/** The feed with each run of `RUN_MIN` or more small changes folded into one entry. Entries must be oldest first. */
export function foldRuns(entries: readonly FeedEntry[]): FeedEntry[] {
  const out: FeedEntry[] = []
  let run: Activity[] = []
  const flush = (): void => {
    if (run.length >= RUN_MIN) {
      out.push({ kind: 'run', key: `run-${run[0].id}`, at: run[run.length - 1].createdAt, actor: run[0].actor, items: run })
    } else {
      for (const a of run) out.push({ kind: 'system', key: a.id, at: a.createdAt, activity: a })
    }
    run = []
  }
  for (const e of entries) {
    if (e.kind === 'system' && FIELD[e.activity.kind] !== undefined) {
      if (run.length && !joins(run[run.length - 1], e.activity)) flush()
      run.push(e.activity)
      continue
    }
    flush()
    out.push(e)
  }
  flush()
  return out
}

/** "updated the status, priority and due date"; past four fields, "… and 2 more". */
export function runSummary(items: readonly Activity[]): string {
  const fields: string[] = []
  for (const a of items) {
    const f = FIELD[a.kind]
    if (f && !fields.includes(f)) fields.push(f)
  }
  const shown = fields.length > 4 ? [...fields.slice(0, 3), `${fields.length - 3} more`] : fields
  const list = shown.length > 1 ? `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}` : (shown[0] ?? 'this task')
  return `updated the ${list}`
}

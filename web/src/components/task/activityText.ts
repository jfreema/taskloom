// The wording of system activity entries (8.10), as text segments so names can be bold without
// v-html. The actor's name is rendered separately, before these segments.
import { formatDateShort, isIsoDate } from '../../lib/dates'
import { PRIORITY_LABEL, type Activity, type IsoDate, type Priority } from '../../types'

export interface ActivitySegment {
  text: string
  strong?: boolean
}

function quoted(s: string | null): string {
  return `“${s ?? ''}”`
}

function day(s: string | null, today: IsoDate): string {
  if (!s) return ''
  return isIsoDate(s) ? formatDateShort(s, today) : s
}

function dateChange(what: string, from: string | null, to: string | null, today: IsoDate): ActivitySegment[] {
  if (!to) return [{ text: `removed the ${what}` }]
  if (!from) return [{ text: `set the ${what} to ${day(to, today)}` }]
  return [{ text: `changed the ${what} from ${day(from, today)} to ${day(to, today)}` }]
}

function priorityLabel(p: string): string {
  return p in PRIORITY_LABEL ? PRIORITY_LABEL[p as Priority] : p
}

export function activitySegments(a: Pick<Activity, 'kind' | 'from' | 'to'>, today: IsoDate): ActivitySegment[] {
  const { from, to } = a
  switch (a.kind) {
    case 'Renamed':
      return [{ text: `renamed this from ${quoted(from)} to ${quoted(to)}` }]
    case 'StatusChanged':
      if (!from) return [{ text: 'set status to ' }, { text: to ?? '', strong: true }]
      return [{ text: 'changed status from ' }, { text: from, strong: true }, { text: ' to ' }, { text: to ?? '', strong: true }]
    case 'AssigneeAdded':
      return [{ text: `assigned ${to ?? from ?? 'someone'}` }]
    case 'AssigneeRemoved':
      return [{ text: `unassigned ${from ?? to ?? 'someone'}` }]
    case 'DueDateChanged':
      return dateChange('due date', from, to, today)
    case 'StartDateChanged':
      return dateChange('start date', from, to, today)
    case 'PriorityChanged':
      return to && to !== 'None' ? [{ text: `set priority to ${priorityLabel(to)}` }] : [{ text: 'cleared the priority' }]
    case 'TagAdded':
      return [{ text: `added tag ${to ?? from ?? ''}` }]
    case 'TagRemoved':
      return [{ text: `removed tag ${from ?? to ?? ''}` }]
    case 'DescriptionChanged':
      return [{ text: 'updated the description' }]
    case 'MovedList':
      return [{ text: `moved this from ${from ?? 'another list'} to ${to ?? 'another list'}` }]
    case 'AttachmentAdded':
      return [{ text: `attached ${to ?? from ?? 'a file'}` }]
    case 'AttachmentRemoved':
      return [{ text: `removed ${from ?? to ?? 'a file'}` }]
    case 'SubtaskAdded':
      return [{ text: `added subtask ${quoted(to ?? from)}` }]
    default:
      return [{ text: 'changed this task' }]
  }
}

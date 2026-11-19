// The wording of Inbox rows (Features 2), as text segments so names can be bold without v-html.
// The actor's name is rendered separately, before these segments; reminders have no actor.
import { formatDateShort, isIsoDate, toIso } from '../../lib/dates'
import type { InboxItem, IsoDate } from '../../types'
import { activitySegments, type ActivitySegment } from '../task/activityText'

export function inboxSegments(n: Pick<InboxItem, 'kind' | 'from' | 'to'>, today: IsoDate): ActivitySegment[] {
  switch (n.kind) {
    case 'Assigned':
      return [{ text: 'assigned this to you' }]
    case 'Mentioned':
      return [{ text: 'mentioned you' }]
    case 'Commented':
      return [{ text: 'commented' }]
    case 'Completed':
      return [{ text: 'completed this' }]
    case 'StatusChanged':
    case 'DueDateChanged':
    case 'PriorityChanged':
      return activitySegments({ kind: n.kind, from: n.from, to: n.to }, today)
    case 'DueToday':
      return [{ text: 'Due today', strong: true }]
    case 'Overdue':
      return n.to && isIsoDate(n.to)
        ? [{ text: 'Overdue', strong: true }, { text: `, was due ${formatDateShort(n.to, today)}` }]
        : [{ text: 'Overdue', strong: true }]
    default:
      return [{ text: 'changed this task' }]
  }
}

/** The comment a row quotes (mentions and comments), or null. */
export function inboxQuote(n: Pick<InboxItem, 'kind' | 'to'>): string | null {
  return (n.kind === 'Mentioned' || n.kind === 'Commented') && n.to ? n.to : null
}

/** The row in one sentence, for screen readers: "Ana Lee assigned this to you: Book venue". */
export function inboxLabel(n: InboxItem, today: IsoDate): string {
  const what = inboxSegments(n, today).map((s) => s.text).join('')
  const who = n.actor ? `${n.actor.displayName} ` : ''
  const quote = inboxQuote(n)
  return `${n.readAt ? '' : 'Unread. '}${who}${what}: ${n.taskTitle}${quote ? `. “${quote}”` : ''}`
}

export type InboxGroupKey = 'today' | 'yesterday' | 'earlier'

export const INBOX_GROUP_LABEL: Record<InboxGroupKey, string> = { today: 'Today', yesterday: 'Yesterday', earlier: 'Earlier' }

/** Which day heading a row sits under, by the reader's own calendar. */
export function inboxGroup(createdAt: string, today: IsoDate, yesterday: IsoDate): InboxGroupKey {
  const day = toIso(new Date(createdAt))
  return day >= today ? 'today' : day === yesterday ? 'yesterday' : 'earlier'
}

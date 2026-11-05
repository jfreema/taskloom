// Calendar dates ("YYYY-MM-DD", no time zone) and timestamps. Dates are local calendar days:
// they are parsed to local midnight, never with `new Date('YYYY-MM-DD')` (which means UTC midnight).
// Text uses Intl in the browser's locale (9.7); the words Today, Tomorrow and Yesterday are UI copy.
import { addDays, addMonths, differenceInCalendarDays, endOfMonth, startOfMonth, startOfWeek } from 'date-fns'
import type { IsoDate, IsoTimestamp, Task } from '../types'

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

export function isIsoDate(s: unknown): s is IsoDate {
  if (typeof s !== 'string') return false
  const m = ISO_DATE.exec(s)
  if (!m) return false
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return d.getFullYear() === Number(m[1]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[3])
}

// ---- Conversion ----

/** The local calendar date of `d`. */
export function toIso(d: Date): IsoDate {
  const y = String(d.getFullYear()).padStart(4, '0')
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export function todayIso(): IsoDate {
  return toIso(new Date())
}

/** Local midnight of the date. An invalid string gives an Invalid Date. */
export function fromIso(s: IsoDate): Date {
  const m = ISO_DATE.exec(s)
  if (!m) return new Date(Number.NaN)
  return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
}

export function addDaysIso(s: IsoDate, n: number): IsoDate {
  return toIso(addDays(fromIso(s), n))
}

export function addMonthsIso(s: IsoDate, n: number): IsoDate {
  return toIso(addMonths(fromIso(s), n))
}

/** a − b in calendar days (DST-safe): diffDays('2026-11-06', '2026-11-04') === 2. */
export function diffDays(a: IsoDate, b: IsoDate): number {
  return differenceInCalendarDays(fromIso(a), fromIso(b))
}

/** The first day of the month that contains `s`. */
export function startOfMonthIso(s: IsoDate): IsoDate {
  return toIso(startOfMonth(fromIso(s)))
}

// ---- Formatting ----

const formatters = new Map<string, Intl.DateTimeFormat>()

function formatter(options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = JSON.stringify(options)
  let f = formatters.get(key)
  if (!f) {
    f = new Intl.DateTimeFormat(undefined, options)
    formatters.set(key, f)
  }
  return f
}

function sameYear(a: IsoDate, b: IsoDate): boolean {
  return a.slice(0, 4) === b.slice(0, 4)
}

/** "Thu" */
export function formatWeekdayShort(s: IsoDate): string {
  return formatter({ weekday: 'short' }).format(fromIso(s))
}

/** "Nov 12", or "Nov 12, 2027" outside the current year. */
export function formatDateShort(s: IsoDate, today: IsoDate = todayIso()): string {
  const d = fromIso(s)
  if (Number.isNaN(d.getTime())) return ''
  return sameYear(s, today)
    ? formatter({ month: 'short', day: 'numeric' }).format(d)
    : formatter({ month: 'short', day: 'numeric', year: 'numeric' }).format(d)
}

/** "Wednesday, November 4" (calendar cells and chip labels; no year). */
export function formatDateLong(s: IsoDate): string {
  const d = fromIso(s)
  if (Number.isNaN(d.getTime())) return ''
  return formatter({ weekday: 'long', month: 'long', day: 'numeric' }).format(d)
}

/** "Thu, Nov 5" (undo toasts and agenda headers), with the year outside the current one. */
export function formatDateMedium(s: IsoDate, today: IsoDate = todayIso()): string {
  const d = fromIso(s)
  if (Number.isNaN(d.getTime())) return ''
  return sameYear(s, today)
    ? formatter({ weekday: 'short', month: 'short', day: 'numeric' }).format(d)
    : formatter({ weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }).format(d)
}

/** "November 2026" */
export function formatMonthTitle(s: IsoDate): string {
  const d = fromIso(s)
  if (Number.isNaN(d.getTime())) return ''
  return formatter({ month: 'long', year: 'numeric' }).format(d)
}

/** formatDue's texts for `dueTextDay`: long views show the same few dates on many rows (S10). */
let dueTextDay = ''
const dueText = new Map<IsoDate, string>()

/**
 * Due-date text: "Today", "Tomorrow", "Yesterday", a weekday for 2 to 6 days ahead ("Fri"),
 * otherwise "Nov 12", or "Nov 12, 2027" outside the current year.
 */
export function formatDue(s: IsoDate, today: IsoDate): string {
  if (dueTextDay !== today) {
    dueTextDay = today
    dueText.clear()
  }
  let text = dueText.get(s)
  if (text === undefined) {
    text = dueTextOf(s, today)
    dueText.set(s, text)
  }
  return text
}

function dueTextOf(s: IsoDate, today: IsoDate): string {
  const diff = diffDays(s, today)
  if (Number.isNaN(diff)) return ''
  if (diff === 0) return 'Today'
  if (diff === 1) return 'Tomorrow'
  if (diff === -1) return 'Yesterday'
  if (diff > 1 && diff <= 6) return formatWeekdayShort(s)
  return formatDateShort(s, today)
}

/** Activity times: "just now", "5m", "2h", "Yesterday", "Nov 3" (or "Nov 3, 2025"). */
export function formatRelativeTime(ts: IsoTimestamp, now: Date = new Date()): string {
  const t = new Date(ts)
  if (Number.isNaN(t.getTime())) return ''
  const ms = now.getTime() - t.getTime()
  if (ms < 60_000) return 'just now'
  const minutes = Math.floor(ms / 60_000)
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h`
  if (differenceInCalendarDays(now, t) === 1) return 'Yesterday'
  return formatDateShort(toIso(t), toIso(now))
}

/** The full timestamp for tooltips: "Nov 3, 2026, 4:05 PM". */
export function formatTimestamp(ts: IsoTimestamp): string {
  const t = new Date(ts)
  if (Number.isNaN(t.getTime())) return ''
  return formatter({ dateStyle: 'medium', timeStyle: 'short' }).format(t)
}

// ---- Weeks and months ----

interface WeekInfo {
  firstDay: number
}
type LocaleWithWeekInfo = Intl.Locale & { getWeekInfo?: () => WeekInfo; weekInfo?: WeekInfo }

const weekStartCache = new Map<string, 0 | 1 | 6>()

function browserLocale(): string {
  return (typeof navigator !== 'undefined' && navigator.language) || 'en-US'
}

/**
 * The first day of the week (0 Sunday, 1 Monday, 6 Saturday) for the browser's locale, from
 * `Intl.Locale#getWeekInfo()` or `.weekInfo`; otherwise Sunday for en-US, en-CA and ja, Monday elsewhere.
 */
export function weekStartsOn(locale?: string): 0 | 1 | 6 {
  const tag = locale ?? browserLocale()
  const cached = weekStartCache.get(tag)
  if (cached !== undefined) return cached
  let result: 0 | 1 | 6 | null = null
  try {
    const loc = new Intl.Locale(tag) as LocaleWithWeekInfo
    const info = typeof loc.getWeekInfo === 'function' ? loc.getWeekInfo() : loc.weekInfo
    if (info && typeof info.firstDay === 'number') {
      const day = info.firstDay % 7 // Intl counts 1 (Monday) to 7 (Sunday)
      result = day === 0 || day === 6 ? day : 1
    }
  } catch {
    result = null
  }
  if (result === null) {
    const lower = tag.toLowerCase()
    result = lower === 'en-us' || lower === 'en-ca' || lower === 'ja' || lower.startsWith('ja-') ? 0 : 1
  }
  weekStartCache.set(tag, result)
  return result
}

/** The first day of the week that contains `s`. */
export function startOfWeekIso(s: IsoDate): IsoDate {
  return toIso(startOfWeek(fromIso(s), { weekStartsOn: weekStartsOn() }))
}

/** The last day of the week that contains `s`. */
export function endOfWeekIso(s: IsoDate): IsoDate {
  return addDaysIso(startOfWeekIso(s), 6)
}

/** The weeks shown for the month that contains `s`: 5 or 6 rows of 7 days, starting on weekStartsOn(). */
export function monthGrid(s: IsoDate): IsoDate[][] {
  const day = fromIso(s)
  if (Number.isNaN(day.getTime())) return []
  const last = endOfMonth(day)
  let cursor = startOfWeek(startOfMonth(day), { weekStartsOn: weekStartsOn() })
  const weeks: IsoDate[][] = []
  while (weeks.length < 5 || cursor <= last) {
    const week: IsoDate[] = []
    for (let i = 0; i < 7; i++) {
      week.push(toIso(cursor))
      cursor = addDays(cursor, 1)
    }
    weeks.push(week)
  }
  return weeks
}

/** Weekday names in display order for column headers, starting on weekStartsOn(). */
export function weekdayNames(): { short: string; long: string; narrow: string }[] {
  const start = startOfWeek(new Date(2026, 0, 7), { weekStartsOn: weekStartsOn() })
  return Array.from({ length: 7 }, (_, i) => {
    const d = addDays(start, i)
    return {
      short: formatter({ weekday: 'short' }).format(d),
      long: formatter({ weekday: 'long' }).format(d),
      narrow: formatter({ weekday: 'narrow' }).format(d),
    }
  })
}

// ---- Quick picks and buckets ----

export interface QuickPick {
  label: string
  /** null is the clear row. */
  date: IsoDate | null
  /** The resolved day: "Thu" within six days, otherwise "Nov 18". Empty on the clear row. */
  hint: string
}

function pickHint(date: IsoDate, today: IsoDate): string {
  const diff = diffDays(date, today)
  return diff >= 0 && diff <= 6 ? formatWeekdayShort(date) : formatDateShort(date, today)
}

/**
 * The date picker's quick picks (7.10): Today, Tomorrow, This weekend (the coming Saturday, or today
 * on a weekend), Next week (the next week-start day), In 2 weeks, then the clear row.
 */
export function quickPicks(today: IsoDate): QuickPick[] {
  const dow = fromIso(today).getDay()
  const weekend = dow === 6 || dow === 0 ? today : addDaysIso(today, 6 - dow)
  const toWeekStart = (weekStartsOn() - dow + 7) % 7 || 7
  const picks: { label: string; date: IsoDate }[] = [
    { label: 'Today', date: today },
    { label: 'Tomorrow', date: addDaysIso(today, 1) },
    { label: 'This weekend', date: weekend },
    { label: 'Next week', date: addDaysIso(today, toWeekStart) },
    { label: 'In 2 weeks', date: addDaysIso(today, 14) },
  ]
  return [...picks.map((p) => ({ ...p, hint: pickHint(p.date, today) })), { label: 'No date', date: null, hint: '' }]
}

export type DueBucket = 'overdue' | 'today' | 'next7' | 'later' | 'none'

export const DUE_BUCKET_LABEL: Record<DueBucket, string> = {
  overdue: 'Overdue',
  today: 'Today',
  next7: 'Next 7 days',
  later: 'Later',
  none: 'No due date',
}

/**
 * The Home and Group-by-due bucket of a task, by its due date only: before today, today,
 * today+1 to today+7, later, or none. Callers leave done tasks out first.
 */
export function dueBucket(t: Pick<Task, 'dueDate'>, today: IsoDate): DueBucket {
  if (!t.dueDate) return 'none'
  const diff = diffDays(t.dueDate, today)
  if (Number.isNaN(diff)) return 'none'
  if (diff < 0) return 'overdue'
  if (diff === 0) return 'today'
  if (diff <= 7) return 'next7'
  return 'later'
}

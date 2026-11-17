// Repeat rules (types.ts RepeatRule): their wording, the picker's presets, and the parts the
// custom dialog offers. The dates themselves are the server's job (Taskloom.Application
// Recurrence): the app never works out a next due date, it shows the one the server sets.
import { formatDateShort, fromIso, todayIso } from './dates'
import type { IsoDate, RepeatRequest, RepeatRule, RepeatUnit, Weekday } from '../types'

/** Index i is `Date.getDay() === i`. */
export const WEEKDAYS: readonly Weekday[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

const WORKWEEK: readonly Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']

const UNIT_WORDS: Record<RepeatUnit, [string, string]> = {
  Day: ['day', 'days'],
  Week: ['week', 'weeks'],
  Month: ['month', 'months'],
  Year: ['year', 'years'],
}

const weekdayFormats = new Map<string, Intl.DateTimeFormat>()

/** The day's name in the browser's locale: "Mon" (short) or "Monday" (long). 2026-11-15 is a Sunday. */
export function weekdayName(day: Weekday, width: 'short' | 'long' = 'short'): string {
  let f = weekdayFormats.get(width)
  if (!f) {
    f = new Intl.DateTimeFormat(undefined, { weekday: width })
    weekdayFormats.set(width, f)
  }
  return f.format(new Date(2026, 10, 15 + WEEKDAYS.indexOf(day)))
}

export function weekdayOf(date: IsoDate): Weekday {
  return WEEKDAYS[fromIso(date).getDay()]
}

/** "1st", "2nd", "3rd", "4th", "11th", "22nd". */
export function ordinal(n: number): string {
  const tens = n % 100
  if (tens >= 11 && tens <= 13) return `${n}th`
  return `${n}${({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th'}`
}

/** Which of its weekdays the date is in its month: 1 to 5. */
export function weekOfMonth(date: IsoDate): number {
  return Math.ceil(fromIso(date).getDate() / 7)
}

/** True when the date falls in the last seven days of its month (so it is the month's last such weekday). */
export function isLastWeekOfMonth(date: IsoDate): boolean {
  const d = fromIso(date)
  return d.getDate() + 7 > new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate()
}

/** A request with every field filled the way the server fills a rule it isn't given a value for (but the anchor). */
export function toRule(r: RepeatRequest): RepeatRule {
  return {
    unit: r.unit,
    every: r.every ?? 1,
    basis: r.basis ?? 'Schedule',
    weekdays: WEEKDAYS.filter((d) => r.weekdays?.includes(d)),
    monthDay: r.monthDay ?? null,
    monthWeek: r.monthWeek ?? null,
    monthWeekday: r.monthWeekday ?? null,
    until: r.until ?? null,
  }
}

/** The day of the month part: "the 16th", "the last day", "the 3rd Monday", "the last Friday". */
export function monthPart(r: Pick<RepeatRule, 'monthDay' | 'monthWeek' | 'monthWeekday'>): string {
  if (r.monthWeek !== null && r.monthWeekday) {
    return `the ${r.monthWeek < 0 ? 'last' : ordinal(r.monthWeek)} ${weekdayName(r.monthWeekday, 'long')}`
  }
  if (r.monthDay === -1) return 'the last day'
  return r.monthDay ? `the ${ordinal(r.monthDay)}` : 'the same day'
}

function sameDays(a: readonly Weekday[], b: readonly Weekday[]): boolean {
  return a.length === b.length && a.every((d) => b.includes(d))
}

/**
 * The rule in words, for the task panel, tooltips and the activity feed: "Daily", "Every weekday",
 * "Weekly on Mon, Wed", "Every 2 weeks on Fri", "Monthly on the 1st", "Every 3 months on the last
 * Friday", "Yearly", "2 weeks after done", then ", until Dec 31" when it ends.
 */
export function describeRepeat(rule: RepeatRule, today: IsoDate = todayIso()): string {
  const [one, many] = UNIT_WORDS[rule.unit] ?? ['time', 'times']
  const n = Math.max(1, rule.every || 1)
  let text: string
  if (rule.basis === 'Completion') {
    text = `${n} ${n === 1 ? one : many} after done`
  } else {
    const every = n === 1 ? null : `Every ${n} ${many}`
    switch (rule.unit) {
      case 'Day':
        text = every ?? 'Daily'
        break
      case 'Week': {
        const days = rule.weekdays.length ? rule.weekdays : []
        if (n === 1 && sameDays(days, WORKWEEK)) text = 'Every weekday'
        else {
          const on = days.length ? ` on ${days.map((d) => weekdayName(d)).join(', ')}` : ''
          text = `${every ?? 'Weekly'}${on}`
        }
        break
      }
      case 'Month':
        text = `${every ?? 'Monthly'} on ${monthPart(rule)}`
        break
      default:
        text = every ?? 'Yearly'
    }
  }
  return rule.until ? `${text}, until ${formatDateShort(rule.until, today)}` : text
}

/** A rule from an activity value (JSON written by the server), or null. */
export function parseRepeat(json: string | null): RepeatRule | null {
  if (!json) return null
  try {
    const raw = JSON.parse(json) as Partial<RepeatRule> | null
    if (!raw || typeof raw !== 'object' || !raw.unit) return null
    return toRule(raw as RepeatRequest)
  } catch {
    return null
  }
}

export interface RepeatPreset {
  key: string
  label: string
  rule: RepeatRequest
}

/**
 * The picker's one-click rules, worded for the task's due date (else today): Daily, Every weekday,
 * Weekly on <its weekday>, Monthly on <its day>, Yearly on <its date>.
 */
export function repeatPresets(anchor: IsoDate, today: IsoDate = todayIso()): RepeatPreset[] {
  const day = weekdayOf(anchor)
  const date = fromIso(anchor).getDate()
  return [
    { key: 'daily', label: 'Daily', rule: { unit: 'Day' } },
    { key: 'weekdays', label: 'Every weekday (Mon–Fri)', rule: { unit: 'Week', weekdays: [...WORKWEEK] } },
    { key: 'weekly', label: `Weekly on ${weekdayName(day, 'long')}`, rule: { unit: 'Week', weekdays: [day] } },
    { key: 'monthly', label: `Monthly on the ${ordinal(date)}`, rule: { unit: 'Month', monthDay: date } },
    { key: 'yearly', label: `Yearly on ${formatDateShort(anchor, today).replace(/, \d{4}$/, '')}`, rule: { unit: 'Year' } },
  ]
}

/** Whether a rule says the same as a request (the request's missing fields taken as the server's defaults). */
export function sameRule(a: RepeatRule | null, b: RepeatRequest | null): boolean {
  if (!a || !b) return a === null && b === null
  const r = toRule(b)
  return (
    a.unit === r.unit &&
    a.every === r.every &&
    a.basis === r.basis &&
    sameDays(a.weekdays, r.weekdays) &&
    a.monthDay === r.monthDay &&
    a.monthWeek === r.monthWeek &&
    a.monthWeekday === r.monthWeekday &&
    a.until === r.until
  )
}

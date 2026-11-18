// Typed dates (Look 1): "today", "tomorrow", "fri", "next fri", "this weekend", "next week",
// "in 2 weeks", "end of month", "nov 20", "20 nov", "11/20", "2026-11-20", "the 20th".
// Used two ways:
// - findTypedDates: every date phrase inside a task title, with its position, for quick add. A
//   phrase may start with "due", "on" or "by" ("Ship by fri"), which belongs to the phrase.
// - parseTypedDate: the whole text as one date, for the date picker's text box, which also takes a
//   bare day of the month ("20") and "yesterday".
// Meanings, for today = Wed Nov 18 2026 with weeks starting on Sunday:
// - "fri" is the coming Friday (Nov 20); a bare weekday is never today, so "wed" is Nov 25.
// - "this wed" is the coming one counting today (Nov 18); "next fri" is Friday of next week (Nov 27).
// - "next week" is the next week-start day and "this weekend" the coming Saturday, as the date
//   picker's quick picks; "next month" is the 1st of next month, "next year" Jan 1.
// - A day and month without a year that has already passed this year means next year.
import { addMonths, addYears, endOfMonth, startOfMonth, startOfYear } from 'date-fns'
import type { IsoDate } from '../types'
import { addDaysIso, addMonthsIso, fromIso, isIsoDate, startOfWeekIso, toIso, weekStartsOn } from './dates'

export interface DateMatch {
  /** Where the phrase starts and ends in the text (end exclusive), "due"/"on"/"by" included. */
  start: number
  end: number
  date: IsoDate
}

type Groups = Record<string, string | undefined>
interface Rule {
  body: string
  resolve(g: Groups, today: IsoDate): IsoDate | null
}

const WEEKDAY = 'sunday|sun|monday|mon|tuesday|tues|tue|wednesday|wed|thursday|thurs|thur|thu|friday|fri|saturday|sat'
const WEEKDAY_INDEX: Record<string, number> = { sun: 0, mon: 1, tue: 2, wed: 3, thu: 4, fri: 5, sat: 6 }
const MONTH =
  'january|jan|february|feb|march|mar|april|apr|may|june|jun|july|jul|august|aug|september|sept|sep|october|oct|november|nov|december|dec'
const MONTH_INDEX: Record<string, number> = {
  jan: 0, feb: 1, mar: 2, apr: 3, may: 4, jun: 5, jul: 6, aug: 7, sep: 8, oct: 9, nov: 10, dec: 11,
}
const NUMBER_WORDS: Record<string, number> = {
  a: 1, an: 1, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10, eleven: 11, twelve: 12,
}
const NUMBER = `\\d{1,3}|${Object.keys(NUMBER_WORDS).join('|')}`
const ORDINAL = '(?:st|nd|rd|th)'

function dayOfWeek(d: IsoDate): number {
  return fromIso(d).getDay()
}

/** The date for year, month (0–11) and day, or null when there is no such day (Feb 30). */
function makeDate(y: number, m: number, d: number): IsoDate | null {
  const iso = `${String(y).padStart(4, '0')}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
  return isIsoDate(iso) ? iso : null
}

/** A day and month: in the given year, else this year, or next year once this year's has passed. */
function dayMonth(m: number, d: number, year: string | undefined, today: IsoDate): IsoDate | null {
  if (year) {
    const y = year.length === 2 ? 2000 + Number(year) : Number(year)
    return makeDate(y, m, d)
  }
  const y = Number(today.slice(0, 4))
  const thisYear = makeDate(y, m, d)
  if (thisYear && thisYear >= today) return thisYear
  return makeDate(y + 1, m, d) ?? thisYear
}

/** The next day-of-month `d` on or after today (skipping months that don't have it). */
function nextDayOfMonth(d: number, today: IsoDate): IsoDate | null {
  if (d < 1 || d > 31) return null
  const base = startOfMonth(fromIso(today))
  for (let i = 0; i < 13; i++) {
    const month = addMonths(base, i)
    const date = makeDate(month.getFullYear(), month.getMonth(), d)
    if (date && date >= today) return date
  }
  return null
}

/** "fri": the coming one, never today. "this fri": the coming one, today included. "next fri": in next week. */
function weekday(name: string, mod: string | undefined, today: IsoDate): IsoDate {
  const target = WEEKDAY_INDEX[name.slice(0, 3)]
  const dow = dayOfWeek(today)
  if (mod === 'next') {
    const nextWeek = addDaysIso(startOfWeekIso(today), 7)
    return addDaysIso(nextWeek, (target - weekStartsOn() + 7) % 7)
  }
  const ahead = (target - dow + 7) % 7
  return addDaysIso(today, mod === 'this' ? ahead : ahead || 7)
}

/** The coming Saturday, or today on a weekend (the date picker's "This weekend"). */
function thisWeekend(today: IsoDate): IsoDate {
  const dow = dayOfWeek(today)
  return dow === 6 || dow === 0 ? today : addDaysIso(today, 6 - dow)
}

/** Day-first or month-first numeric dates ("20/11" or "11/20"), from the browser's locale. */
const monthFirstCache = new Map<string, boolean>()
function monthFirst(): boolean {
  const locale = (typeof navigator !== 'undefined' && navigator.language) || 'en-US'
  const cached = monthFirstCache.get(locale)
  if (cached !== undefined) return cached
  let result = true
  try {
    const parts = new Intl.DateTimeFormat(locale, { month: 'numeric', day: 'numeric' }).formatToParts(new Date(2026, 10, 20))
    result = parts.findIndex((p) => p.type === 'month') < parts.findIndex((p) => p.type === 'day')
  } catch {
    result = true
  }
  monthFirstCache.set(locale, result)
  return result
}

const RULES: Rule[] = [
  {
    body: '(?<rel>today|tonight|tomorrow|tmrw|tmr)',
    resolve: (g, today) => addDaysIso(today, g.rel === 'today' || g.rel === 'tonight' ? 0 : 1),
  },
  {
    body: `(?:(?<mod>this|next)\\s+)?(?<wd>${WEEKDAY})`,
    resolve: (g, today) => weekday(g.wd!, g.mod, today),
  },
  {
    body: '(?:this\\s+)?(?<weekend>weekend)|next\\s+(?<next>weekend|week|month|year)',
    resolve: (g, today) => {
      if (g.weekend) return thisWeekend(today)
      if (g.next === 'weekend') {
        const dow = dayOfWeek(today)
        const saturday = dow === 0 ? addDaysIso(today, -1) : addDaysIso(today, 6 - dow)
        return addDaysIso(saturday, 7)
      }
      if (g.next === 'week') return addDaysIso(today, (weekStartsOn() - dayOfWeek(today) + 7) % 7 || 7)
      if (g.next === 'month') return toIso(startOfMonth(addMonths(fromIso(today), 1)))
      return toIso(startOfYear(addYears(fromIso(today), 1)))
    },
  },
  {
    body: `in\\s+(?<n>${NUMBER})\\s+(?<unit>days?|weeks?|months?|years?)`,
    resolve: (g, today) => {
      const n = NUMBER_WORDS[g.n!] ?? Number(g.n)
      if (!Number.isFinite(n)) return null
      const unit = g.unit!.replace(/s$/, '')
      if (unit === 'day') return addDaysIso(today, n)
      if (unit === 'week') return addDaysIso(today, n * 7)
      return addMonthsIso(today, unit === 'month' ? n : n * 12)
    },
  },
  {
    body: 'end\\s+of\\s+(?:the\\s+)?month|eom',
    resolve: (_g, today) => toIso(endOfMonth(fromIso(today))),
  },
  {
    body: `(?<mon>${MONTH})\\.?\\s+(?<d>\\d{1,2})${ORDINAL}?(?:,?\\s+(?<y>\\d{4}))?`,
    resolve: (g, today) => dayMonth(MONTH_INDEX[g.mon!.slice(0, 3)], Number(g.d), g.y, today),
  },
  {
    body: `(?:the\\s+)?(?<d>\\d{1,2})${ORDINAL}?\\s+(?:of\\s+)?(?<mon>${MONTH})\\.?(?:,?\\s+(?<y>\\d{4}))?`,
    resolve: (g, today) => dayMonth(MONTH_INDEX[g.mon!.slice(0, 3)], Number(g.d), g.y, today),
  },
  {
    body: '(?<a>\\d{1,2})\\/(?<b>\\d{1,2})(?:\\/(?<y>\\d{4}|\\d{2}))?',
    resolve: (g, today) => {
      const [m, d] = monthFirst() ? [Number(g.a), Number(g.b)] : [Number(g.b), Number(g.a)]
      if (m < 1 || m > 12) return null
      return dayMonth(m - 1, d, g.y, today)
    },
  },
  {
    body: '(?<iso>\\d{4}-\\d{2}-\\d{2})',
    resolve: (g) => (isIsoDate(g.iso) ? g.iso : null),
  },
  {
    body: `the\\s+(?<d>\\d{1,2})${ORDINAL}`,
    resolve: (g, today) => nextDayOfMonth(Number(g.d), today),
  },
]

/** Only in the date picker's text box, where the whole text is the date. */
const PICKER_RULES: Rule[] = [
  { body: 'yesterday', resolve: (_g, today) => addDaysIso(today, -1) },
  { body: `(?<d>\\d{1,2})${ORDINAL}?`, resolve: (g, today) => nextDayOfMonth(Number(g.d), today) },
]

// A phrase can't touch a letter, digit or one of these on either side ("today's", "#today", "1/2/3").
const BEFORE = "(?<![\\p{L}\\p{N}_'’@#!/\\\\.-])"
const AFTER = "(?![\\p{L}\\p{N}_'’@#/\\\\-])"
const PREFIX = '(?:(?:due|on|by)\\s+)?'

const FIND = RULES.map((r) => ({ r, re: new RegExp(`${BEFORE}${PREFIX}(?:${r.body})${AFTER}`, 'giu') }))
const WHOLE = [...RULES, ...PICKER_RULES].map((r) => ({ r, re: new RegExp(`^\\s*${PREFIX}(?:${r.body})\\s*$`, 'iu') }))

function lowerGroups(g: Groups | undefined): Groups {
  const out: Groups = {}
  for (const [k, v] of Object.entries(g ?? {})) out[k] = v?.toLowerCase()
  return out
}

/**
 * Every date phrase in `text` that doesn't overlap a `blocked` range, in text order. Where phrases
 * overlap, the longer one wins.
 */
export function findTypedDates(text: string, today: IsoDate, blocked: readonly [number, number][] = []): DateMatch[] {
  const found: DateMatch[] = []
  for (const { r, re } of FIND) {
    re.lastIndex = 0
    for (const m of text.matchAll(re)) {
      const start = m.index ?? 0
      const end = start + m[0].length
      if (blocked.some(([a, b]) => start < b && end > a)) continue
      const date = r.resolve(lowerGroups(m.groups), today)
      if (date) found.push({ start, end, date })
    }
  }
  found.sort((a, b) => b.end - b.start - (a.end - a.start) || a.start - b.start)
  const taken: DateMatch[] = []
  for (const m of found) {
    if (!taken.some((t) => m.start < t.end && m.end > t.start)) taken.push(m)
  }
  return taken.sort((a, b) => a.start - b.start)
}

/** The whole text read as one date ("next fri", "nov 20", "20"), or null. */
export function parseTypedDate(text: string, today: IsoDate): IsoDate | null {
  if (!text.trim()) return null
  for (const { r, re } of WHOLE) {
    const m = re.exec(text)
    if (!m) continue
    const date = r.resolve(lowerGroups(m.groups), today)
    if (date) return date
  }
  return null
}

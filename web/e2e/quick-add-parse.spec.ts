// Type-to-fill quick add (Look 1): the title reader (src/lib/quickAdd.ts) and typed dates (src/lib/typedDate.ts),
// checked in Node without a browser. "Today" is fixed at Wednesday, Nov 18 2026, and the locale at en-US (weeks start
// on Sunday, numeric dates read month first).
import { expect, test } from '@playwright/test'
import { parseQuickAdd, removeToken, tokenKey, type QuickContext } from '../src/lib/quickAdd'
import { findTypedDates, parseTypedDate } from '../src/lib/typedDate'

// The libraries read the locale when called, so this applies although the imports above ran first.
try {
  Object.defineProperty(globalThis, 'navigator', { value: { language: 'en-US' }, configurable: true })
} catch {
  // Node's own navigator already says en-US in CI.
}

const TODAY = '2026-11-18'

const ctx = (over: Partial<QuickContext> = {}): QuickContext => ({
  today: TODAY,
  tags: [
    { id: 'tag-home', name: 'home' },
    { id: 'tag-q4', name: 'Q4 plans' },
    { id: 'tag-q', name: 'Q4' },
  ],
  people: [
    { id: 'me', name: 'Jared Doe', email: 'jared@example.com' },
    { id: 'ana-m', name: 'Ana Moreno', email: 'ana@example.com' },
    { id: 'ana-s', name: 'Ana Smith', email: 'asmith@example.com' },
    { id: 'ben', name: 'Ben Ode', email: 'bode@example.com' },
  ],
  meId: 'me',
  canCreateTags: true,
  ...over,
})

const dueOf = (text: string) => parseQuickAdd(text, ctx()).dueDate

test.describe('Quick add: reading a title', () => {
  test('the example from the review: date, priority and tag', () => {
    const p = parseQuickAdd('Pay rent tomorrow !high #home', ctx())
    expect(p).toMatchObject({ title: 'Pay rent', dueDate: '2026-11-19', priority: 'High', tagIds: ['tag-home'], newTags: [] })
    expect(p.tokens.map((t) => [t.kind, t.text])).toEqual([
      ['date', 'tomorrow'],
      ['priority', '!high'],
      ['tag', '#home'],
    ])
    // Positions are in the typed text, for the highlight.
    expect(p.tokens.map((t) => 'Pay rent tomorrow !high #home'.slice(t.start, t.end))).toEqual(['tomorrow', '!high', '#home'])
  })

  test('weekdays, next and this', () => {
    expect(dueOf('Call fri')).toBe('2026-11-20')
    expect(dueOf('Call friday')).toBe('2026-11-20')
    // A bare weekday is never today; "this" counts today.
    expect(dueOf('Call wed')).toBe('2026-11-25')
    expect(dueOf('Call this wed')).toBe('2026-11-18')
    // "next" is that day in next week (Sunday Nov 22 to Saturday Nov 28).
    expect(dueOf('Call next fri')).toBe('2026-11-27')
    expect(dueOf('Call next mon')).toBe('2026-11-23')
    expect(dueOf('Call next sun')).toBe('2026-11-22')
  })

  test('relative words and spans', () => {
    expect(dueOf('Plan today')).toBe('2026-11-18')
    expect(dueOf('Plan tonight')).toBe('2026-11-18')
    expect(dueOf('Plan tmrw')).toBe('2026-11-19')
    expect(dueOf('Plan this weekend')).toBe('2026-11-21')
    expect(dueOf('Plan weekend')).toBe('2026-11-21')
    expect(dueOf('Plan next weekend')).toBe('2026-11-28')
    expect(dueOf('Plan next week')).toBe('2026-11-22')
    expect(dueOf('Plan next month')).toBe('2026-12-01')
    expect(dueOf('Plan next year')).toBe('2027-01-01')
    expect(dueOf('Plan in 3 days')).toBe('2026-11-21')
    expect(dueOf('Plan in 2 weeks')).toBe('2026-12-02')
    expect(dueOf('Plan in a month')).toBe('2026-12-18')
    expect(dueOf('Plan in one year')).toBe('2027-11-18')
    expect(dueOf('Plan end of month')).toBe('2026-11-30')
    expect(dueOf('Plan eom')).toBe('2026-11-30')
  })

  test('calendar dates roll into next year once passed', () => {
    expect(dueOf('Renew nov 20')).toBe('2026-11-20')
    expect(dueOf('Renew Nov 20th')).toBe('2026-11-20')
    expect(dueOf('Renew november 10')).toBe('2027-11-10')
    expect(dueOf('Renew 20 dec')).toBe('2026-12-20')
    expect(dueOf('Renew the 3rd of march')).toBe('2027-03-03')
    expect(dueOf('Renew dec 1, 2027')).toBe('2027-12-01')
    expect(dueOf('Renew 11/25')).toBe('2026-11-25')
    expect(dueOf('Renew 1/2/27')).toBe('2027-01-02')
    expect(dueOf('Renew 2026-12-24')).toBe('2026-12-24')
    expect(dueOf('Renew the 25th')).toBe('2026-11-25')
    expect(dueOf('Renew the 5th')).toBe('2026-12-05')
    expect(dueOf('Renew feb 30')).toBeNull()
    expect(dueOf('Renew 13/40')).toBeNull()
  })

  test('"due", "on" and "by" belong to the date', () => {
    for (const [text, title] of [
      ['Ship by fri', 'Ship'],
      ['Ship due tomorrow', 'Ship'],
      ['Meet on nov 20', 'Meet'],
      ['Ship it, by fri', 'Ship it'],
    ]) {
      const p = parseQuickAdd(text, ctx())
      expect(p.title, text).toBe(title)
      expect(p.dueDate, text).not.toBeNull()
    }
    expect(parseQuickAdd('Ship by fri', ctx()).tokens[0].text).toBe('by fri')
  })

  test('with several dates or priorities the last one counts, the others stay in the title', () => {
    const p = parseQuickAdd('Call fri tomorrow !low !urgent', ctx())
    expect(p).toMatchObject({ title: 'Call fri !low', dueDate: '2026-11-19', priority: 'Urgent' })
  })

  test('priorities', () => {
    const pri = (text: string) => parseQuickAdd(`Fix it ${text}`, ctx()).priority
    expect(pri('!urgent')).toBe('Urgent')
    expect(pri('!1')).toBe('Urgent')
    expect(pri('!U')).toBe('Urgent')
    expect(pri('!HIGH')).toBe('High')
    expect(pri('!2')).toBe('High')
    expect(pri('!medium')).toBe('Normal')
    expect(pri('!n')).toBe('Normal')
    expect(pri('!low')).toBe('Low')
    expect(pri('!4')).toBe('Low')
    expect(pri('!5')).toBeNull()
    expect(pri('!highest')).toBeNull()
    expect(parseQuickAdd('Wow!high', ctx()).priority).toBeNull()
  })

  test('tags: the longest name, case-insensitive; new tags; never numbers', () => {
    expect(parseQuickAdd('Review #q4 plans soon', ctx())).toMatchObject({ title: 'Review soon', tagIds: ['tag-q4'] })
    expect(parseQuickAdd('Review #Q4 soon', ctx())).toMatchObject({ title: 'Review soon', tagIds: ['tag-q'] })
    expect(parseQuickAdd('Buy milk #groceries', ctx())).toMatchObject({ title: 'Buy milk', tagIds: [], newTags: ['groceries'] })
    expect(parseQuickAdd('Buy milk #Home', ctx())).toMatchObject({ tagIds: ['tag-home'], newTags: [] })
    expect(parseQuickAdd('Buy milk #groceries.', ctx()).newTags).toEqual(['groceries'])
    expect(parseQuickAdd('Buy milk #groceries', ctx({ canCreateTags: false })).tokens).toEqual([])
    expect(parseQuickAdd('Fix bug #123', ctx()).tokens).toEqual([])
    expect(parseQuickAdd('Refactor C# code', ctx()).tokens).toEqual([])
    expect(parseQuickAdd('Fix page.html#home link', ctx()).tokens).toEqual([])
    expect(parseQuickAdd(`Tag #${'x'.repeat(51)}`, ctx()).tokens).toEqual([])
  })

  test('people: full name, a unique first name or email name, @me; nobody when two match', () => {
    const who = (text: string) => parseQuickAdd(text, ctx()).assigneeIds
    expect(who('Draft plan @Ana Moreno')).toEqual(['ana-m'])
    expect(who('Draft plan @ben')).toEqual(['ben'])
    expect(who('Draft plan @bode')).toEqual(['ben'])
    expect(who('Draft plan @asmith')).toEqual(['ana-s'])
    expect(who('Draft plan @me')).toEqual(['me'])
    expect(who('Draft plan @ana')).toEqual([])
    expect(who('Draft plan @ana @ben')).toEqual(['ben'])
    expect(who('Email ben@example.com')).toEqual([])
    expect(parseQuickAdd('Draft plan @Ana Moreno', ctx()).title).toBe('Draft plan')
    // Only people who may be assigned in the list count (a private space's people).
    expect(parseQuickAdd('Draft plan @ben', ctx({ people: [{ id: 'me', name: 'Jared Doe', email: 'j@example.com' }] })).tokens).toEqual([])
  })

  test('no false matches inside words', () => {
    for (const text of ["Read today's news", 'Fix the standby mode', 'Buy 2 eggs', 'Review March report', 'Fix may release', 'Version 1.2.3', 'Ship v2/3']) {
      expect(parseQuickAdd(text, ctx()).tokens, text).toEqual([])
    }
  })

  test('nothing counts when the title would be empty', () => {
    expect(parseQuickAdd('Tomorrow', ctx())).toMatchObject({ title: 'Tomorrow', tokens: [], dueDate: null })
    expect(parseQuickAdd('  tomorrow !high  ', ctx())).toMatchObject({ title: 'tomorrow !high', tokens: [], priority: null })
  })

  test('a dismissed part stays in the title, and stays dismissed as typing goes on', () => {
    const ignored = new Set([tokenKey('date', 'tomorrow')])
    expect(parseQuickAdd('Pay rent tomorrow', ctx({ ignored }))).toMatchObject({ title: 'Pay rent tomorrow', dueDate: null, tokens: [] })
    expect(parseQuickAdd('Pay rent tomorrow !high', ctx({ ignored }))).toMatchObject({ title: 'Pay rent tomorrow', priority: 'High' })
    // A dismissed date lets an earlier one count.
    expect(parseQuickAdd('Pay rent fri tomorrow', ctx({ ignored }))).toMatchObject({ title: 'Pay rent tomorrow', dueDate: '2026-11-20' })
  })

  test('removeToken takes one part out and closes the gap', () => {
    const text = 'Pay rent tomorrow !high'
    const p = parseQuickAdd(text, ctx())
    expect(removeToken(text, p.tokens[0])).toBe('Pay rent !high')
    expect(removeToken(text, p.tokens[1])).toBe('Pay rent tomorrow')
  })
})

test.describe('Typed dates', () => {
  test('the date picker box reads the whole text', () => {
    expect(parseTypedDate('next fri', TODAY)).toBe('2026-11-27')
    expect(parseTypedDate('  Tomorrow ', TODAY)).toBe('2026-11-19')
    expect(parseTypedDate('due fri', TODAY)).toBe('2026-11-20')
    expect(parseTypedDate('yesterday', TODAY)).toBe('2026-11-17')
    expect(parseTypedDate('20', TODAY)).toBe('2026-11-20')
    expect(parseTypedDate('5th', TODAY)).toBe('2026-12-05')
    expect(parseTypedDate('31', '2026-11-18')).toBe('2026-12-31')
    expect(parseTypedDate('in 2 weeks', TODAY)).toBe('2026-12-02')
    expect(parseTypedDate('fri soon', TODAY)).toBeNull()
    expect(parseTypedDate('banana', TODAY)).toBeNull()
    expect(parseTypedDate('', TODAY)).toBeNull()
  })

  test('yesterday and bare numbers are never read inside a title', () => {
    expect(findTypedDates('Notes from yesterday', TODAY)).toEqual([])
    expect(findTypedDates('Buy 20 stamps', TODAY)).toEqual([])
  })

  test('day-first locales read 20/11 as November 20', () => {
    Object.defineProperty(globalThis, 'navigator', { value: { language: 'en-GB' }, configurable: true })
    try {
      expect(parseTypedDate('20/11', TODAY)).toBe('2026-11-20')
      expect(parseTypedDate('11/20', TODAY)).toBeNull()
    } finally {
      Object.defineProperty(globalThis, 'navigator', { value: { language: 'en-US' }, configurable: true })
    }
  })
})

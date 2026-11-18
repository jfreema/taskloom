// Type-to-fill quick add (Look 1): reads a typed task title such as "Pay rent tomorrow !high #home @ana"
// into the title ("Pay rent") and fields:
// - a date phrase (lib/typedDate) sets the due date; with several, the last one counts;
// - "!urgent", "!high", "!normal" (or "!medium"), "!low", their first letters or "!1"–"!4" set the
//   priority; with several, the last one counts;
// - "#name" adds a workspace tag, matching the longest tag name (so "#q4 plans" finds "Q4 plans");
//   an unknown "#name" is a new tag when the person may create tags. "#123" is never a tag;
// - "@name" assigns someone who may be assigned in the list: by full name, a first name or an email
//   name that only one person has, or "@me".
// Each recognised part is a token the person can dismiss: its text then stays in the title (`ignored`,
// keyed by kind and text, so typing on keeps it dismissed). Nothing is recognised when the title would
// be left empty, so a task can still be called "Tomorrow".
import type { IsoDate, Priority, Uuid } from '../types'
import { findTypedDates } from './typedDate'

export type QuickTokenKind = 'date' | 'priority' | 'tag' | 'assignee'

export interface QuickToken {
  kind: QuickTokenKind
  /** The token's place in the typed text (end exclusive). */
  start: number
  end: number
  /** The typed text, e.g. "by fri" or "#home". */
  text: string
  /** What a dismissal is remembered by. */
  key: string
  date?: IsoDate
  priority?: Priority
  /** An existing tag. */
  tagId?: Uuid
  /** A tag that doesn't exist yet, created when the task is. */
  newTag?: string
  userId?: Uuid
}

export interface QuickPerson {
  id: Uuid
  name: string
  email: string
}

export interface QuickContext {
  today: IsoDate
  tags: readonly { id: Uuid; name: string }[]
  /** The people who may be assigned in the target list. */
  people: readonly QuickPerson[]
  meId: Uuid | null
  canCreateTags: boolean
  ignored?: ReadonlySet<string>
}

export interface QuickParse {
  /** The title without the tokens; the whole text, trimmed, when there are none. */
  title: string
  /** In text order. */
  tokens: QuickToken[]
  dueDate: IsoDate | null
  priority: Priority | null
  tagIds: Uuid[]
  /** Names of tags to create, as typed. */
  newTags: string[]
  assigneeIds: Uuid[]
}

export const TAG_NAME_MAX = 50

const PRIORITY_WORDS: Record<string, Priority> = {
  urgent: 'Urgent', u: 'Urgent', '1': 'Urgent',
  high: 'High', h: 'High', '2': 'High',
  normal: 'Normal', medium: 'Normal', med: 'Normal', n: 'Normal', '3': 'Normal',
  low: 'Low', l: 'Low', '4': 'Low',
}
const PRIORITY_RE = /(?<![\p{L}\p{N}_!])!(urgent|high|normal|medium|med|low|[1-4]|u|h|n|l)(?![\p{L}\p{N}_'’-])/giu
/** A sigil ("#", "@") starts a token only at the start or after a space or an opening bracket. */
const SIGIL_BEFORE = /[\s([{"“]/u
/** A name ends at the end of the text, a space or punctuation, but not inside a word ("@ana's", "#home-office"). */
const NAME_AFTER = /^(?:$|[^\p{L}\p{N}_'’-])/u
const NEW_TAG_RE = /^[\p{L}\p{N}_][\p{L}\p{N}_\-/.&+]*/u

export function tokenKey(kind: QuickTokenKind, text: string): string {
  return `${kind}:${text.toLocaleLowerCase().replace(/\s+/g, ' ')}`
}

function fold(s: string): string {
  return s.trim().toLocaleLowerCase()
}

/** True when `text` at `at` starts with `name` (ignoring case) and the name ends there. */
function nameAt(text: string, at: number, name: string): boolean {
  if (!name) return false
  const slice = text.slice(at, at + name.length)
  return slice.toLocaleLowerCase() === name.toLocaleLowerCase() && NAME_AFTER.test(text.slice(at + name.length))
}

function sigilStarts(text: string, sigil: string): number[] {
  const out: number[] = []
  for (let i = text.indexOf(sigil); i !== -1; i = text.indexOf(sigil, i + 1)) {
    if (i === 0 || SIGIL_BEFORE.test(text[i - 1])) out.push(i)
  }
  return out
}

function tagTokens(text: string, ctx: QuickContext): QuickToken[] {
  const byLength = ctx.tags.filter((t) => t.name.trim()).sort((a, b) => b.name.trim().length - a.name.trim().length)
  const out: QuickToken[] = []
  for (const at of sigilStarts(text, '#')) {
    const tag = byLength.find((t) => nameAt(text, at + 1, t.name.trim()))
    if (tag) {
      const end = at + 1 + tag.name.trim().length
      out.push({ kind: 'tag', start: at, end, text: text.slice(at, end), key: '', tagId: tag.id })
      continue
    }
    if (!ctx.canCreateTags) continue
    const m = NEW_TAG_RE.exec(text.slice(at + 1))
    if (!m) continue
    const name = m[0].replace(/[\-/.&+]+$/u, '')
    if (!name || /^\d+$/u.test(name) || name.length > TAG_NAME_MAX || !NAME_AFTER.test(text.slice(at + 1 + name.length))) continue
    const end = at + 1 + name.length
    const existing = ctx.tags.find((t) => fold(t.name) === fold(name))
    out.push({
      kind: 'tag',
      start: at,
      end,
      text: text.slice(at, end),
      key: '',
      ...(existing ? { tagId: existing.id } : { newTag: name }),
    })
  }
  return out
}

/** The people, ordered so longer names are tried first. */
function personAt(text: string, at: number, ctx: QuickContext): { id: Uuid; length: number } | null {
  const people = ctx.people
  if (ctx.meId && people.some((p) => p.id === ctx.meId) && nameAt(text, at, 'me')) return { id: ctx.meId, length: 2 }
  const full = [...people].filter((p) => p.name.trim()).sort((a, b) => b.name.trim().length - a.name.trim().length)
  const byFull = full.find((p) => nameAt(text, at, p.name.trim()))
  if (byFull) {
    // Two people with the same full name: nobody is guessed.
    const n = fold(byFull.name)
    return people.filter((p) => fold(p.name) === n).length === 1 ? { id: byFull.id, length: byFull.name.trim().length } : null
  }
  const firsts = new Map<string, Uuid[]>()
  const locals = new Map<string, Uuid[]>()
  for (const p of people) {
    const first = fold(p.name).split(/\s+/u)[0]
    if (first) firsts.set(first, [...(firsts.get(first) ?? []), p.id])
    const local = fold(p.email).split('@')[0]
    if (local) locals.set(local, [...(locals.get(local) ?? []), p.id])
  }
  for (const map of [firsts, locals]) {
    const names = [...map.keys()].sort((a, b) => b.length - a.length)
    const name = names.find((x) => nameAt(text, at, x))
    if (name) {
      const ids = map.get(name)!
      if (ids.length === 1) return { id: ids[0], length: name.length }
      return null
    }
  }
  return null
}

function assigneeTokens(text: string, ctx: QuickContext): QuickToken[] {
  const out: QuickToken[] = []
  for (const at of sigilStarts(text, '@')) {
    const p = personAt(text, at + 1, ctx)
    if (!p) continue
    const end = at + 1 + p.length
    out.push({ kind: 'assignee', start: at, end, text: text.slice(at, end), key: '', userId: p.id })
  }
  return out
}

function priorityTokens(text: string): QuickToken[] {
  const out: QuickToken[] = []
  PRIORITY_RE.lastIndex = 0
  for (const m of text.matchAll(PRIORITY_RE)) {
    const start = m.index ?? 0
    const end = start + m[0].length
    out.push({ kind: 'priority', start, end, text: m[0], key: '', priority: PRIORITY_WORDS[m[1].toLowerCase()] })
  }
  return out
}

/** The text with the ranges taken out, spaces collapsed and stray separators trimmed. */
export function withoutRanges(text: string, ranges: readonly { start: number; end: number }[]): string {
  let out = ''
  let at = 0
  for (const r of [...ranges].sort((a, b) => a.start - b.start)) {
    out += `${text.slice(at, r.start)} `
    at = Math.max(at, r.end)
  }
  out += text.slice(at)
  return out
    .replace(/\s+/gu, ' ')
    .replace(/\s+([,.;:!?])(?=\s|$)/gu, '$1')
    .replace(/^[\s,;:–—-]+|[\s,;:–—-]+$/gu, '')
}

/**
 * The text with one token taken out, keeping the rest as typed (for a field changed by hand while
 * a typed value set it): one space closes the gap.
 */
export function removeToken(text: string, token: Pick<QuickToken, 'start' | 'end'>): string {
  const before = text.slice(0, token.start).replace(/\s+$/u, '')
  const after = text.slice(token.end).replace(/^\s+/u, '')
  return before && after ? `${before} ${after}` : before + after
}

export function parseQuickAdd(text: string, ctx: QuickContext): QuickParse {
  const ignored = ctx.ignored ?? new Set<string>()
  const keep = (t: QuickToken): QuickToken | null => {
    const key = tokenKey(t.kind, t.text)
    return ignored.has(key) ? null : { ...t, key }
  }
  const lastOf = (list: QuickToken[]): QuickToken[] => {
    const kept = list.map(keep).filter((t): t is QuickToken => !!t)
    return kept.length ? [kept[kept.length - 1]] : []
  }

  const tokens: QuickToken[] = []
  const overlaps = (a: { start: number; end: number }) => tokens.some((t) => a.start < t.end && a.end > t.start)
  const add = (list: QuickToken[]) => {
    for (const t of list) if (!overlaps(t)) tokens.push(t)
  }
  add(tagTokens(text, ctx).map(keep).filter((t): t is QuickToken => !!t))
  add(assigneeTokens(text, ctx).map(keep).filter((t): t is QuickToken => !!t))
  add(lastOf(priorityTokens(text).filter((t) => !overlaps(t))))
  const blocked = tokens.map((t) => [t.start, t.end] as [number, number])
  const dates = findTypedDates(text, ctx.today, blocked).map(
    (m): QuickToken => ({ kind: 'date', start: m.start, end: m.end, text: text.slice(m.start, m.end), key: '', date: m.date }),
  )
  add(lastOf(dates))
  tokens.sort((a, b) => a.start - b.start)

  const title = withoutRanges(text, tokens)
  if (!tokens.length || !title) {
    return { title: text.trim(), tokens: [], dueDate: null, priority: null, tagIds: [], newTags: [], assigneeIds: [] }
  }
  const unique = <T>(xs: T[]) => [...new Set(xs)]
  const newTags: string[] = []
  for (const t of tokens) {
    if (t.newTag && !newTags.some((n) => fold(n) === fold(t.newTag!))) newTags.push(t.newTag)
  }
  return {
    title,
    tokens,
    dueDate: tokens.find((t) => t.kind === 'date')?.date ?? null,
    priority: tokens.find((t) => t.kind === 'priority')?.priority ?? null,
    tagIds: unique(tokens.flatMap((t) => (t.tagId ? [t.tagId] : []))),
    newTags,
    assigneeIds: unique(tokens.flatMap((t) => (t.userId ? [t.userId] : []))),
  }
}

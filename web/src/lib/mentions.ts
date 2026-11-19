// @mentions in comments (Features 2). On the wire a comment's body is plain text in which a mention
// is the token `<@user id>`. People never see the token: the composer shows "@Name" and remembers
// who each name stands for (the picks), and a comment draws the token as a chip.
import type { Mention, Uuid } from '../types'

const TOKEN = /<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>/g

/** The people picked from the @ list while writing, by the text that stands for them ("@Ana Lee"). */
export type MentionPicks = Record<string, Uuid>

export function mentionToken(userId: Uuid): string {
  return `<@${userId.toLowerCase()}>`
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** A letter, digit or underscore: a name must not run straight on into one ("@Ana" inside "@Anabel"). */
const WORD = /[\p{L}\p{N}_]/u

/**
 * The text as the server stores it: each picked "@Name" still in the text becomes that person's
 * token (longer names first, so "@Ana Lee" wins over "@Ana"), and `mentions` lists them in order.
 */
export function encodeMentions(text: string, picks: MentionPicks): { body: string; mentions: Mention[] } {
  const names = Object.keys(picks).filter((n) => n.length > 1 && text.includes(n)).sort((a, b) => b.length - a.length)
  if (!names.length) return { body: text, mentions: [] }
  const pattern = new RegExp(names.map(escapeRegExp).join('|'), 'g')
  const mentions: Mention[] = []
  const body = text.replace(pattern, (name: string, at: number) => {
    const before = at > 0 ? text[at - 1] : ''
    const after = text[at + name.length] ?? ''
    if ((before && (WORD.test(before) || before === '@')) || (after && WORD.test(after))) return name
    const userId = picks[name]
    if (!mentions.some((m) => m.userId === userId)) mentions.push({ userId, displayName: name.slice(1) })
    return mentionToken(userId)
  })
  return { body, mentions }
}

/** The text to edit: each token as "@Name", with the picks that turn them back into tokens. */
export function decodeMentions(body: string, nameOf: (userId: Uuid) => string): { text: string; picks: MentionPicks } {
  const picks: MentionPicks = {}
  const text = body.replace(TOKEN, (_token: string, id: string) => {
    const userId = id.toLowerCase()
    const name = `@${nameOf(userId)}`
    // Two people with one name: the first keeps the plain name, so every token survives the round trip.
    if (picks[name] && picks[name] !== userId) return mentionToken(userId)
    picks[name] = userId
    return name
  })
  return { text, picks }
}

export interface MentionSegment {
  text: string
  /** Set for a mention: the person's id. `text` is then "@Name". */
  userId?: Uuid
}

/** The body cut into plain text and mentions, for drawing. */
export function mentionSegments(body: string, nameOf: (userId: Uuid) => string): MentionSegment[] {
  const out: MentionSegment[] = []
  let last = 0
  for (const match of body.matchAll(TOKEN)) {
    const start = match.index ?? 0
    if (start > last) out.push({ text: body.slice(last, start) })
    const userId = match[1].toLowerCase()
    out.push({ text: `@${nameOf(userId)}`, userId })
    last = start + match[0].length
  }
  if (last < body.length) out.push({ text: body.slice(last) })
  return out
}

/** The people a body mentions, each once, in order (a comment being sent has no server list yet). */
export function mentionsIn(body: string, nameOf: (userId: Uuid) => string): Mention[] {
  const out: Mention[] = []
  for (const match of body.matchAll(TOKEN)) {
    const userId = match[1].toLowerCase()
    if (!out.some((m) => m.userId === userId)) out.push({ userId, displayName: nameOf(userId) })
  }
  return out
}

/** The body as plain text, mentions as "@Name" (an Inbox preview, a tooltip). */
export function mentionsAsText(body: string, nameOf: (userId: Uuid) => string): string {
  return body.replace(TOKEN, (_token: string, id: string) => `@${nameOf(id.toLowerCase())}`)
}

export interface MentionQuery {
  /** Where the "@" is in the text. */
  start: number
  /** What has been typed after it, up to the caret. */
  query: string
}

const MAX_QUERY = 40

/**
 * The mention being typed at the caret, if any: an "@" at the start of the text or after a space or
 * an opening bracket, then up to 40 characters on the same line.
 */
export function mentionQueryAt(text: string, caret: number): MentionQuery | null {
  const from = Math.max(0, caret - MAX_QUERY - 1)
  for (let i = caret - 1; i >= from; i--) {
    const c = text[i]
    if (c === '\n') return null
    if (c !== '@') continue
    const before = i > 0 ? text[i - 1] : ''
    if (before && !/[\s(\[{"'“‘,;:>]/.test(before)) return null
    return { start: i, query: text.slice(i + 1, caret) }
  }
  return null
}

export interface MentionCandidate {
  userId: Uuid
  displayName: string
  email: string
}

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function fold(s: string): string {
  return s.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase()
}

/**
 * The people matching what was typed after "@": names whose start or any word's start matches rank
 * first, then names containing it, then email addresses that start with it. Up to `limit`.
 */
export function matchMentions<T extends MentionCandidate>(people: readonly T[], query: string, limit = 8): T[] {
  const q = fold(query.trim())
  const scored: { person: T; score: number }[] = []
  for (const person of people) {
    const name = fold(person.displayName)
    let score = -1
    if (!q) score = 0
    else if (name.startsWith(q)) score = 0
    else if (name.split(/\s+/).some((w) => w.startsWith(q))) score = 1
    else if (name.includes(q)) score = 2
    else if (fold(person.email).startsWith(q)) score = 3
    if (score >= 0) scored.push({ person, score })
  }
  scored.sort((a, b) => a.score - b.score || collator.compare(a.person.displayName, b.person.displayName))
  return scored.slice(0, limit).map((s) => s.person)
}

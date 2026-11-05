// The palette's text matching (8.11): case- and accent-insensitive substring search, and the
// segments that set the matched text in weight 600 (rendered as spans, never as HTML). Folded texts
// are kept by text, so the palette's local index (list, space and action names) is folded once,
// ahead of the first keystroke (`prepare`, when the palette opens), not on every keystroke.

export interface MatchSegment {
  text: string
  match: boolean
}

/** A folded text plus, for each folded character, the index of the original character it came from. */
interface FoldMap {
  folded: string
  from: number[]
}

/** Entries kept per cache: the palette folds a few hundred texts, so a full cache simply starts again. */
const CACHE_MAX = 2000
const folds = new Map<string, string>()
const maps = new Map<string, FoldMap>()

function cached<T>(cache: Map<string, T>, key: string, make: (s: string) => T): T {
  let value = cache.get(key)
  if (value === undefined) {
    if (cache.size >= CACHE_MAX) cache.clear()
    value = make(key)
    cache.set(key, value)
  }
  return value
}

/** Lower case, with accents removed: "Équipe" → "equipe". */
export function fold(s: string): string {
  return s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
}

function foldWithMap(s: string): FoldMap {
  let folded = ''
  const from: number[] = []
  let i = 0
  for (const ch of s) {
    const f = fold(ch)
    for (let k = 0; k < f.length; k++) from.push(i)
    folded += f
    i += ch.length
  }
  from.push(s.length)
  return { folded, from }
}

function folded(s: string): string {
  return cached(folds, s, fold)
}

/**
 * Folds `texts` for matching and highlighting ahead of the first keystroke. The first fold also
 * loads the Unicode data that normalizing and the mark pattern need.
 */
export function prepare(texts: Iterable<string>): void {
  for (const t of texts) {
    folded(t)
    cached(maps, t, foldWithMap)
  }
}

/** Whether `text` contains `query` (both folded). An empty query matches everything. */
export function matches(text: string, query: string): boolean {
  const q = folded(query.trim())
  return !q || folded(text).includes(q)
}

/** Whether a word of `words` starts with `query` (folded): action keywords, so "mo" finds "mode" but "an" doesn't find "appearance". */
export function startsWord(words: string, query: string): boolean {
  const q = folded(query.trim())
  return !!q && folded(words).split(/\s+/).some((w) => w.startsWith(q))
}

/** Splits `text` into plain and matched parts: the first occurrence of `query`, case- and accent-insensitive. */
export function highlight(text: string, query: string): MatchSegment[] {
  const q = folded(query.trim())
  if (!q) return [{ text, match: false }]
  const { folded: f, from } = cached(maps, text, foldWithMap)
  const at = f.indexOf(q)
  if (at === -1) return [{ text, match: false }]
  const start = from[at]
  // The end is the start of the original character after the last matched folded one.
  const lastFrom = from[at + q.length - 1]
  let end = text.length
  for (let k = at + q.length; k < from.length; k++) {
    if (from[k] > lastFrom) {
      end = from[k]
      break
    }
  }
  const out: MatchSegment[] = []
  if (start > 0) out.push({ text: text.slice(0, start), match: false })
  out.push({ text: text.slice(start, end), match: true })
  if (end < text.length) out.push({ text: text.slice(end), match: false })
  return out
}

// The palette's text matching (8.11): case- and accent-insensitive substring search, and the
// segments that set the matched text in weight 600 (rendered as spans, never as HTML).

export interface MatchSegment {
  text: string
  match: boolean
}

/** Lower case, with accents removed: "Équipe" → "equipe". */
export function fold(s: string): string {
  return s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
}

/** The folded string plus, for each folded character, the index of the original character it came from. */
function foldWithMap(s: string): { folded: string; from: number[] } {
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

/** Whether `text` contains `query` (both folded). An empty query matches everything. */
export function matches(text: string, query: string): boolean {
  const q = fold(query.trim())
  return !q || fold(text).includes(q)
}

/** Whether a word of `words` starts with `query` (folded): action keywords, so "mo" finds "mode" but "an" doesn't find "appearance". */
export function startsWord(words: string, query: string): boolean {
  const q = fold(query.trim())
  return !!q && fold(words).split(/\s+/).some((w) => w.startsWith(q))
}

/** Splits `text` into plain and matched parts: the first occurrence of `query`, case- and accent-insensitive. */
export function highlight(text: string, query: string): MatchSegment[] {
  const q = fold(query.trim())
  if (!q) return [{ text, match: false }]
  const { folded, from } = foldWithMap(text)
  const at = folded.indexOf(q)
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

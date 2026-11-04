// "Paste to create many" (9.3): turn pasted text into task titles.

/** Most titles one paste may create; more shows "You can paste up to 100 tasks at a time." */
export const MAX_PASTED_TASKS = 100

// One bullet ("- ", "* ", "• ") or number ("1. ", "1) "), then optionally a checkbox ("[ ] ", "[x] ").
// A marker alone on its line (an empty bullet) leaves nothing, so the line is dropped.
const BULLET = /^(?:[-*•]|\d+[.)])(?:[ \t]+|$)/
const CHECKBOX = /^\[[ xX]\](?:[ \t]+|$)/

/**
 * Splits on line breaks, trims each line, strips one list marker and one checkbox marker,
 * and drops empty lines. "- [ ] Buy milk" becomes "Buy milk".
 */
export function splitPastedLines(text: string): string[] {
  const out: string[] = []
  for (const raw of text.split(/\r\n|\r|\n/)) {
    let line = raw.trim()
    line = line.replace(BULLET, '')
    line = line.replace(CHECKBOX, '').trim()
    if (line) out.push(line)
  }
  return out
}

/**
 * The confirm message for a paste (9.3): the first 3 titles, then "and N more", e.g.
 * “Buy milk”, “Call Ana”, “Book flights” and 2 more. Long titles are shortened to 60 characters.
 */
export function pasteSummary(lines: string[]): string {
  const quote = (s: string) => `“${s.length > 60 ? `${s.slice(0, 59).trimEnd()}…` : s}”`
  const shown = lines.slice(0, 3).map(quote)
  const rest = lines.length - shown.length
  if (rest > 0) return `${shown.join(', ')} and ${rest} more`
  return shown.length > 1 ? `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}` : (shown[0] ?? '')
}

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

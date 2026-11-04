// Small HTML helpers. Description HTML is rendered only through Tiptap and comments are plain text
// rendered with `linkify`, so nothing here ever feeds `v-html`.

const ESCAPES: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }

export function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ESCAPES[c])
}

/**
 * Plain text as escaped paragraphs, one `<p>` per line (the quick-create description, 8.12).
 * Returns "" for blank text, so an empty description stays empty.
 */
export function plainTextToHtml(s: string): string {
  const text = s.replace(/\r\n?/g, '\n').replace(/^\n+|\n+$/g, '')
  if (!text.trim()) return ''
  return text
    .split('\n')
    .map((line) => `<p>${escapeHtml(line)}</p>`)
    .join('')
}

export interface LinkifySegment {
  text: string
  /** Set on links: always an http, https or mailto URL. */
  href?: string
}

const URL_PATTERN = /\b(?:https?:\/\/|mailto:)[^\s<>"']+/gi
const TRAILING_PUNCTUATION = '.,;:!?'
const BRACKETS: Record<string, string> = { ')': '(', ']': '[', '}': '{' }

function count(s: string, ch: string): number {
  let n = 0
  for (const c of s) if (c === ch) n++
  return n
}

/** Drops sentence punctuation and unbalanced closing brackets from the end of a matched URL. */
function trimUrl(url: string): string {
  let u = url
  for (;;) {
    const last = u[u.length - 1]
    if (last === undefined) return u
    if (TRAILING_PUNCTUATION.includes(last)) {
      u = u.slice(0, -1)
      continue
    }
    const open = BRACKETS[last]
    if (open && count(u, last) > count(u, open)) {
      u = u.slice(0, -1)
      continue
    }
    return u
  }
}

function isLinkable(url: string): boolean {
  const lower = url.toLowerCase()
  if (lower.startsWith('mailto:')) return url.length > 'mailto:'.length && url.includes('@')
  const scheme = lower.startsWith('https://') ? 'https://' : 'http://'
  return url.length > scheme.length
}

/** Splits plain text into text and link segments. Only http, https and mailto URLs become links. */
export function linkify(s: string): LinkifySegment[] {
  const out: LinkifySegment[] = []
  let last = 0
  for (const match of s.matchAll(URL_PATTERN)) {
    const start = match.index ?? 0
    const url = trimUrl(match[0])
    if (!isLinkable(url)) continue
    if (start > last) out.push({ text: s.slice(last, start) })
    out.push({ text: url, href: url })
    last = start + url.length
  }
  if (last < s.length) out.push({ text: s.slice(last) })
  return out
}

const ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ', '#39': "'" }

/** True when the HTML has no visible text (`""`, `<p></p>`, `<p><br></p>`, an empty checklist). */
export function isEmptyHtml(html: string | null | undefined): boolean {
  if (!html) return true
  const text = html
    .replace(/<[^>]*>/g, ' ')
    .replace(/&(#\d+|#x[0-9a-f]+|[a-z]+|#39);/gi, (m, name: string) => {
      const key = name.toLowerCase()
      if (key in ENTITIES) return ENTITIES[key]
      if (key.startsWith('#x')) return String.fromCodePoint(parseInt(key.slice(2), 16) || 32)
      if (key.startsWith('#')) return String.fromCodePoint(parseInt(key.slice(1), 10) || 32)
      return m
    })
    .replace(/[\s ​]+/g, '')
  return text.length === 0
}

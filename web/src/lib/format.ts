// Text formatting helpers shared by many components.

/** "Jared" · "Jared and Ana" · "Jared, Ana and Sam" · with max 2: "Jared, Ana and 2 others". */
export function formatNames(names: string[], max = 2): string {
  const list = names.filter((n) => n && n.trim())
  if (list.length === 0) return ''
  if (list.length === 1) return list[0]
  const limit = Math.max(1, max)
  if (list.length <= limit) return `${list.slice(0, -1).join(', ')} and ${list[list.length - 1]}`
  const rest = list.length - limit
  return `${list.slice(0, limit).join(', ')} and ${rest} ${rest === 1 ? 'other' : 'others'}`
}

/** "1 task", "3 tasks"; `many` overrides the plural ("1 person", "2 people"). */
export function pluralize(n: number, one: string, many?: string): string {
  return `${n} ${n === 1 ? one : (many ?? `${one}s`)}`
}

/** First letters of the first and last words, upper-cased: "Jared Doe" → "JD", "jsmith" → "J". */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '?'
  const first = Array.from(words[0])[0] ?? ''
  const last = words.length > 1 ? (Array.from(words[words.length - 1])[0] ?? '') : ''
  return (first + last).toUpperCase()
}

// Deliberately simple: one @, no spaces, a dot in the domain. The server has the final word.
const EMAIL = /^[^\s@<>()[\]\\,;:"]+@[^\s@<>()[\]\\,;:"]+\.[^\s@<>()[\]\\,;:"]+$/

export function isValidEmail(s: string): boolean {
  return EMAIL.test(s.trim())
}

/**
 * Parses the invite dialog's text (8.13): addresses split on commas, semicolons, new lines and
 * whitespace outside "Name <email>" forms. Duplicates (ignoring case) are dropped; the first wins.
 */
export function parseInviteList(text: string): { valid: { email: string; displayName?: string }[]; invalid: string[] } {
  const valid: { email: string; displayName?: string }[] = []
  const invalid: string[] = []
  const seen = new Set<string>()

  const add = (emailRaw: string, displayName?: string) => {
    const email = emailRaw.trim()
    if (!isValidEmail(email)) {
      invalid.push(displayName ? `${displayName} <${email}>` : email)
      return
    }
    const key = email.toLowerCase()
    if (seen.has(key)) return
    seen.add(key)
    valid.push(displayName ? { email, displayName } : { email })
  }

  for (const chunk of text.split(/[,;\r\n]+/)) {
    // Tokens: a quoted name, an <address>, or a run of non-space characters. Words before an
    // <address> are its name; words that are neither a name nor an address are reported as invalid.
    const tokens = chunk.match(/"[^"]*"|<[^<>]*>|[^\s<>"]+/g) ?? []
    let nameParts: string[] = []
    const flushWords = () => {
      if (nameParts.length) invalid.push(nameParts.join(' '))
      nameParts = []
    }
    for (const token of tokens) {
      if (token.startsWith('<')) {
        add(token.slice(1, -1), nameParts.join(' ').trim() || undefined)
        nameParts = []
      } else if (token.startsWith('"')) {
        nameParts.push(token.slice(1, -1).trim())
      } else if (token.includes('@')) {
        flushWords()
        add(token)
      } else {
        nameParts.push(token)
      }
    }
    flushWords()
  }
  return { valid, invalid }
}

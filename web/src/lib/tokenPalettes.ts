// Reads the colours a style and theme would give, without switching to them (Settings › Appearance draws its
// previews with these). The token rules of styles/tokens.css and styles/pelican.css are found in the loaded
// stylesheets and their custom properties merged in cascade order, as on <html>: Loom's :root, then its
// [data-theme="dark"] rule, then Pelican's [data-look="pelican"] rule and its dark one. var() references are
// resolved within that merge, so a token such as --bg-page: var(--bg-canvas) comes back as a colour.
// The @media copies of the dark rules are skipped: they repeat the explicit ones.
import type { Look } from '../composables/useLook'

export type Mode = 'light' | 'dark'

/** Selectors as the CSSOM gives them, without spaces or quotes: [data-theme="dark"] → [data-theme=dark]. */
const normalise = (selector: string): string => selector.replace(/\s+/g, '').replace(/["']/g, '')

function rootSelectors(look: Look, mode: Mode): string[] {
  const out = [':root']
  if (mode === 'dark') out.push(':root[data-theme=dark]')
  if (look === 'pelican') {
    out.push(':root[data-look=pelican]')
    if (mode === 'dark') out.push(':root[data-look=pelican][data-theme=dark]')
  }
  return out
}

/** Every plain style rule, in source order (rules inside @media are left out; @layer and @supports are walked). */
function styleRules(): CSSStyleRule[] {
  const out: CSSStyleRule[] = []
  const visit = (rules: CSSRuleList): void => {
    for (const rule of Array.from(rules)) {
      if (rule instanceof CSSStyleRule) out.push(rule)
      else if (rule instanceof CSSGroupingRule && !(rule instanceof CSSMediaRule)) visit(rule.cssRules)
    }
  }
  for (const sheet of Array.from(document.styleSheets)) {
    try {
      visit(sheet.cssRules)
    } catch {
      // A cross-origin sheet can't be read; the tokens are never in one.
    }
  }
  return out
}

type Declarations = Map<string, string>

function declarationsFor(rules: CSSStyleRule[], selectors: string[]): Declarations {
  const out: Declarations = new Map()
  for (const wanted of selectors) {
    for (const rule of rules) {
      if (!rule.selectorText.split(',').some((s) => normalise(s) === wanted)) continue
      for (const name of Array.from(rule.style)) {
        if (name.startsWith('--')) out.set(name, rule.style.getPropertyValue(name).trim())
      }
    }
  }
  return out
}

const VAR = /var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*(?:\([^()]*\)[^()]*)*))?\)/g

function resolve(value: string, lookup: (name: string) => string | undefined, depth = 0): string {
  if (depth > 12 || !value.includes('var(')) return value
  return value.replace(VAR, (_, name: string, fallback?: string) => {
    const v = lookup(name)
    return resolve(v ?? fallback?.trim() ?? '', lookup, depth + 1)
  })
}

/**
 * The tokens of a style and theme, resolved to plain values. `region` names one of pelican.css's regions
 * (".app-sidebar"): its own declarations then win, as they would on that element.
 */
export function readTokens(look: Look, mode: Mode, names: readonly string[], region?: string): Record<string, string> {
  const rules = styleRules()
  const root = declarationsFor(rules, rootSelectors(look, mode))
  const rootValue = (name: string): string | undefined => {
    const v = root.get(name)
    return v === undefined ? undefined : resolve(v, rootValue)
  }
  const local = region ? declarationsFor(rules, rootSelectors(look, mode).map((s) => normalise(`${s} ${region}`))) : new Map<string, string>()
  const value = (name: string): string | undefined => {
    const v = local.get(name)
    return v === undefined ? rootValue(name) : resolve(v, value)
  }
  const out: Record<string, string> = {}
  for (const name of names) out[name] = value(name) ?? ''
  return out
}

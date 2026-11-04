// Forced hover and focus for the /_ui state matrix (development only), so every primitive's
// hovered and focused looks can be seen side by side without touching the primitives.
// - Each style rule that uses :hover, :focus, :focus-visible or :focus-within gets a copy in which
//   the pseudo-class means "is, or contains, the element marked data-force-hover" (or
//   data-force-focus). The copy keeps the original's specificity, so it wins and loses where the
//   real state would.
// - StateRow marks the first interactive element of its Hover and Focus cells (markTarget), the
//   element a pointer or the Tab key would reach.
// - Stylesheets added or hot-reloaded later (lazy routes, Vite HMR) are copied when they appear.
import { onBeforeUnmount, onMounted } from 'vue'

const HOVER = ':is([data-force-hover], :has([data-force-hover]))'
const FOCUS = '[data-force-focus]'
const FOCUS_WITHIN = ':is([data-force-focus], :has([data-force-focus]))'

// :focus-visible and :focus-within before :focus, which must not match their prefix.
const SWAPS: readonly [RegExp, string][] = [
  [/:hover(?![\w-])/g, HOVER],
  [/:focus-visible(?![\w-])/g, FOCUS],
  [/:focus-within(?![\w-])/g, FOCUS_WITHIN],
  [/:focus(?![\w-])/g, FOCUS],
]

const PSEUDO = /:(hover|focus)/
// "Not hovered" can't be forced off: such rules are left alone.
const NEGATED = /:not\([^()]*:(hover|focus)/

/** The interactive element of a demo: what a pointer or the Tab key would reach first. */
const TARGET = [
  'input:not([type="hidden"])',
  'textarea',
  'select',
  'button',
  'a[href]',
  '[tabindex]:not([tabindex="-1"])',
  '[role="radio"]',
  '[role="switch"]',
].join(',')

export type ForcedState = 'hover' | 'focus'

/** The rule's selector with the pseudo-classes swapped, or null when it has none to force. */
export function forcedSelector(selector: string): string | null {
  if (!PSEUDO.test(selector) || NEGATED.test(selector)) return null
  let out = selector
  for (const [pattern, swap] of SWAPS) out = out.replace(pattern, swap)
  return out === selector ? null : out
}

function copyRules(rules: CSSRuleList, insert: (text: string) => void): void {
  const copies: string[] = []
  for (const rule of Array.from(rules)) {
    if (rule instanceof CSSStyleRule) {
      const selector = forcedSelector(rule.selectorText)
      if (selector && rule.style.length) copies.push(`${selector} { ${rule.style.cssText} }`)
    } else if (rule instanceof CSSGroupingRule) {
      // @media, @supports, @layer and @container: the copies go inside the same block.
      copyRules(rule.cssRules, (text) => rule.insertRule(text, rule.cssRules.length))
    }
  }
  for (const text of copies) {
    try {
      insert(text)
    } catch {
      // A selector this browser can't parse after the swap: that look stays unforced.
    }
  }
}

const copied = new WeakSet<CSSStyleSheet>()

function copyNewSheets(): void {
  for (const sheet of Array.from(document.styleSheets)) {
    if (copied.has(sheet)) continue
    copied.add(sheet)
    let rules: CSSRuleList
    try {
      rules = sheet.cssRules
    } catch {
      continue // A cross-origin sheet (web fonts): nothing of ours in it.
    }
    copyRules(rules, (text) => sheet.insertRule(text, sheet.cssRules.length))
  }
}

/** Copies the hover and focus rules of every stylesheet while the gallery is mounted. */
export function useForcedStates(): void {
  let observer: MutationObserver | null = null
  let frame = 0
  onMounted(() => {
    copyNewSheets()
    observer = new MutationObserver(() => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(copyNewSheets)
    })
    observer.observe(document.head, { childList: true, subtree: true, characterData: true })
  })
  onBeforeUnmount(() => {
    observer?.disconnect()
    cancelAnimationFrame(frame)
  })
}

/** Marks the first interactive element in `cell` as hovered or focused (and nothing else there). */
export function markTarget(cell: HTMLElement, state: ForcedState): void {
  const attribute = `data-force-${state}`
  const target = cell.querySelector<HTMLElement>(TARGET) ?? (cell.firstElementChild as HTMLElement | null)
  for (const el of Array.from(cell.querySelectorAll(`[${attribute}]`))) if (el !== target) el.removeAttribute(attribute)
  if (target && !target.hasAttribute(attribute)) target.setAttribute(attribute, '')
}

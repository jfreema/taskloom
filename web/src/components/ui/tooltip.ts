// The v-tooltip directive and the state of the one shared tooltip (spec 7.10 UiTooltipHost).
// Usage: v-tooltip="'Copy link'" or v-tooltip="{ text: 'Due date', kbd: 'D', placement: 'top' }".
// - Shows after 500ms of hover, at once if another tooltip closed within the last 300ms, and
//   immediately on :focus-visible. Never for touch input.
// - Hides on Esc, pointer down, scroll or blur.
// - Adds aria-describedby only when its text differs from the trigger's accessible name.
// - With `overflowOnly`, shows only while the trigger's text is cut off by an ellipsis.
// The directive only records each element's options: a few listeners on the document find the
// element under the pointer or focus (the nearest one whose tooltip is on), so a long list of
// rows adds no listeners of its own (S10).
import { reactive, type ObjectDirective } from 'vue'
import type { Placement } from '@floating-ui/vue'

export interface TooltipOptions {
  text: string
  kbd?: string
  placement?: Placement
  /**
   * Only while the text is cut off: the element, or a `.truncate` element inside it, is narrower
   * than its content (8.1: long names truncate and show the full text in a tooltip).
   */
  overflowOnly?: boolean
}

export type TooltipValue = string | TooltipOptions | null | undefined | false

export const TOOLTIP_ID = 'loom-tooltip'

export interface TooltipState {
  anchor: HTMLElement | null
  text: string
  kbd: string | undefined
  placement: Placement
  visible: boolean
}

/** Read by UiTooltipHost. */
export const tooltipState = reactive<TooltipState>({
  anchor: null,
  text: '',
  kbd: undefined,
  placement: 'top',
  visible: false,
})

const SHOW_DELAY_MS = 500
const WARM_WINDOW_MS = 300
const TOUCH_FOCUS_WINDOW_MS = 800

/** Each bound element's options; null while its tooltip is off. */
const bound = new WeakMap<HTMLElement, TooltipOptions | null>()
/** The element whose tooltip the pointer is over (not touch). */
let hovered: HTMLElement | null = null
let showTimer: ReturnType<typeof setTimeout> | undefined
let lastHiddenAt = 0
let lastTouchAt = 0
let globalsAttached = false

function normalize(value: TooltipValue): TooltipOptions | null {
  if (!value) return null
  if (typeof value === 'string') return value.trim() ? { text: value } : null
  return value.text && value.text.trim() ? value : null
}

function squash(s: string | null | undefined): string {
  return (s ?? '').replace(/\s+/g, ' ').trim().toLowerCase()
}

function accessibleName(el: HTMLElement): string {
  const label = el.getAttribute('aria-label')
  if (label) return label
  const labelledby = el.getAttribute('aria-labelledby')
  if (labelledby) {
    return labelledby
      .split(/\s+/)
      .map((id) => document.getElementById(id)?.textContent ?? '')
      .join(' ')
  }
  return el.textContent ?? ''
}

function setDescribedBy(el: HTMLElement, on: boolean): void {
  const ids = (el.getAttribute('aria-describedby') ?? '').split(/\s+/).filter(Boolean)
  const has = ids.includes(TOOLTIP_ID)
  if (on && !has) el.setAttribute('aria-describedby', [...ids, TOOLTIP_ID].join(' '))
  if (!on && has) {
    const rest = ids.filter((id) => id !== TOOLTIP_ID)
    if (rest.length) el.setAttribute('aria-describedby', rest.join(' '))
    else el.removeAttribute('aria-describedby')
  }
}

function onGlobalKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') hide()
}

function onGlobalScroll(): void {
  hide()
}

function attachGlobals(): void {
  if (globalsAttached) return
  globalsAttached = true
  document.addEventListener('keydown', onGlobalKeydown, true)
  window.addEventListener('scroll', onGlobalScroll, { capture: true, passive: true })
}

function detachGlobals(): void {
  if (!globalsAttached) return
  globalsAttached = false
  document.removeEventListener('keydown', onGlobalKeydown, true)
  window.removeEventListener('scroll', onGlobalScroll, { capture: true })
}

function isCutOff(el: HTMLElement): boolean {
  const candidates = [el, ...Array.from(el.querySelectorAll<HTMLElement>('.truncate'))]
  return candidates.some((c) => c.scrollWidth > c.clientWidth + 1)
}

function show(el: HTMLElement): void {
  clearTimeout(showTimer)
  const options = bound.get(el)
  if (!options || !el.isConnected) return
  if (options.overflowOnly && !isCutOff(el)) return
  if (tooltipState.anchor && tooltipState.anchor !== el) setDescribedBy(tooltipState.anchor, false)
  tooltipState.anchor = el
  tooltipState.text = options.text
  tooltipState.kbd = options.kbd
  tooltipState.placement = options.placement ?? 'top'
  tooltipState.visible = true
  setDescribedBy(el, squash(options.text) !== squash(accessibleName(el)))
  attachGlobals()
}

/** Hides the tooltip; with `el`, only if it belongs to that element. */
export function hide(el?: HTMLElement): void {
  clearTimeout(showTimer)
  if (el && tooltipState.anchor !== el) return
  if (tooltipState.visible) lastHiddenAt = Date.now()
  if (tooltipState.anchor) setDescribedBy(tooltipState.anchor, false)
  tooltipState.visible = false
  tooltipState.anchor = null
  detachGlobals()
}

function scheduleShow(el: HTMLElement): void {
  clearTimeout(showTimer)
  const warm = tooltipState.visible || Date.now() - lastHiddenAt < WARM_WINDOW_MS
  if (warm) show(el)
  else showTimer = setTimeout(() => show(el), SHOW_DELAY_MS)
}

/**
 * The nearest element from `node` up whose tooltip is on. An element whose tooltip is off (an
 * undefined value) leaves the tooltip to an ancestor, e.g. PageHeader's tooltip for a compacted
 * action button.
 */
function anchorOf(node: EventTarget | null): HTMLElement | null {
  for (let el = node instanceof Element ? node : null; el; el = el.parentElement) {
    if (el instanceof HTMLElement && bound.get(el)) return el
  }
  return null
}

function onPointerOver(e: PointerEvent): void {
  if (e.pointerType === 'touch') return
  const el = anchorOf(e.target)
  if (el === hovered) return
  if (hovered) hide(hovered)
  hovered = el
  if (el) scheduleShow(el)
}

function onPointerOut(e: PointerEvent): void {
  // The pointer left the page; moves between elements are pointerover's.
  if (e.relatedTarget || !hovered) return
  hide(hovered)
  hovered = null
}

function onPointerDown(e: PointerEvent): void {
  if (e.pointerType === 'touch') lastTouchAt = Date.now()
  const el = anchorOf(e.target)
  if (el) hide(el)
}

function onFocusIn(e: FocusEvent): void {
  if (Date.now() - lastTouchAt < TOUCH_FOCUS_WINDOW_MS) return
  const el = anchorOf(e.target)
  if (!el) return
  const target = e.target instanceof Element ? e.target : el
  let visible = false
  try {
    visible = target.matches(':focus-visible')
  } catch {
    visible = false
  }
  if (visible) show(el)
}

function onFocusOut(e: FocusEvent): void {
  const el = anchorOf(e.target)
  if (el) hide(el)
}

function unbind(el: HTMLElement): void {
  if (!bound.has(el)) return
  bound.delete(el)
  if (hovered === el) hovered = null
  if (tooltipState.anchor === el) hide()
}

if (typeof document !== 'undefined') {
  const passive = { capture: true, passive: true }
  document.addEventListener('pointerover', onPointerOver, passive)
  document.addEventListener('pointerout', onPointerOut, passive)
  document.addEventListener('pointerdown', onPointerDown, passive)
  document.addEventListener('focusin', onFocusIn, true)
  document.addEventListener('focusout', onFocusOut, true)
}

export const vTooltip: ObjectDirective<HTMLElement, TooltipValue> = {
  mounted(el, binding) {
    bound.set(el, normalize(binding.value))
  },
  updated(el, binding) {
    const options = normalize(binding.value)
    bound.set(el, options)
    if (tooltipState.anchor === el) {
      if (options) {
        tooltipState.text = options.text
        tooltipState.kbd = options.kbd
        tooltipState.placement = options.placement ?? 'top'
        setDescribedBy(el, squash(options.text) !== squash(accessibleName(el)))
      } else hide()
    }
  },
  beforeUnmount(el) {
    unbind(el)
  },
}

declare module 'vue' {
  interface GlobalDirectives {
    vTooltip: typeof vTooltip
  }
}

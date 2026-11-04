// The v-tooltip directive and the state of the one shared tooltip (spec 7.10 UiTooltipHost).
// Usage: v-tooltip="'Copy link'" or v-tooltip="{ text: 'Due date', kbd: 'D', placement: 'top' }".
// - Shows after 500ms of hover, at once if another tooltip closed within the last 300ms, and
//   immediately on :focus-visible. Never for touch input.
// - Hides on Esc, pointer down, scroll or blur.
// - Adds aria-describedby only when its text differs from the trigger's accessible name.
// - With `overflowOnly`, shows only while the trigger's text is cut off by an ellipsis.
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

interface Bound {
  options: TooltipOptions | null
  onEnter: (e: PointerEvent) => void
  onLeave: () => void
  onFocus: (e: FocusEvent) => void
  onBlur: () => void
  onDown: () => void
}

const bound = new WeakMap<HTMLElement, Bound>()
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
  const options = bound.get(el)?.options
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
  // An element whose tooltip is off (an undefined value) doesn't cancel one pending on an ancestor,
  // e.g. PageHeader's tooltip for a compacted action button.
  if (!bound.get(el)?.options) return
  clearTimeout(showTimer)
  const warm = tooltipState.visible || Date.now() - lastHiddenAt < WARM_WINDOW_MS
  if (warm) show(el)
  else showTimer = setTimeout(() => show(el), SHOW_DELAY_MS)
}

function bind(el: HTMLElement, value: TooltipValue): void {
  const entry: Bound = {
    options: normalize(value),
    onEnter: (e) => {
      if (e.pointerType === 'touch') return
      scheduleShow(el)
    },
    onLeave: () => hide(el),
    onFocus: (e) => {
      if (Date.now() - lastTouchAt < TOUCH_FOCUS_WINDOW_MS) return
      const target = e.target instanceof Element ? e.target : el
      let visible = false
      try {
        visible = target.matches(':focus-visible')
      } catch {
        visible = false
      }
      if (visible) show(el)
    },
    onBlur: () => hide(el),
    onDown: () => hide(el),
  }
  bound.set(el, entry)
  el.addEventListener('pointerenter', entry.onEnter)
  el.addEventListener('pointerleave', entry.onLeave)
  el.addEventListener('focusin', entry.onFocus)
  el.addEventListener('focusout', entry.onBlur)
  el.addEventListener('pointerdown', entry.onDown)
}

function unbind(el: HTMLElement): void {
  const entry = bound.get(el)
  if (!entry) return
  if (tooltipState.anchor === el) hide()
  el.removeEventListener('pointerenter', entry.onEnter)
  el.removeEventListener('pointerleave', entry.onLeave)
  el.removeEventListener('focusin', entry.onFocus)
  el.removeEventListener('focusout', entry.onBlur)
  el.removeEventListener('pointerdown', entry.onDown)
  bound.delete(el)
}

if (typeof document !== 'undefined') {
  document.addEventListener(
    'pointerdown',
    (e) => {
      if (e.pointerType === 'touch') lastTouchAt = Date.now()
    },
    { capture: true, passive: true },
  )
}

export const vTooltip: ObjectDirective<HTMLElement, TooltipValue> = {
  mounted(el, binding) {
    bind(el, binding.value)
  },
  updated(el, binding) {
    const entry = bound.get(el)
    if (!entry) return bind(el, binding.value)
    entry.options = normalize(binding.value)
    if (tooltipState.anchor === el) {
      if (entry.options) {
        tooltipState.text = entry.options.text
        tooltipState.kbd = entry.options.kbd
        tooltipState.placement = entry.options.placement ?? 'top'
        setDescribedBy(el, squash(entry.options.text) !== squash(accessibleName(el)))
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

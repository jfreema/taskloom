// Focus trap for modal layers (spec 7.7). While active, focus stays inside `el`, inside any layer
// stacked above it (a picker opened from a dialog), or inside a region marked data-layer-allow
// (the notifications region). Tab and Shift+Tab wrap at the ends. `el` must be the element of a
// registered layer (useLayer): the trap acts only while that layer is in the stack.
import { getCurrentInstance, onBeforeUnmount, watch, type Ref } from 'vue'
import { useLayerStack } from './useLayerStack'

const TABBABLE = [
  'a[href]',
  'area[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'iframe',
  'audio[controls]',
  'video[controls]',
  '[contenteditable]:not([contenteditable="false"])',
  '[tabindex]',
].join(',')

function isVisible(el: HTMLElement): boolean {
  if (typeof el.checkVisibility === 'function') return el.checkVisibility({ visibilityProperty: true })
  return el.getClientRects().length > 0
}

/** Keyboard-reachable elements inside `container`, in DOM order. */
export function tabbables(container: HTMLElement): HTMLElement[] {
  const found = Array.from(container.querySelectorAll<HTMLElement>(TABBABLE))
  return found.filter((el) => el.tabIndex >= 0 && !el.closest('[inert]') && isVisible(el))
}

/** Focuses the element matching `selector`, else the first tabbable element, else the container. */
export function focusFirst(container: HTMLElement, selector?: string): void {
  const chosen = selector ? container.querySelector<HTMLElement>(selector) : null
  const target = chosen ?? tabbables(container)[0] ?? container
  target.focus({ preventScroll: true })
}

export function useFocusTrap(el: Ref<HTMLElement | null>, active: Ref<boolean>): void {
  const layers = useLayerStack()
  let lastInside: HTMLElement | null = null
  let attached = false

  function onFocusIn(e: FocusEvent): void {
    const container = el.value
    // `active` turns false before watchers run, so a closing layer never pulls focus back.
    if (!active.value || !container || !container.isConnected) return
    // Nor does one whose component unmounts while open (a popover under v-if keeps a stale
    // `active`): its layer has left the stack before it returns focus, and the trap stands down.
    if (!layers.layerOf(container)) return
    const target = e.target
    if (!(target instanceof Node)) return
    if (layers.allowsFocus(container, target)) {
      if (container.contains(target) && target instanceof HTMLElement) lastInside = target
      return
    }
    const back = lastInside && lastInside.isConnected && container.contains(lastInside) ? lastInside : null
    if (back) back.focus({ preventScroll: true })
    else focusFirst(container)
  }

  function onKeydown(e: KeyboardEvent): void {
    if (e.key !== 'Tab' || e.defaultPrevented || !active.value) return
    const container = el.value
    const current = document.activeElement
    if (!container || !current || !container.contains(current)) return
    const items = tabbables(container)
    if (!items.length) {
      e.preventDefault()
      container.focus({ preventScroll: true })
      return
    }
    const first = items[0]
    const last = items[items.length - 1]
    if (e.shiftKey && (current === first || current === container)) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && current === last) {
      e.preventDefault()
      first.focus()
    }
  }

  function attach(): void {
    if (attached) return
    attached = true
    document.addEventListener('focusin', onFocusIn)
    document.addEventListener('keydown', onKeydown)
  }

  function detach(): void {
    if (!attached) return
    attached = false
    lastInside = null
    document.removeEventListener('focusin', onFocusIn)
    document.removeEventListener('keydown', onKeydown)
  }

  watch(
    [active, el],
    ([on, container]) => {
      if (on && container) attach()
      else detach()
    },
    { immediate: true },
  )

  if (getCurrentInstance()) onBeforeUnmount(detach)
}

// Roving tabindex (WAI-ARIA): one item of the container is in the tab order (tabindex 0), the
// others are -1, and arrow keys move focus between items. Home and End jump to the ends. In a
// 'grid', Left/Right move by one and Up/Down by one row (items laid out in rows by their offsetTop).
import { getCurrentScope, onScopeDispose, watch, type Ref } from 'vue'

export interface RovingFocusOptions {
  /** Selects the focusable items inside the container, e.g. '[data-row]'. */
  itemSelector: string
  orientation: 'vertical' | 'horizontal' | 'grid'
  /** Wrap from the last item to the first and back. Default false. */
  loop?: boolean
}

function isUsable(el: HTMLElement): boolean {
  if (el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true') return false
  if (el.closest('[hidden], [inert]')) return false
  return el.getClientRects().length > 0
}

export function useRovingFocus(container: Ref<HTMLElement | null>, o: RovingFocusOptions): { focusItem(i: number): void } {
  let current: HTMLElement | null = null
  let observer: MutationObserver | null = null
  let syncQueued = false

  function items(): HTMLElement[] {
    const root = container.value
    if (!root) return []
    return Array.from(root.querySelectorAll<HTMLElement>(o.itemSelector)).filter(isUsable)
  }

  /** Exactly one item keeps tabindex 0: the current one if it is still there, else the first. */
  function sync(): void {
    const list = items()
    if (!list.length) return
    if (!current || !list.includes(current)) {
      current = list.find((el) => el.tabIndex === 0) ?? list[0]
    }
    for (const el of list) {
      const want = el === current ? '0' : '-1'
      if (el.getAttribute('tabindex') !== want) el.setAttribute('tabindex', want)
    }
  }

  /**
   * After DOM changes, at the next frame: sync() reads layout (getClientRects), which then costs
   * nothing extra, where in the task that changed the DOM it would force a layout of its own (S10).
   */
  function queueSync(): void {
    if (syncQueued) return
    syncQueued = true
    requestAnimationFrame(() => {
      syncQueued = false
      sync()
    })
  }

  function focusItem(i: number): void {
    const list = items()
    if (!list.length) return
    const index = Math.max(0, Math.min(list.length - 1, i))
    current = list[index]
    sync()
    current.focus()
    current.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  }

  function rowOf(list: HTMLElement[], index: number): { start: number; length: number } {
    // Items of a row share their offsetTop; rows are consecutive in DOM order.
    const top = list[index].offsetTop
    let start = index
    while (start > 0 && list[start - 1].offsetTop === top) start--
    let end = index
    while (end < list.length - 1 && list[end + 1].offsetTop === top) end++
    return { start, length: end - start + 1 }
  }

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return
    const list = items()
    if (!list.length) return
    const target = e.target instanceof HTMLElement ? e.target : null
    const index = target ? list.findIndex((el) => el === target) : -1
    // Keys pressed in a control inside an item (an input, a button's menu) are the control's.
    if (index === -1) return
    const last = list.length - 1
    let next = -1
    const wrap = (i: number) => (o.loop ? (i + list.length) % list.length : Math.max(0, Math.min(last, i)))
    const vertical = o.orientation !== 'horizontal'
    const horizontal = o.orientation !== 'vertical'
    switch (e.key) {
      case 'ArrowDown':
        if (!vertical) return
        if (o.orientation === 'grid') {
          const { length } = rowOf(list, index)
          next = index + length <= last ? index + length : o.loop ? index % length : index
        } else next = wrap(index + 1)
        break
      case 'ArrowUp':
        if (!vertical) return
        if (o.orientation === 'grid') {
          const { length } = rowOf(list, index)
          next = index - length >= 0 ? index - length : index
        } else next = wrap(index - 1)
        break
      case 'ArrowRight':
        if (!horizontal) return
        next = wrap(index + 1)
        break
      case 'ArrowLeft':
        if (!horizontal) return
        next = wrap(index - 1)
        break
      case 'Home':
        next = 0
        break
      case 'End':
        next = last
        break
      default:
        return
    }
    e.preventDefault()
    if (next !== index) focusItem(next)
  }

  function onFocusIn(e: FocusEvent): void {
    const target = e.target instanceof HTMLElement ? e.target : null
    if (!target) return
    const item = items().find((el) => el === target || el.contains(target))
    if (item && item !== current) {
      current = item
      sync()
    }
  }

  function detach(el: HTMLElement): void {
    el.removeEventListener('keydown', onKeydown)
    el.removeEventListener('focusin', onFocusIn)
    observer?.disconnect()
    observer = null
  }

  const stop = watch(
    container,
    (el, old) => {
      if (old) detach(old)
      if (!el) return
      el.addEventListener('keydown', onKeydown)
      el.addEventListener('focusin', onFocusIn)
      if (typeof MutationObserver !== 'undefined') {
        observer = new MutationObserver(queueSync)
        observer.observe(el, { childList: true, subtree: true })
      }
      queueSync()
    },
    { immediate: true, flush: 'post' },
  )

  if (getCurrentScope()) {
    onScopeDispose(() => {
      stop()
      if (container.value) detach(container.value)
    })
  }

  return { focusItem }
}

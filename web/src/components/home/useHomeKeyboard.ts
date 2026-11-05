// Home's rows as one keyboard grid (8.5, 9.5, 9.6). Grid scope over the sections container:
// Up/Down or J/K move between rows, Home/End jump, Enter opens, Mod+Enter completes or reopens,
// D reschedules, Delete or Mod+Backspace deletes with undo, Shift+F10 or ContextMenu opens the
// row's menu. One roving tab stop. When the focused row leaves (completed after its hold,
// rescheduled into another section, deleted) focus moves to the next row, else the previous one,
// else a section header. The panel follows the visible order and the open row scrolls into view.
// From elsewhere on the page (or the body), Up/Down or J/K focus the remembered row (the last
// focused one, else the open task's), else the first row.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useShortcut } from '../../composables/useShortcut'
import { focusOutsideGrids } from '../shell/shellContext'
import { useTaskActions } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import type { Uuid } from '../../types'

const ROW = '[data-task-row]'

export function useHomeKeyboard(o: {
  container: Ref<HTMLElement | null>
  /** The ids of the rendered rows, in order (expanded sections only). */
  order: ComputedRef<Uuid[]>
  canEdit: () => boolean
  reschedule: (id: Uuid, anchor: HTMLElement) => void
}): { tabStopId: ComputedRef<Uuid | null> } {
  const panel = useTaskPanel()
  const actions = useTaskActions()

  /** The row that holds the tab stop; the first row until another one takes focus. */
  const activeId = ref<Uuid | null>(null)
  const tabStopId = computed(() => {
    const id = activeId.value
    return id && o.order.value.includes(id) ? id : (o.order.value[0] ?? null)
  })

  /** The row that had focus most recently, kept while focus is in an overlay opened from it. */
  let lastFocusedId: Uuid | null = null

  function rows(): HTMLElement[] {
    return o.container.value ? Array.from(o.container.value.querySelectorAll<HTMLElement>(ROW)) : []
  }

  function rowEl(id: Uuid): HTMLElement | null {
    return o.container.value?.querySelector<HTMLElement>(`${ROW}[data-task-row="${CSS.escape(id)}"]`) ?? null
  }

  /** The row that contains the focus, if any. */
  function focusedRow(): HTMLElement | null {
    const a = document.activeElement
    const row = a instanceof Element ? a.closest<HTMLElement>(ROW) : null
    return row && o.container.value?.contains(row) ? row : null
  }

  function focusRow(el: HTMLElement | null | undefined): void {
    if (!el) return
    activeId.value = el.dataset.taskRow ?? null
    el.focus({ preventScroll: true })
    el.scrollIntoView?.({ block: 'nearest' })
  }

  /** The next (or previous) row in document order from the focused element, a header included. */
  function step(delta: 1 | -1): boolean {
    const all = rows()
    if (!all.length) return false
    const current = focusedRow()
    if (current) {
      const i = all.indexOf(current)
      const next = all[i + delta]
      if (next) focusRow(next)
      return true
    }
    const a = document.activeElement
    if (!(a instanceof Element)) return false
    const after = (el: Element) => !!(a.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING)
    const target = delta === 1 ? all.find(after) : [...all].reverse().find((el) => !after(el))
    if (target) focusRow(target)
    return true
  }

  const grid = { scope: 'grid' as const, el: o.container }
  const focusedId = () => focusedRow()?.dataset.taskRow ?? null

  useShortcut(['ArrowDown', 'j'], () => step(1), grid)
  useShortcut(['ArrowUp', 'k'], () => step(-1), grid)
  useShortcut('Home', () => {
    if (!focusedRow()) return false
    focusRow(rows()[0])
  }, grid)
  useShortcut('End', () => {
    if (!focusedRow()) return false
    const all = rows()
    focusRow(all[all.length - 1])
  }, grid)
  useShortcut('Enter', () => {
    const row = focusedRow()
    // Enter on a control inside the row is the control's own.
    if (!row || document.activeElement !== row) return false
    panel.open(row.dataset.taskRow!)
  }, grid)
  useShortcut('Mod+Enter', () => {
    const id = focusedId()
    if (!id || !o.canEdit()) return false
    void actions.toggleComplete(id)
  }, grid)
  useShortcut('d', () => {
    const row = focusedRow()
    if (!row || !o.canEdit()) return false
    o.reschedule(row.dataset.taskRow!, row)
  }, grid)
  useShortcut(['Delete', 'Mod+Backspace'], () => {
    const id = focusedId()
    if (!id || !o.canEdit()) return false
    actions.remove([id])
  }, grid)
  useShortcut(['Shift+F10', 'ContextMenu'], () => {
    const row = focusedRow()
    const button = row?.querySelector<HTMLElement>('[data-row-menu]')
    if (!button) return false
    button.click()
  }, grid)

  // Getting in: the keys the grid owns, pressed outside it.
  useShortcut(
    ['ArrowDown', 'j', 'ArrowUp', 'k'],
    () => {
      const ids = o.order.value
      const remembered = [activeId.value, panel.openId.value].find((id): id is Uuid => !!id && ids.includes(id))
      const target = remembered ?? ids[0]
      if (!target) return false
      focusRow(rowEl(target))
    },
    { when: () => focusOutsideGrids() && !o.container.value?.contains(document.activeElement) },
  )

  // ---- Focus bookkeeping ----

  function onFocusIn(e: FocusEvent): void {
    const t = e.target
    if (!(t instanceof Element)) return
    const row = t.closest<HTMLElement>(ROW)
    if (row && o.container.value?.contains(row)) {
      activeId.value = row.dataset.taskRow ?? null
      lastFocusedId = activeId.value
    } else if (!t.closest('#overlay-root')) {
      // Focus went elsewhere on the page (not into a menu or picker opened from the row).
      lastFocusedId = null
    }
  }

  function onPointerDown(e: PointerEvent): void {
    const t = e.target
    if (t instanceof Node && !o.container.value?.contains(t) && !(t instanceof Element && t.closest('#overlay-root'))) {
      lastFocusedId = null
    }
  }

  onMounted(() => {
    document.addEventListener('focusin', onFocusIn)
    document.addEventListener('pointerdown', onPointerDown, true)
  })
  onBeforeUnmount(() => {
    document.removeEventListener('focusin', onFocusIn)
    document.removeEventListener('pointerdown', onPointerDown, true)
  })

  /**
   * The focused row left its place (9.6): focus the next row of the old order, else the previous,
   * else a section header. A row that only moved to another section counts as gone from here.
   */
  function rescue(prev: Uuid[], next: Uuid[], id: Uuid): void {
    const shown = new Set(next)
    shown.delete(id)
    const i = prev.indexOf(id)
    let target: Uuid | undefined
    for (let j = i + 1; j < prev.length && !target; j++) if (shown.has(prev[j])) target = prev[j]
    for (let j = i - 1; j >= 0 && !target; j--) if (shown.has(prev[j])) target = prev[j]
    if (!target) target = next.find((x) => x !== id)
    const el = target ? rowEl(target) : null
    lastFocusedId = null
    if (el) focusRow(el)
    else o.container.value?.querySelector<HTMLElement>('[data-section-toggle]')?.focus({ preventScroll: true })
  }

  // Runs after the DOM update: a focused row that left (or moved to another section, which renders
  // it anew) has dropped focus to the body by then. Overlays return focus before the update, so a
  // picker or menu closed from the row has already put focus back in it.
  watch(
    o.order,
    (next, prev) => {
      const id = lastFocusedId
      if (!id || !prev) return
      const a = document.activeElement
      if (a && a !== document.body && a.isConnected) return
      rescue(prev, next, id)
    },
    { flush: 'post' },
  )

  // ---- The panel ----

  watch(o.order, (ids) => panel.setOrder(ids), { immediate: true })

  // Following the open task (8.1): its row scrolls into view without taking focus.
  watch(
    () => panel.openId.value,
    (id) => {
      if (!id) return
      void nextTick(() => rowEl(id)?.scrollIntoView?.({ block: 'nearest' }))
    },
    { immediate: true },
  )

  return { tabStopId }
}

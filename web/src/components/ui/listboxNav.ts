// The active row of a UiListbox: the row the keys move through and Enter picks. It is kept by key,
// so it survives a re-filter, and scrolled into view inside the list, never the page (a listbox
// can sit inline in a page). Moves clamp at the ends; they don't wrap.
import { computed, nextTick, ref, watch, type ComputedRef, type Ref } from 'vue'
import type { ListboxRow } from './listbox'

/** How many rows PageUp and PageDown move. */
const PAGE = 8

export function useListboxNav(o: {
  /** The rows the keys move through, in display order (no disabled options). */
  rows: ComputedRef<readonly ListboxRow[]>
  list: Ref<HTMLElement | null>
  rowId: (row: ListboxRow) => string
}) {
  const activeKey = ref<string | null>(null)
  /** The active row was reached by keyboard (it shows the focus ring) rather than by pointer. */
  const byKeyboard = ref(false)
  const activeRow = computed(() => o.rows.value.find((r) => r.key === activeKey.value) ?? null)
  const activeId = computed(() => (activeRow.value ? o.rowId(activeRow.value) : undefined))

  function scrollIntoView(): void {
    void nextTick(() => {
      const list = o.list.value
      const row = activeId.value ? document.getElementById(activeId.value) : null
      if (!list || !row) return
      const r = row.getBoundingClientRect()
      const c = list.getBoundingClientRect()
      if (r.top < c.top) list.scrollTop -= c.top - r.top
      else if (r.bottom > c.bottom) list.scrollTop += r.bottom - c.bottom
    })
  }

  function setActive(index: number, keyboard: boolean): void {
    const rows = o.rows.value
    if (!rows.length) {
      activeKey.value = null
      return
    }
    activeKey.value = rows[Math.max(0, Math.min(rows.length - 1, index))].key
    byKeyboard.value = keyboard
    if (keyboard) scrollIntoView()
  }

  // Rows that change keep the active row while it is still there.
  watch(o.rows, (rows) => {
    if (!rows.some((r) => r.key === activeKey.value)) setActive(0, byKeyboard.value)
  })

  function target(key: string, at: number, last: number): number | null {
    switch (key) {
      case 'ArrowDown':
        return at < 0 ? 0 : at + 1
      case 'ArrowUp':
        return at < 0 ? 0 : at - 1
      case 'Home':
        return 0
      case 'End':
        return last
      case 'PageDown':
        return at + PAGE
      case 'PageUp':
        return at - PAGE
      default:
        return null
    }
  }

  /** Handles a moving key (the arrows, Home, End, PageUp, PageDown); true when it did. */
  function move(e: KeyboardEvent): boolean {
    const rows = o.rows.value
    const at = activeRow.value ? rows.indexOf(activeRow.value) : -1
    const to = target(e.key, at, rows.length - 1)
    if (to === null) return false
    e.preventDefault()
    setActive(to, true)
    return true
  }

  /** The pointer moved over a row: it becomes the active one, without the keyboard ring. */
  function hover(row: ListboxRow, e: PointerEvent): void {
    if (e.pointerType === 'touch' || activeKey.value === row.key) return
    if (row.kind === 'option' && row.option.disabled) return
    activeKey.value = row.key
    byKeyboard.value = false
  }

  return { activeKey, byKeyboard, activeRow, activeId, setActive, scrollIntoView, move, hover }
}

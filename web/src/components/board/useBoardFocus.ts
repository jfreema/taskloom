// Keeps keyboard focus on the Board when cards come and go (8.8, 8.1):
// - refocus(target) puts focus on a card or a collapsed column's strip after the next render
//   (keyboard moves re-render the card in its new place).
// - A card that leaves while it holds focus (deleted, moved away, completed while Done is hidden)
//   hands focus to the next card of its column, else the previous one, else the column's "+".
//   Each render keeps a snapshot of the columns' cards, so the rescue knows where the card was.
// - Focus that moves on elsewhere, or a click on the page, is left alone.
// Bind onFocusIn and onFocusOut to the board element.
import { nextTick, watch, type Ref } from 'vue'
import type { Uuid } from '../../types'
import { cardEl, focusEl, type FocusTarget } from './useBoardKeyboard'
import type { BoardModel } from './useBoardModel'

export interface BoardFocus {
  refocus(target: FocusTarget): void
  onFocusIn(e: FocusEvent): void
  onFocusOut(e: FocusEvent): void
}

function focusLost(): boolean {
  const active = document.activeElement
  return !active || active === document.body || !active.isConnected
}

export function useBoardFocus(root: Ref<HTMLElement | null>, model: BoardModel): BoardFocus {
  let pending: FocusTarget | null = null
  /** The card that holds focus (or a control inside it), and each column's cards at the last render. */
  let focusedId: Uuid | null = null
  let snapshot: { statusId: Uuid; ids: Uuid[] }[] = []

  function applyPending(): void {
    const target = pending
    pending = null
    if (!target) return
    if ('card' in target) focusEl(cardEl(root.value, target.card))
    else focusEl(root.value?.querySelector<HTMLElement>(`[data-column="${CSS.escape(target.strip)}"] [data-strip]`))
  }

  function refocus(target: FocusTarget): void {
    pending = target
    void nextTick(applyPending)
  }

  /** Focus was on a card that is gone: the next card of its column, else the previous one, else the column. */
  function rescue(id: Uuid): void {
    const el = root.value
    const again = cardEl(el, id)
    if (again) {
      focusEl(again)
      return
    }
    const col = snapshot.find((c) => c.ids.includes(id))
    focusedId = null
    if (!col || !el) return
    const i = col.ids.indexOf(id)
    const candidates = [...col.ids.slice(i + 1), ...col.ids.slice(0, i).reverse()]
    for (const other of candidates) if (focusEl(cardEl(el, other))) return
    const column = el.querySelector<HTMLElement>(`[data-column="${CSS.escape(col.statusId)}"]`)
    focusEl(column?.querySelector<HTMLElement>('[data-add-top], button') ?? null)
  }

  watch(
    () => model.columns.value,
    (cols) => {
      if (!pending && focusedId && focusLost()) rescue(focusedId)
      snapshot = cols.map((c) => ({ statusId: c.status.id, ids: c.collapsed ? [] : c.shown.map((t) => t.id) }))
    },
    { flush: 'post', immediate: true },
  )

  function onFocusIn(e: FocusEvent): void {
    const card = e.target instanceof Element ? e.target.closest<HTMLElement>('[data-card]') : null
    focusedId = card?.dataset.taskRow ?? null
  }

  function onFocusOut(e: FocusEvent): void {
    const next = e.relatedTarget
    if (next instanceof Node && root.value?.contains(next)) return
    const id = focusedId
    const el = e.target instanceof HTMLElement ? e.target : null
    requestAnimationFrame(() => {
      if (!id || focusedId !== id) return
      if (el?.isConnected || !focusLost()) focusedId = null
      else if (!pending) rescue(id)
    })
  }

  return { refocus, onFocusIn, onFocusOut }
}

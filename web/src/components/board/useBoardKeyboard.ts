// The Board's grid keys (8.8, 9.5), registered with scope 'grid' on the board, so they work only while
// focus is inside it and never while typing (useShortcut).
// - Up/Down and J/K move within a column; Left/Right move to the next column with cards, to the same
//   index or else its last card. From a column's header or add button, Up/Down reach its cards.
// - Enter opens the card; Mod+Enter completes or reopens it; S, A, D and P open its pickers; F2
//   renames it; Shift+F10 opens its menu; Delete removes it (with undo).
// - Alt+Left/Right move the card to the previous or next column at the same index, Alt+Up/Down move
//   it one place; each move is announced ("Moved to In progress, 1 of 3").
// - Esc lets a docked panel close first, then leaves the board for the page.
import { type ComputedRef, type Ref } from 'vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { useShortcut } from '../../composables/useShortcut'
import { useTaskActions, type PickField } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import type { BoardColumnView, BoardModel } from './useBoardModel'

/** Where focus should go after the next render. */
export type FocusTarget = { card: Uuid } | { strip: Uuid }

export interface BoardKeyboardDeps {
  root: Ref<HTMLElement | null>
  model: BoardModel
  canEdit: ComputedRef<boolean>
  pick(id: Uuid, field: PickField): void
  startRename(id: Uuid): void
  /** Focuses the target now, or after the render that puts it in place. */
  refocus(target: FocusTarget): void
}

export function cardEl(root: HTMLElement | null, id: Uuid): HTMLElement | null {
  return root?.querySelector<HTMLElement>(`[data-card][data-task-row="${CSS.escape(id)}"]`) ?? null
}

export function focusEl(el: HTMLElement | null | undefined): boolean {
  if (!el) return false
  el.focus({ preventScroll: true })
  el.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  return document.activeElement === el
}

export function useBoardKeyboard(d: BoardKeyboardDeps): void {
  const tasks = useTasks()
  const panel = useTaskPanel()
  const actions = useTaskActions()
  const announce = useAnnouncer()
  const grid = { scope: 'grid' as const, el: d.root }

  interface Current {
    id: Uuid
    el: HTMLElement
    /** Focus is on the card itself, not on a control inside it. */
    onCard: boolean
  }

  function current(): Current | null {
    const root = d.root.value
    const active = document.activeElement
    if (!root || !(active instanceof HTMLElement) || !root.contains(active)) return null
    const el = active.closest<HTMLElement>('[data-card]')
    const id = el?.dataset.taskRow
    if (!el || !id) return null
    return { id, el, onCard: active === el }
  }

  /** The column holding focus when it isn't on a card (its header, add button, empty area). */
  function currentColumn(): BoardColumnView | null {
    const root = d.root.value
    const active = document.activeElement
    if (!root || !(active instanceof HTMLElement) || !root.contains(active)) return null
    const statusId = active.closest<HTMLElement>('[data-column]')?.dataset.column
    return d.model.columns.value.find((c) => c.status.id === statusId) ?? null
  }

  function columnIndexOf(id: Uuid): number {
    return d.model.columns.value.findIndex((c) => !c.collapsed && c.shown.some((t) => t.id === id))
  }

  function focusCard(id: Uuid | undefined): boolean {
    return !!id && focusEl(cardEl(d.root.value, id))
  }

  // ---- Moving focus ----

  function vertical(delta: 1 | -1): boolean {
    const c = current()
    if (!c) {
      const col = currentColumn()
      if (!col || col.collapsed || !col.shown.length) return false
      return focusCard(delta === 1 ? col.shown[0].id : col.shown[col.shown.length - 1].id)
    }
    const ids = d.model.siblingIds(c.id)
    const i = ids.indexOf(c.id)
    const next = ids[Math.min(Math.max(i + delta, 0), ids.length - 1)]
    if (next && next !== c.id) focusCard(next)
    return true
  }

  function across(delta: 1 | -1): boolean {
    const c = current()
    if (!c) return false
    const cols = d.model.columns.value
    const from = columnIndexOf(c.id)
    if (from === -1) return true
    const index = cols[from].shown.findIndex((t) => t.id === c.id)
    for (let i = from + delta; i >= 0 && i < cols.length; i += delta) {
      const col = cols[i]
      if (col.collapsed || !col.shown.length) continue
      focusCard(col.shown[Math.min(index, col.shown.length - 1)].id)
      return true
    }
    return true
  }

  useShortcut(['ArrowDown', 'j'], () => vertical(1), grid)
  useShortcut(['ArrowUp', 'k'], () => vertical(-1), grid)
  useShortcut('ArrowRight', () => across(1), grid)
  useShortcut('ArrowLeft', () => across(-1), grid)

  // ---- Acting on the focused card ----

  const editable = (id: Uuid) => d.canEdit.value && !tasks.isPendingDelete(id)

  useShortcut(
    'Enter',
    () => {
      const c = current()
      if (!c || !c.onCard) return false
      panel.open(c.id)
      return true
    },
    grid,
  )

  useShortcut(
    'Mod+Enter',
    () => {
      const c = current()
      if (!c || !editable(c.id)) return false
      void actions.toggleComplete(c.id)
      return true
    },
    grid,
  )

  const fields: [string, PickField][] = [
    ['s', 'status'],
    ['a', 'assignees'],
    ['d', 'dueDate'],
    ['p', 'priority'],
  ]
  for (const [key, field] of fields) {
    useShortcut(
      key,
      () => {
        const c = current()
        if (!c || !editable(c.id)) return false
        d.pick(c.id, field)
        return true
      },
      grid,
    )
  }

  useShortcut(
    'F2',
    () => {
      const c = current()
      if (!c || !editable(c.id)) return false
      d.startRename(c.id)
      return true
    },
    grid,
  )

  useShortcut(
    ['Shift+F10', 'ContextMenu'],
    () => {
      const c = current()
      const button = c?.el.querySelector<HTMLElement>('[data-card-menu]')
      if (!button) return false
      // The contextmenu event that may follow finds the menu open and leaves it (BoardCard).
      if (button.getAttribute('aria-expanded') !== 'true') button.click()
      return true
    },
    grid,
  )

  useShortcut(
    ['Delete', 'Mod+Backspace'],
    () => {
      const c = current()
      if (!c || !editable(c.id)) return false
      tasks.remove([c.id])
      return true
    },
    grid,
  )

  // ---- Moving the card ----

  function moveAcross(delta: 1 | -1): boolean {
    const c = current()
    if (!c) return false
    if (!editable(c.id)) return true
    const cols = d.model.columns.value
    const from = columnIndexOf(c.id)
    const to = cols[from + delta]
    if (from === -1 || !to) return true
    const index = cols[from].shown.findIndex((t) => t.id === c.id)
    const ids = to.collapsed ? [] : to.shown.map((t) => t.id)
    // A collapsed column shows no cards: the card goes to its end.
    const at = to.collapsed ? to.tasks.length : Math.min(index, ids.length)
    const beforeTaskId = ids[at - 1] ?? null
    const afterTaskId = ids[at] ?? null
    d.model.placed(c.id)
    void tasks.move(c.id, { statusId: to.status.id, beforeTaskId, afterTaskId }).catch(noop)
    announce(`Moved to ${to.status.name}, ${at + 1} of ${to.tasks.length + 1}`)
    d.refocus(to.collapsed ? { strip: to.status.id } : { card: c.id })
    return true
  }

  function moveAlong(where: 'up' | 'down'): boolean {
    const c = current()
    if (!c) return false
    if (!editable(c.id)) return true
    void actions.reorder(c.id, where, d.model.siblingIds(c.id))
    // The card moves in the DOM; keep it focused and in view.
    d.refocus({ card: c.id })
    return true
  }

  useShortcut('Alt+ArrowRight', () => moveAcross(1), grid)
  useShortcut('Alt+ArrowLeft', () => moveAcross(-1), grid)
  useShortcut('Alt+ArrowUp', () => moveAlong('up'), grid)
  useShortcut('Alt+ArrowDown', () => moveAlong('down'), grid)

  // ---- Leaving ----

  useShortcut(
    'Escape',
    () => {
      // A docked panel closes first (TaskPanelHost, global scope); then Esc leaves the board.
      if (panel.isOpen.value && panel.mode.value === 'docked') return false
      const main = document.getElementById('main')
      if (!main) return false
      main.focus({ preventScroll: true })
      return true
    },
    grid,
  )
}

// The List's grid keys (8.7, 9.5), registered with scope 'grid' on the treegrid, so they work only
// while focus is inside it, and never while typing (useShortcut).
// - Every row has one focus target, its `[data-nav]` element: the row itself for tasks, the
//   disclosure button for group headers, the button (or input) of add and "Show N more" rows.
// - Up/Down and J/K move between them (also from a control inside a row); Home/End go to the ends.
// - Left/Right collapse and expand, or go to the parent or the first subtask; on a group header
//   they collapse and expand the group.
// - Enter opens, X (and Space on the row) toggles the selection, Mod+A selects every visible
//   top-level task, Esc clears the selection, then lets a docked panel close, then leaves the grid.
// - Mod+Enter, S/A/D/P, F2, Shift+F10, Delete and Alt+Up/Down act on the focused task.
import { nextTick, type ComputedRef, type Ref } from 'vue'
import { useShortcut } from '../../composables/useShortcut'
import { useTaskActions, type PickField } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useSelection } from '../../stores/selection'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import { taskKey, type ListModel, type RowItem, type TaskRowItem } from './useListModel'

export interface ListKeyboardDeps {
  root: Ref<HTMLElement | null>
  model: ListModel
  canEdit: ComputedRef<boolean>
  selectable: ComputedRef<boolean>
  canReorder: ComputedRef<boolean>
  startRename(id: Uuid): void
  pick(id: Uuid, field: PickField): void
}

/** The element that takes focus for a row. */
export function navOf(row: Element): HTMLElement | null {
  if (row instanceof HTMLElement && row.matches('[data-nav]')) return row
  return row.querySelector<HTMLElement>('[data-nav]')
}

/** Focuses a row's focus target and scrolls it into view (scroll-padding keeps it clear of the sticky headers). */
export function focusRow(root: HTMLElement | null, key: string, o: { scroll?: boolean } = {}): boolean {
  if (!root) return false
  const row = root.querySelector(`[data-row-key="${CSS.escape(key)}"]`)
  const target = row ? navOf(row) : null
  if (!target) return false
  target.focus({ preventScroll: true })
  if (o.scroll !== false) target.scrollIntoView?.({ block: 'nearest' })
  return document.activeElement === target
}

export function useListKeyboard(d: ListKeyboardDeps): void {
  const selection = useSelection()
  const tasks = useTasks()
  const panel = useTaskPanel()
  const actions = useTaskActions()
  const grid = { scope: 'grid' as const, el: d.root }

  interface Current {
    row: HTMLElement
    key: string
    item: RowItem | null
    /** Focus is on the row's own focus target, not on a control inside it. */
    onTarget: boolean
  }

  function current(): Current | null {
    const root = d.root.value
    const active = document.activeElement
    if (!root || !(active instanceof HTMLElement) || !root.contains(active)) return null
    const row = active.closest<HTMLElement>('[data-row-key]')
    const key = row?.dataset.rowKey
    if (!row || !key) return null
    return { row, key, item: d.model.rowByKey.value.get(key) ?? null, onTarget: active === navOf(row) }
  }

  function currentTask(): (Current & { item: TaskRowItem }) | null {
    const c = current()
    return c && c.item?.kind === 'task' ? (c as Current & { item: TaskRowItem }) : null
  }

  function focusEl(el: HTMLElement | undefined): boolean {
    if (!el) return false
    el.focus({ preventScroll: true })
    el.scrollIntoView?.({ block: 'nearest' })
    return true
  }

  function navs(): HTMLElement[] {
    const root = d.root.value
    return root ? Array.from(root.querySelectorAll<HTMLElement>('[data-nav]')) : []
  }

  function move(delta: number | 'first' | 'last'): boolean {
    const list = navs()
    if (!list.length) return false
    const c = current()
    const from = c ? list.indexOf(navOf(c.row) as HTMLElement) : -1
    let i: number
    if (delta === 'first') i = 0
    else if (delta === 'last') i = list.length - 1
    else if (from < 0) i = 0
    else i = Math.min(Math.max(from + delta, 0), list.length - 1)
    return focusEl(list[i])
  }

  // ---- Moving between rows ----

  useShortcut(['ArrowDown', 'j'], () => move(1), grid)
  useShortcut(['ArrowUp', 'k'], () => move(-1), grid)
  useShortcut('Home', () => move('first'), grid)
  useShortcut('End', () => move('last'), grid)

  useShortcut(
    'ArrowRight',
    () => {
      const c = current()
      if (!c || !c.onTarget) return false
      if (c.key.startsWith('h:')) {
        const g = c.key.slice(2)
        if (d.model.isCollapsed(g)) d.model.toggleGroup(g, false)
        return true
      }
      if (c.item?.kind !== 'task') return false
      const r = c.item
      if (r.hasChildren && !r.expanded) {
        d.model.toggleExpanded(r.task.id, true)
        return true
      }
      if (r.expanded) {
        // The first subtask is the next row.
        const keys = d.model.navKeys.value
        const next = keys[keys.indexOf(c.key) + 1]
        const item = next ? d.model.rowByKey.value.get(next) : undefined
        if (item?.kind === 'task' && item.parentId === r.task.id) move(1)
        return true
      }
      return true
    },
    grid,
  )

  useShortcut(
    'ArrowLeft',
    () => {
      const c = current()
      if (!c || !c.onTarget) return false
      if (c.key.startsWith('h:')) {
        const g = c.key.slice(2)
        if (!d.model.isCollapsed(g)) d.model.toggleGroup(g, true)
        return true
      }
      if (c.item?.kind !== 'task') return false
      const r = c.item
      if (r.expanded) {
        d.model.toggleExpanded(r.task.id, false)
        return true
      }
      if (r.parentId) focusRow(d.root.value, taskKey(r.parentId))
      return true
    },
    grid,
  )

  // ---- Opening and selecting ----

  useShortcut(
    'Enter',
    () => {
      const c = currentTask()
      if (!c || !c.onTarget) return false
      panel.open(c.item.task.id)
      return true
    },
    grid,
  )

  function toggleSelect(c: Current & { item: TaskRowItem }): boolean {
    if (!d.selectable.value || c.item.level !== 1) return true
    selection.toggle(c.item.task.id)
    return true
  }

  useShortcut(
    'x',
    () => {
      const c = currentTask()
      return c ? toggleSelect(c) : false
    },
    grid,
  )

  useShortcut(
    'Space',
    () => {
      const c = currentTask()
      if (!c || !c.onTarget) return false
      return toggleSelect(c)
    },
    grid,
  )

  useShortcut(
    'Mod+a',
    () => {
      if (!d.selectable.value) return false
      selection.select(d.model.renderedTopIds.value)
      return true
    },
    grid,
  )

  useShortcut(
    'Escape',
    () => {
      if (selection.count > 0) {
        selection.clear()
        return true
      }
      // A docked panel closes first (TaskPanelHost, global scope); then Esc leaves the grid.
      if (panel.isOpen.value && panel.mode.value === 'docked') return false
      const main = document.getElementById('main')
      if (!main) return false
      main.focus({ preventScroll: true })
      return true
    },
    grid,
  )

  // ---- Acting on the focused task ----

  const editable = () => d.canEdit.value

  useShortcut(
    'Mod+Enter',
    () => {
      const c = currentTask()
      if (!c || !editable() || tasks.isPendingDelete(c.item.task.id)) return false
      void actions.toggleComplete(c.item.task.id)
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
        const c = currentTask()
        if (!c || !editable()) return false
        d.pick(c.item.task.id, field)
        return true
      },
      grid,
    )
  }

  useShortcut(
    'F2',
    () => {
      const c = currentTask()
      if (!c || !editable()) return false
      d.startRename(c.item.task.id)
      return true
    },
    grid,
  )

  useShortcut(
    ['Shift+F10', 'ContextMenu'],
    () => {
      const c = currentTask()
      const button = c?.row.querySelector<HTMLElement>('[data-row-menu]')
      if (!button) return false
      // The contextmenu event that may follow finds the menu open and leaves it (ListRow).
      if (button.getAttribute('aria-expanded') !== 'true') button.click()
      return true
    },
    grid,
  )

  useShortcut(
    ['Delete', 'Mod+Backspace'],
    () => {
      const c = currentTask()
      if (!c || !editable()) return false
      const id = c.item.task.id
      if (c.item.level === 1 && selection.has(id)) tasks.remove([...selection.ids])
      else tasks.remove([id])
      return true
    },
    grid,
  )

  function reorder(where: 'up' | 'down'): boolean {
    const c = currentTask()
    if (!c) return false
    if (!d.canReorder.value || c.item.level !== 1) return true
    const id = c.item.task.id
    void actions.reorder(id, where, d.model.siblingIds(id))
    // The row moves in the DOM; keep it focused and in view.
    void nextTick(() => focusRow(d.root.value, taskKey(id)))
    return true
  }

  useShortcut('Alt+ArrowUp', () => reorder('up'), grid)
  useShortcut('Alt+ArrowDown', () => reorder('down'), grid)
}

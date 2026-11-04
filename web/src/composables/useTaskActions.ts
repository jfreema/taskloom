// Task actions shared by rows, cards and the panel (10.2), and the shared row and card menus of
// 8.5, 8.7, 8.8 and 8.10.
import {
  ArrowDown,
  ArrowDownToLine,
  ArrowUp,
  ArrowUpToLine,
  Calendar,
  CalendarClock,
  CircleCheck,
  FolderInput,
  Link,
  ListTree,
  Maximize2,
  Pencil,
  SquareCheck,
  Trash2,
  Undo2,
  UserRoundPlus,
} from '@lucide/vue'
import type { MenuItem } from '../components/ui/menu'
import { handled, noop } from '../lib/async'
import { useSelection } from '../stores/selection'
import { useTasks } from '../stores/tasks'
import { useUi } from '../stores/ui'
import { useWorkspaces } from '../stores/workspaces'
import type { Task, Uuid } from '../types'
import { appRouter } from './appRouter'
import { useAnnouncer } from './useAnnouncer'
import { useBreakpoint } from './useBreakpoint'
import { useClipboard } from './useClipboard'
import { useTaskPanel } from './useTaskPanel'
import { useToast } from './useToast'

export type ReorderWhere = 'up' | 'down' | 'top' | 'bottom'
export type PickField = 'status' | 'assignees' | 'dueDate' | 'priority'

export interface RowMenuOptions {
  variant: 'row' | 'card' | 'home' | 'subtask'
  /** Shows Move up / down (and to top / bottom on rows): Manual sort, Group by status, Member+. */
  canReorder?: boolean
  /** The visible siblings in order, for the Move items. */
  siblingIds?: () => Uuid[]
  /** Shows "Add subtask" (top-level tasks). */
  onAddSubtask?: () => void
  /** Shows "Rename" {F2}. */
  onRename?: () => void
  /** Opens a field's picker anchored to the row or card (Due date…, Assign…, Reschedule). */
  onPick?: (field: PickField) => void
}

const MOVE_LABEL: Record<ReorderWhere, string> = {
  up: 'Moved up',
  down: 'Moved down',
  top: 'Moved to top',
  bottom: 'Moved to bottom',
}

export function useTaskActions(): {
  toggleComplete(id: Uuid): Promise<void>
  setStatus(id: Uuid, statusId: Uuid): Promise<void>
  rename(id: Uuid, title: string): Promise<void>
  remove(ids: Uuid[]): void
  copyLink(id: Uuid): Promise<void>
  openMoveDialog(ids: Uuid[]): void
  reorder(id: Uuid, where: ReorderWhere, siblingIds: Uuid[]): Promise<void>
  rowMenuItems(t: Task, o: RowMenuOptions): MenuItem[]
} {
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const selection = useSelection()
  const ui = useUi()
  const toast = useToast()
  const announce = useAnnouncer()
  const clipboard = useClipboard()
  const panel = useTaskPanel()
  const { coarse } = useBreakpoint()

  /** Complete or reopen, with the undo toast and the completion hold. */
  function toggleComplete(id: Uuid): Promise<void> {
    return tasks.toggleComplete(id)
  }

  /** A status picker change: no toast (9.2). */
  function setStatus(id: Uuid, statusId: Uuid): Promise<void> {
    const t = tasks.get(id)
    if (!t || t.statusId === statusId) return Promise.resolve()
    return handled(tasks.update(id, { statusId }, { undo: false }).then(noop))
  }

  /** A blank or unchanged title is not saved. */
  function rename(id: Uuid, title: string): Promise<void> {
    const t = tasks.get(id)
    const next = title.trim()
    if (!t || !next || next === t.title) return Promise.resolve()
    return handled(tasks.update(id, { title: next }, { undo: false }).then(noop))
  }

  function remove(ids: Uuid[]): void {
    tasks.remove(ids)
  }

  /** Copies <origin>/t/<id> and toasts "Link copied". */
  async function copyLink(id: Uuid): Promise<void> {
    const ok = await clipboard.copy(`${window.location.origin}/t/${id}`)
    if (ok) toast.success('Link copied')
    else toast.show({ message: "Couldn't copy the link.", tone: 'danger' })
  }

  /** Opens "Move N tasks" for top-level tasks only. */
  function openMoveDialog(ids: Uuid[]): void {
    const top = ids.filter((id) => {
      const t = tasks.get(id)
      return !!t && t.parentTaskId === null
    })
    if (top.length) ui.openMoveTasks(top)
  }

  /**
   * Moves a task among its visible siblings (Alt+Up/Down and the Move menu items): sends the new
   * neighbours with tasks.move (no toast) and announces "Moved up, 2 of 5".
   */
  function reorder(id: Uuid, where: ReorderWhere, siblingIds: Uuid[]): Promise<void> {
    const t = tasks.get(id)
    const i = siblingIds.indexOf(id)
    if (!t || i === -1) return Promise.resolve()
    const last = siblingIds.length - 1
    if ((where === 'up' || where === 'top') && i === 0) return Promise.resolve()
    if ((where === 'down' || where === 'bottom') && i === last) return Promise.resolve()
    const rest = siblingIds.filter((x) => x !== id)
    const target = where === 'up' ? i - 1 : where === 'down' ? i + 1 : where === 'top' ? 0 : rest.length
    const beforeTaskId = rest[target - 1] ?? null
    const afterTaskId = rest[target] ?? null
    announce(`${MOVE_LABEL[where]}, ${target + 1} of ${siblingIds.length}`)
    return handled(tasks.move(id, { statusId: t.statusId, beforeTaskId, afterTaskId }, { undo: false }).then(noop))
  }

  function moveToStatus(t: Task, statusId: Uuid): void {
    if (t.statusId === statusId) return
    const name = workspaces.statusById(statusId)?.name ?? 'another status'
    void tasks.update(t.id, { statusId }, { undo: `Moved to ${name}` }).catch(noop)
  }

  function startSelecting(t: Task): void {
    if (selection.listId !== t.listId) selection.setList(t.listId)
    selection.mode = true
    if (!selection.has(t.id)) selection.toggle(t.id)
  }

  function rowMenuItems(t: Task, o: RowMenuOptions): MenuItem[] {
    const variant = o.variant
    const items: MenuItem[] = [{ id: 'open', label: 'Open', onSelect: () => panel.open(t.id) }]
    if (variant === 'subtask') {
      if (workspaces.canEdit() && !tasks.isPendingDelete(t.id)) {
        items.push({ kind: 'separator', id: 'sep-delete' })
        items.push({ id: 'delete', label: 'Delete', icon: Trash2, danger: true, onSelect: () => remove([t.id]) })
      }
      return items
    }
    if (variant !== 'card') {
      items.push({
        id: 'open-page',
        label: 'Open full page',
        icon: Maximize2,
        onSelect: () => void appRouter()?.push(`/t/${t.id}`).catch(noop),
      })
    }
    items.push({ id: 'copy-link', label: 'Copy link', icon: Link, onSelect: () => void copyLink(t.id) })
    // Guests, and tasks being deleted, get only the read-only items (9.1 rule 12, 9.4).
    if (!workspaces.canEdit() || tasks.isPendingDelete(t.id)) return items

    const top = t.parentTaskId === null
    const done = tasks.isDone(t)
    items.push({ kind: 'separator', id: 'sep-actions' })
    items.push({
      id: 'complete',
      label: done ? 'Reopen' : 'Mark complete',
      icon: done ? Undo2 : CircleCheck,
      kbd: 'Mod+Enter',
      onSelect: () => void toggleComplete(t.id),
    })
    if (o.onRename) items.push({ id: 'rename', label: 'Rename', icon: Pencil, kbd: 'F2', onSelect: o.onRename })
    if (variant === 'home' && o.onPick) {
      const pick = o.onPick
      items.push({ id: 'reschedule', label: 'Reschedule', icon: CalendarClock, kbd: 'D', onSelect: () => pick('dueDate') })
    }
    if (variant === 'card') {
      items.push({
        kind: 'submenu',
        id: 'move-status',
        label: 'Move to status',
        items: workspaces.statusesFor(t.listId).map((s) => ({
          kind: 'radio' as const,
          id: `status-${s.id}`,
          label: s.name,
          checked: s.id === t.statusId,
          onSelect: () => moveToStatus(t, s.id),
        })),
      })
    }
    if (variant !== 'card' && top && o.onAddSubtask) {
      items.push({ id: 'add-subtask', label: 'Add subtask', icon: ListTree, onSelect: o.onAddSubtask })
    }
    if (variant !== 'card' && top) {
      items.push({ id: 'move-list', label: 'Move to list…', icon: FolderInput, onSelect: () => openMoveDialog([t.id]) })
    }

    if (o.canReorder) {
      const siblings = o.siblingIds?.() ?? []
      const i = siblings.indexOf(t.id)
      const first = i <= 0
      const last = i === -1 || i === siblings.length - 1
      const move = (where: ReorderWhere) => () => void reorder(t.id, where, o.siblingIds?.() ?? siblings)
      items.push({ id: 'move-up', label: 'Move up', icon: ArrowUp, kbd: 'Alt+ArrowUp', disabled: first, onSelect: move('up') })
      items.push({ id: 'move-down', label: 'Move down', icon: ArrowDown, kbd: 'Alt+ArrowDown', disabled: last, onSelect: move('down') })
      if (variant !== 'card') {
        items.push({ id: 'move-top', label: 'Move to top', icon: ArrowUpToLine, disabled: first, onSelect: move('top') })
        items.push({ id: 'move-bottom', label: 'Move to bottom', icon: ArrowDownToLine, disabled: last, onSelect: move('bottom') })
      }
    }

    if (variant === 'card') {
      if (o.onPick) {
        const pick = o.onPick
        items.push({ id: 'due-date', label: 'Due date…', icon: Calendar, kbd: 'D', onSelect: () => pick('dueDate') })
        items.push({ id: 'assign', label: 'Assign…', icon: UserRoundPlus, kbd: 'A', onSelect: () => pick('assignees') })
      }
      if (top) items.push({ id: 'move-list', label: 'Move to list…', icon: FolderInput, onSelect: () => openMoveDialog([t.id]) })
    }

    if (variant === 'row' && top && coarse.value) {
      items.push({ id: 'select', label: 'Select', icon: SquareCheck, onSelect: () => startSelecting(t) })
    }

    items.push({ kind: 'separator', id: 'sep-delete' })
    items.push({ id: 'delete', label: 'Delete', icon: Trash2, kbd: 'Delete', danger: true, onSelect: () => remove([t.id]) })
    return items
  }

  return { toggleComplete, setStatus, rename, remove, copyLink, openMoveDialog, reorder, rowMenuItems }
}

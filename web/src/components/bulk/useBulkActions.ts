// The bulk bar's actions (8.7). Each edit is one tasks.bulkUpdate, so it shows one "Updated N tasks"
// toast whose Undo restores every task's previous values (with `position` when the status changed);
// the store sends more than 500 items in chunks (9.2).
// - Setting or adding a value covers every selected task, so the toast counts the selection; nothing
//   is sent when no task would change. Removing a person or a tag covers the tasks that have it (the
//   menu said "On N tasks").
// - Re-statused tasks go to the top of their new status one after another, so they are sent from the
//   last (in status, then manual order) to the first and keep their order there.
// - The selection stays after an edit; Move and Delete take their tasks out of it (the store does
//   that), which empties it.
import { computed, type ComputedRef } from 'vue'
import { noop } from '../../lib/async'
import { compareManual } from '../../lib/taskQuery'
import { useSelection } from '../../stores/selection'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { BulkTaskUpdate, IsoDate, Priority, Task, Uuid } from '../../types'

export interface BulkActions {
  /** The selected tasks that are loaded and not being deleted, in selection order. */
  selected: ComputedRef<Task[]>
  setStatus(statusId: Uuid): void
  addAssignee(userId: Uuid): void
  removeAssignee(userId: Uuid): void
  unassignAll(): void
  /** A start date that would fall after the new due date moves to it too. */
  setDueDate(date: IsoDate | null): void
  setPriority(priority: Priority): void
  addTag(tagId: Uuid): void
  removeTag(tagId: Uuid): void
  move(): void
  remove(): void
}

export function useBulkActions(): BulkActions {
  const selection = useSelection()
  const tasks = useTasks()
  const ui = useUi()
  const workspaces = useWorkspaces()

  const selected = computed<Task[]>(() => {
    const out: Task[] = []
    for (const id of selection.ids) {
      const t = tasks.get(id)
      if (t && !tasks.isPendingDelete(id)) out.push(t)
    }
    return out
  })

  function send(updates: BulkTaskUpdate[]): void {
    if (updates.length) void tasks.bulkUpdate(updates).catch(noop)
  }

  /** Every selected task, when the edit changes at least one of them. */
  function every(list: Task[], changes: (t: Task) => boolean, update: (t: Task) => BulkTaskUpdate): void {
    if (list.some(changes)) send(list.map(update))
  }

  /** Only the selected tasks the edit changes. */
  function only(changes: (t: Task) => boolean, update: (t: Task) => BulkTaskUpdate): void {
    send(selected.value.filter(changes).map(update))
  }

  /** The selection from its last task to its first, by status and then manual order. */
  function lastFirst(): Task[] {
    const rank = new Map<Uuid, number>()
    for (const t of selected.value) {
      if (rank.has(t.statusId)) continue
      const i = workspaces.statusesByPosition(t.listId).findIndex((s) => s.id === t.statusId)
      rank.set(t.statusId, i < 0 ? Number.MAX_SAFE_INTEGER : i)
    }
    return [...selected.value]
      .sort((a, b) => (rank.get(a.statusId) ?? 0) - (rank.get(b.statusId) ?? 0) || compareManual(a, b))
      .reverse()
  }

  return {
    selected,
    setStatus: (statusId) =>
      every(
        lastFirst(),
        (t) => t.statusId !== statusId,
        (t) => ({ id: t.id, statusId }),
      ),
    addAssignee: (userId) =>
      every(
        selected.value,
        (t) => !t.assigneeIds.includes(userId),
        (t) => ({ id: t.id, addAssigneeIds: [userId] }),
      ),
    removeAssignee: (userId) =>
      only(
        (t) => t.assigneeIds.includes(userId),
        (t) => ({ id: t.id, removeAssigneeIds: [userId] }),
      ),
    unassignAll: () =>
      every(
        selected.value,
        (t) => t.assigneeIds.length > 0,
        (t) => ({ id: t.id, assigneeIds: [] }),
      ),
    setDueDate: (date) =>
      every(
        selected.value,
        (t) => t.dueDate !== date,
        (t) => {
          const u: BulkTaskUpdate = { id: t.id, dueDate: date }
          if (date && t.startDate && t.startDate > date) u.startDate = date
          return u
        },
      ),
    setPriority: (priority) =>
      every(
        selected.value,
        (t) => t.priority !== priority,
        (t) => ({ id: t.id, priority }),
      ),
    addTag: (tagId) =>
      every(
        selected.value,
        (t) => !t.tagIds.includes(tagId),
        (t) => ({ id: t.id, addTagIds: [tagId] }),
      ),
    removeTag: (tagId) =>
      only(
        (t) => t.tagIds.includes(tagId),
        (t) => ({ id: t.id, removeTagIds: [tagId] }),
      ),
    move: () => {
      const ids = selected.value.filter((t) => t.parentTaskId === null).map((t) => t.id)
      if (ids.length) ui.openMoveTasks(ids)
    },
    remove: () => {
      const ids = selected.value.map((t) => t.id)
      if (ids.length) tasks.remove(ids)
    },
  }
}

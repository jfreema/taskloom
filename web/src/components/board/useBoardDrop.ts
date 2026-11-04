// Where a dragged card lands on the Board (8.8). resolveDrop turns a drop point (a column's top or
// end, or a card's top or bottom edge) into the visible cards above and below it, or null when the
// drop can't happen (a subtask, another list, a pending delete) or would leave the card where it
// is. drop sends tasks.move with those neighbours; the server places the card next to its true
// siblings (5.5), so cards hidden by filters or the column caps never collide. A dropped card is
// "placed" in the model: it skips the completion hold and the caps where it landed.
import type { ComputedRef } from 'vue'
import type { DragData } from '../../composables/useDnd'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import type { DropAt, DropPlace } from './boardContext'
import type { BoardModel } from './useBoardModel'

export interface BoardDrop {
  resolveDrop(d: DragData, statusId: Uuid, at: DropAt): DropPlace | null
  drop(d: DragData, statusId: Uuid, at: DropAt): void
}

export function useBoardDrop(model: BoardModel, listId: ComputedRef<Uuid>): BoardDrop {
  const tasks = useTasks()

  function resolveDrop(d: DragData, statusId: Uuid, at: DropAt): DropPlace | null {
    const t = tasks.get(d.id)
    if (!t || t.parentTaskId !== null || d.listId !== listId.value || tasks.isPendingDelete(t.id)) return null
    const col = model.columns.value.find((c) => c.status.id === statusId)
    if (!col) return null
    const current = col.collapsed ? [] : col.shown.map((x) => x.id)
    const ids = current.filter((id) => id !== d.id)
    let index: number
    if (at === 'start') index = 0
    else if (at === 'end') index = ids.length
    else {
      if (at.cardId === d.id) return null
      const i = ids.indexOf(at.cardId)
      if (i === -1) return null
      index = at.edge === 'top' ? i : i + 1
    }
    const place = { beforeTaskId: ids[index - 1] ?? null, afterTaskId: ids[index] ?? null }
    if (t.statusId === statusId) {
      if (col.collapsed) return null
      const i0 = current.indexOf(d.id)
      if (i0 !== -1 && (current[i0 - 1] ?? null) === place.beforeTaskId && (current[i0 + 1] ?? null) === place.afterTaskId) {
        return null
      }
    }
    return place
  }

  function drop(d: DragData, statusId: Uuid, at: DropAt): void {
    const place = resolveDrop(d, statusId, at)
    if (!place) return
    model.placed(d.id)
    void tasks.move(d.id, { statusId, ...place }).catch(noop)
  }

  return { resolveDrop, drop }
}

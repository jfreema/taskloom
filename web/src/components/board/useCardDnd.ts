// A Board card as a drag source and a drop target (8.8, useDnd):
// - The whole card drags with the lifted preview, except its controls (useDnd ignores a drag that
//   starts on one). Dragging is off on phones, for guests, while the card is renamed and while its
//   delete is pending. `dragging` is true from the start to the end, for the dashed placeholder.
// - As a target it reports the closest edge (top or bottom) to the board, which tints the column,
//   and `indicator` says where to draw the drop line: at that edge, unless the drop would leave
//   the dragged card where it is, or at the column's own hint (the top of the column, or its end).
import { computed, watch, type ComputedRef, type Ref } from 'vue'
import { activeDrag, useDraggable, useDropTarget } from '../../composables/useDnd'
import { useTasks } from '../../stores/tasks'
import type { Task, Uuid } from '../../types'
import { useBoard } from './boardContext'

export type CardEdge = 'top' | 'bottom'

export interface CardDndOptions {
  task: () => Task
  /** The column the card renders in (during the completion hold, not yet the task's own status). */
  columnId: () => Uuid
  renaming: () => boolean
  /** The column's own drop line at this card. */
  dropHint: () => CardEdge | null
}

export interface CardDnd {
  dragging: Readonly<Ref<boolean>>
  indicator: ComputedRef<CardEdge | null>
}

export function useCardDnd(el: Ref<HTMLElement | null>, o: CardDndOptions): CardDnd {
  const board = useBoard()
  const tasks = useTasks()
  const id = () => o.task().id

  const { dragging } = useDraggable(el, {
    data: () => ({ kind: 'task', id: id(), listId: o.task().listId, fromStatusId: o.task().statusId }),
    preview: 'lift',
    disabled: () => !board.canDrag.value || o.renaming() || tasks.isPendingDelete(id()),
  })

  const { edge } = useDropTarget(el, {
    data: () => ({ type: 'card', id: id() }),
    accepts: (d) => board.canDrag.value && d.listId === board.listId.value,
    edges: ['top', 'bottom'],
    onDrop: (d, e) => board.drop(d, o.columnId(), { cardId: id(), edge: e === 'top' ? 'top' : 'bottom' }),
  })

  watch(edge, (e) => {
    if (e) board.hover.value = { id: id(), statusId: o.columnId() }
    else if (board.hover.value?.id === id()) board.hover.value = null
  })

  const indicator = computed<CardEdge | null>(() => {
    const hint = o.dropHint()
    if (hint) return hint
    const e = edge.value
    const d = activeDrag.value
    if (!d || (e !== 'top' && e !== 'bottom')) return null
    return board.resolveDrop(d, o.columnId(), { cardId: id(), edge: e }) ? e : null
  })

  return { dragging, indicator }
}

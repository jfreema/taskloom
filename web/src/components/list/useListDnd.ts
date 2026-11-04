// Dragging rows in the List (8.7, 9.3), through useDnd only. Enabled with Manual sort, Group by
// status, Member+ and wider than a phone; only top-level rows drag, by their grip.
// - Every row is a drop target with top and bottom edges, but neighbours are always top-level
//   tasks: a subtask row (or a parent's bottom half) means "after the parent", and the indicator
//   then sits under the parent's last subtask row.
// - An expanded group header drops at the top of its group, the group's last row (its add row or
//   "Show N more") at the bottom; collapsed headers aren't targets.
// - A drop calls tasks.move with the visible neighbours; the store toasts "Moved to <status>" with
//   Undo when the status changes. A drop that changes nothing shows no indicator and sends nothing.
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { activeDrag, useDraggable, useDropTarget, type DragData, type Edge } from '../../composables/useDnd'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import { headerKey, type GroupView, type ListModel } from './useListModel'

export interface DropIndicator {
  /** The row that draws the line. */
  key: string
  edge: 'top' | 'bottom'
  groupKey: string
}

/** What a drop target stands for. */
export type DropTargetInfo =
  | { kind: 'task'; key: string; taskId: Uuid; level: 1 | 2; parentId: Uuid | null; groupKey: string }
  | { kind: 'subtask-add'; key: string; parentId: Uuid; groupKey: string }
  | { kind: 'header'; key: string; groupKey: string }
  | { kind: 'end'; key: string; groupKey: string }

interface Spot {
  groupKey: string
  statusId: Uuid
  aboveId: Uuid | null
  belowId: Uuid | null
  indicator: DropIndicator
}

export interface ListDnd {
  enabled: ComputedRef<boolean>
  indicator: Ref<DropIndicator | null>
  /** A task row: draggable by its grip when top-level, and a drop target. */
  row(el: Ref<HTMLElement | null>, grip: Ref<HTMLElement | null>, info: () => DropTargetInfo & { kind: 'task' }, disabled: () => boolean): { dragging: Ref<boolean> }
  /** A group header, a subtask add row, or a group's last row. */
  target(el: Ref<HTMLElement | null>, info: () => DropTargetInfo, active: () => boolean): void
}

export function useListDnd(o: { listId: ComputedRef<Uuid>; model: ListModel; enabled: ComputedRef<boolean> }): ListDnd {
  const tasks = useTasks()
  const indicator = ref<DropIndicator | null>(null)
  /** The target that set the indicator, so a late leave from another row doesn't clear it. */
  let owner: string | null = null

  /** The last row of a top-level task's block: itself, its subtasks, its subtask add row. */
  function blockEnd(g: GroupView, topId: Uuid): string {
    const rows = g.rows
    const i = rows.findIndex((r) => r.kind === 'task' && r.task.id === topId)
    if (i < 0) return `t:${topId}`
    let last = rows[i].key
    for (let j = i + 1; j < rows.length; j++) {
      const r = rows[j]
      if ((r.kind === 'task' && r.level === 2) || (r.kind === 'add' && r.parentId === topId)) last = r.key
      else break
    }
    return last
  }

  function topRows(g: GroupView): Uuid[] {
    const out: Uuid[] = []
    for (const r of g.rows) if (r.kind === 'task' && r.level === 1) out.push(r.task.id)
    return out
  }

  /** Where a drop on this target with this edge lands, or null when it changes nothing. */
  function resolve(info: DropTargetInfo, edge: Edge | null, draggedId: Uuid): Spot | null {
    const g = o.model.groupByKey.value.get(info.groupKey)
    const statusId = g?.group.status?.id
    if (!g || !statusId || g.collapsed) return null
    const all = topRows(g)
    const tops = all.filter((id) => id !== draggedId)
    let aboveId: Uuid | null = null
    let belowId: Uuid | null = null
    let line: DropIndicator

    const after = (id: Uuid) => {
      const i = tops.indexOf(id)
      aboveId = id
      belowId = i >= 0 ? (tops[i + 1] ?? null) : null
      line = { key: blockEnd(g, id), edge: 'bottom', groupKey: g.key }
    }

    switch (info.kind) {
      case 'task': {
        const topId = info.level === 1 ? info.taskId : info.parentId
        if (!topId || topId === draggedId) return null
        if (info.level === 1 && edge === 'top') {
          const i = tops.indexOf(topId)
          aboveId = i > 0 ? tops[i - 1] : null
          belowId = topId
          line = { key: info.key, edge: 'top', groupKey: g.key }
        } else {
          after(topId)
        }
        break
      }
      case 'subtask-add':
        if (info.parentId === draggedId) return null
        after(info.parentId)
        break
      case 'header':
        aboveId = null
        belowId = tops[0] ?? null
        line = { key: headerKey(g.key), edge: 'bottom', groupKey: g.key }
        break
      case 'end':
        aboveId = tops[tops.length - 1] ?? null
        belowId = null
        line = { key: info.key, edge: 'top', groupKey: g.key }
        break
    }

    // Dropping a task where it already is changes nothing.
    const dragged = tasks.get(draggedId)
    if (dragged && dragged.statusId === statusId) {
      const i = all.indexOf(draggedId)
      if (i >= 0 && (all[i - 1] ?? null) === aboveId && (all[i + 1] ?? null) === belowId) return null
    }
    return { groupKey: g.key, statusId, aboveId, belowId, indicator: line! }
  }

  function track(info: DropTargetInfo, edge: Edge | null, dragged: DragData | null): void {
    if (!edge || !dragged) {
      if (owner === info.key) {
        indicator.value = null
        owner = null
      }
      return
    }
    const spot = resolve(info, edge, dragged.id)
    owner = info.key
    const next = spot?.indicator ?? null
    const cur = indicator.value
    if (next?.key !== cur?.key || next?.edge !== cur?.edge) indicator.value = next
  }

  function drop(info: DropTargetInfo, edge: Edge | null, d: DragData): void {
    indicator.value = null
    owner = null
    const spot = resolve(info, edge, d.id)
    if (!spot) return
    void tasks.move(d.id, { statusId: spot.statusId, beforeTaskId: spot.aboveId, afterTaskId: spot.belowId }).catch(noop)
  }

  function accepts(d: DragData): boolean {
    if (!o.enabled.value || d.kind !== 'task' || d.listId !== o.listId.value) return false
    const t = tasks.get(d.id)
    return !!t && t.parentTaskId === null
  }

  function row(
    el: Ref<HTMLElement | null>,
    grip: Ref<HTMLElement | null>,
    info: () => DropTargetInfo & { kind: 'task' },
    disabled: () => boolean,
  ): { dragging: Ref<boolean> } {
    const { dragging } = useDraggable(el, {
      handle: grip,
      disabled: () => !o.enabled.value || info().level !== 1 || disabled(),
      data: () => {
        const i = info()
        const d: DragData = { kind: 'task', id: i.taskId, listId: o.listId.value }
        const t = tasks.get(i.taskId)
        if (t) d.fromStatusId = t.statusId
        return d
      },
    })
    target(el, info, () => true)
    return { dragging }
  }

  function target(el: Ref<HTMLElement | null>, info: () => DropTargetInfo, active: () => boolean): void {
    const { edge } = useDropTarget(el, {
      data: () => ({ key: info().key }),
      accepts: (d) => active() && accepts(d),
      edges: ['top', 'bottom'],
      onDrop: (d, e) => drop(info(), e, d),
    })
    watch(edge, (e) => track(info(), e, activeDrag.value))
  }

  // A drag that ends anywhere (Esc, outside the list) leaves no indicator behind.
  watch(activeDrag, (d) => {
    if (!d) {
      indicator.value = null
      owner = null
    }
  })

  return { enabled: computed(() => o.enabled.value), indicator, row, target }
}

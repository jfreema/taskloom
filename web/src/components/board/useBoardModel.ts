// The Board's columns (8.8): one per status of the list in display order, each holding its visible
// top-level tasks in manual order (position). Only top-level tasks get cards.
// - With "Show completed" off (ViewConfig.showCompleted), Done columns are left out.
// - Done columns render their first 20 cards and other columns their first 50, then "Show all N",
//   which lasts for the page session (switching views keeps it; a reload ends it). A card created
//   in this page session, or put somewhere by hand (a drop, Alt+Left/Right), always renders, so it
//   never vanishes behind the cap.
// - Collapsed columns (ViewConfig.collapsedGroups `status:<id>`) render as strips, without cards.
// - Completion hold (8.1): a task completed a moment ago (tasks.recentlyCompleted) keeps its old
//   column and place for 1s with its done look, then moves to the top of the Done column, or
//   leaves when Done columns are hidden. Moves placed by hand (a drop, Alt+Left/Right) skip the
//   hold, so the card stays where it was put.
import { computed, reactive, watch, type ComputedRef } from 'vue'
import { useListPageContext } from '../../composables/useListPageContext'
import { compareManual } from '../../lib/taskQuery'
import { useTasks } from '../../stores/tasks'
import type { Status, Task, Uuid } from '../../types'

export const DONE_CAP = 20
export const OPEN_CAP = 50
/** A little longer than the store's 1s hold, so a hand-placed move never meets the end of it. */
const PLACED_MS = 1500

/** Columns opened with "Show all" in this page session, by status id (ids are unique across lists). */
const sessionExpanded = reactive(new Set<Uuid>())

export interface BoardColumnView {
  /** `status:<id>`, as in ViewConfig.collapsedGroups. */
  key: string
  status: Status
  /** A Done-category status. */
  done: boolean
  /** Every visible card of the column, in order. */
  tasks: Task[]
  /** The cards it renders (see the caps above). */
  shown: Task[]
  /** Some cards wait behind "Show all N" (N = tasks.length). */
  capped: boolean
  collapsed: boolean
}

export interface BoardModel {
  columns: ComputedRef<BoardColumnView[]>
  /** Rendered cards in reading order, column by column (the panel's J/K order). */
  order: ComputedRef<Uuid[]>
  columnOf(id: Uuid): BoardColumnView | undefined
  /** The rendered cards of the task's column, in order (Move up / down, Alt+Up/Down). */
  siblingIds(id: Uuid): Uuid[]
  showAll(statusId: Uuid): void
  setCollapsed(statusId: Uuid, collapsed: boolean): void
  /**
   * Marks a move the user placed by hand: the completion hold doesn't hold the card back, and the
   * card renders even past its column's cap.
   */
  placed(id: Uuid): void
}

export function statusKey(statusId: Uuid): string {
  return `status:${statusId}`
}

export function useBoardModel(): BoardModel {
  const ctx = useListPageContext()
  const tasks = useTasks()

  const expanded = sessionExpanded
  /** Moves placed by hand in the last moment (they skip the completion hold). */
  const placedIds = reactive(new Set<Uuid>())
  /** Cards placed by hand while the board is open (they skip the caps). */
  const movedIds = reactive(new Set<Uuid>())
  /** Where each card was last rendered, for the completion hold. Not reactive. */
  const placements = new Map<Uuid, { statusId: Uuid; index: number }>()

  watch(
    () => ctx.listId.value,
    () => {
      placedIds.clear()
      movedIds.clear()
      placements.clear()
    },
  )

  const columns = computed<BoardColumnView[]>(() => {
    const cfg = ctx.config.value
    const visible = ctx.result.value.visibleIds
    const created = ctx.createdThisSession.value
    const held = tasks.recentlyCompleted
    const statuses = ctx.statuses.value
    if (!statuses.length) return []

    const byStatus = new Map<Uuid, Task[]>(statuses.map((s) => [s.id, []]))
    const fallback = byStatus.get(statuses[0].id) ?? []
    for (const t of ctx.allTasks.value) {
      if (t.parentTaskId !== null || !visible.has(t.id)) continue
      const bucket = byStatus.get(t.statusId) ?? fallback
      bucket.push(t)
    }
    for (const list of byStatus.values()) list.sort(compareManual)

    // Completion hold: back to the column and place it had before it was completed.
    for (const id of held) {
      if (placedIds.has(id)) continue
      const memo = placements.get(id)
      const t = tasks.get(id)
      if (!memo || !t || t.statusId === memo.statusId) continue
      const from = byStatus.get(t.statusId)
      const to = byStatus.get(memo.statusId)
      if (!from || !to) continue
      const i = from.findIndex((x) => x.id === id)
      if (i === -1) continue
      from.splice(i, 1)
      to.splice(Math.min(memo.index, to.length), 0, t)
    }
    for (const [statusId, list] of byStatus) {
      list.forEach((t, index) => {
        if (!held.has(t.id) || placedIds.has(t.id)) placements.set(t.id, { statusId, index })
      })
    }

    const out: BoardColumnView[] = []
    for (const status of statuses) {
      const done = status.category === 'Done'
      if (done && !cfg.showCompleted) continue
      const key = statusKey(status.id)
      const list = byStatus.get(status.id) ?? []
      const cap = done ? DONE_CAP : OPEN_CAP
      let shown = list
      if (list.length > cap && !expanded.has(status.id)) {
        shown = list.filter((t, i) => i < cap || created.has(t.id) || movedIds.has(t.id))
      }
      out.push({
        key,
        status,
        done,
        tasks: list,
        shown,
        capped: shown.length < list.length,
        collapsed: cfg.collapsedGroups[key] === true,
      })
    }
    return out
  })

  const order = computed<Uuid[]>(() => columns.value.flatMap((c) => (c.collapsed ? [] : c.shown.map((t) => t.id))))

  function columnOf(id: Uuid): BoardColumnView | undefined {
    return columns.value.find((c) => c.shown.some((t) => t.id === id))
  }

  function siblingIds(id: Uuid): Uuid[] {
    return columnOf(id)?.shown.map((t) => t.id) ?? []
  }

  function showAll(statusId: Uuid): void {
    expanded.add(statusId)
  }

  function setCollapsed(statusId: Uuid, collapsed: boolean): void {
    const key = statusKey(statusId)
    const next = { ...ctx.config.value.collapsedGroups }
    if (collapsed) next[key] = true
    else delete next[key]
    ctx.config.value.collapsedGroups = next
  }

  function placed(id: Uuid): void {
    placedIds.add(id)
    movedIds.add(id)
    setTimeout(() => placedIds.delete(id), PLACED_MS)
  }

  return { columns, order, columnOf, siblingIds, showAll, setCollapsed, placed }
}

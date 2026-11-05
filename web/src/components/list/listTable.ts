// What the List table shares with its groups and rows (8.7). Rows read the slow-changing parts here
// (columns, permissions, the drag state) and get the fast-changing ones (tab stop, selection,
// the open task) as props, so a key press or a click re-renders only the rows it changes.
import { inject, provide, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { PickField } from '../../composables/useTaskActions'
import type { ColumnKey } from '../../lib/taskQuery'
import type { Uuid } from '../../types'
import type { ListModel } from './useListModel'
import type { ListDnd } from './useListDnd'

export interface ListColumns extends Record<ColumnKey, boolean> {}

export interface ListTableContext {
  listId: ComputedRef<Uuid>
  /** Member or above (9.4). */
  canEdit: ComputedRef<boolean>
  /** The optional columns shown; Status is off while grouped by status. */
  columns: ComputedRef<ListColumns>
  /** Cells per row, for the spanning cells' aria-colspan. */
  colCount: ComputedRef<number>
  /** Checkboxes are rendered: Member+, not on phones, and on touch screens only after "Select". */
  selectable: ComputedRef<boolean>
  /** Manual sort, Group by status, Member+ (Alt+arrows, the Move items, dragging). */
  canReorder: ComputedRef<boolean>
  /** Phone rows (two lines, 8.7): avatars show at most 2. */
  phoneRows: Ref<boolean>
  model: ListModel
  dnd: ListDnd
  /** The tab stop's key (roving tabindex), `t:<id>`, `h:<group>`, `a:<group>`… */
  tabStop: ComputedRef<string | null>
  /** The task being renamed in place. */
  renamingId: Ref<Uuid | null>
  /** Tasks created a moment ago in this view: their rows flash once. */
  flashIds: Ref<ReadonlySet<Uuid>>
  /** The row just followed into another group (a status change from the row): highlighted for 1s. */
  movedKey: Readonly<Ref<string | null>>
  openTask(id: Uuid): void
  /** Checkbox click: Shift selects the range from the anchor in visible order. */
  check(id: Uuid, shift: boolean): void
  startRename(id: Uuid): void
  endRename(): void
  rename(id: Uuid, title: string): void
  /**
   * A status change from the row (picker or menu): the row keeps focus in its new place, and one
   * that lands in another group scrolls into view, highlighted.
   */
  setStatus(id: Uuid, statusId: Uuid): void
  /** Opens the add row under a top-level task's subtasks. */
  addSubtask(parentId: Uuid): void
  /** Opens a field's picker for a row: its cell's trigger, else one anchored to the row. */
  pick(id: Uuid, field: PickField): void
  /**
   * A task was created from an add row (its row flashes). With `where`, under Manual sort it stays
   * where it was added until the grouping, the sort or the list changes.
   */
  created(id: Uuid, where?: { groupKey: string; placement: 'Top' | 'Bottom'; parentId: Uuid | null }): void
}

const KEY: InjectionKey<ListTableContext> = Symbol('ListTable')

export function provideListTable(ctx: ListTableContext): void {
  provide(KEY, ctx)
}

export function useListTable(): ListTableContext {
  const ctx = inject(KEY, null)
  if (!ctx) throw new Error('useListTable() must be called inside ListTable.')
  return ctx
}

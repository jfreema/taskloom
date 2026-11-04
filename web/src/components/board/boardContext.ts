// What the Board's parts share (8.8). BoardView provides it; columns and cards inject it.
import { inject, provide, type ComputedRef, type InjectionKey, type Ref, type ShallowRef } from 'vue'
import type { DragData } from '../../composables/useDnd'
import type { PickField } from '../../composables/useTaskActions'
import type { Uuid } from '../../types'
import type { BoardModel } from './useBoardModel'

/** Where a drop lands: next to a card (its closest edge), or the start or end of a column. */
export type DropAt = { cardId: Uuid; edge: 'top' | 'bottom' } | 'start' | 'end'

/** The neighbours a drop sends to tasks.move. */
export interface DropPlace {
  beforeTaskId: Uuid | null
  afterTaskId: Uuid | null
}

/** A picker opened without a visible trigger, anchored to the card. */
export interface PickTarget {
  id: Uuid
  field: PickField
  anchor: HTMLElement
}

/** The card under the pointer during a drag (the innermost drop target). */
export interface CardHover {
  id: Uuid
  statusId: Uuid
}

export interface BoardContext {
  listId: ComputedRef<Uuid>
  canEdit: ComputedRef<boolean>
  /** Members and above, wider than a phone (9.3). */
  canDrag: ComputedRef<boolean>
  model: BoardModel
  /** The task open in the panel. */
  openId: ComputedRef<Uuid | null>
  /** Cards kept visible although filters hide them ("Hidden by filters"). */
  keptIds: ComputedRef<ReadonlySet<Uuid>>
  /** Parents shown only because one of their subtasks matches the filters (muted, 8.1). */
  contextIds: ComputedRef<ReadonlySet<Uuid>>
  renamingId: Ref<Uuid | null>
  /** Cards just created here, which flash once. */
  flashIds: ShallowRef<ReadonlySet<Uuid>>
  /** The card that is the innermost drop target, if any. */
  hover: ShallowRef<CardHover | null>
  openTask(id: Uuid): void
  /** Opens a field's picker anchored to the card (S, A, D, P and the card menu). */
  pick(id: Uuid, field: PickField): void
  startRename(id: Uuid): void
  endRename(id: Uuid): void
  rename(id: Uuid, title: string): void
  /** An inline add created this task. */
  created(id: Uuid): void
  /** The neighbours a drop would send, or null when it would change nothing. */
  resolveDrop(d: DragData, statusId: Uuid, at: DropAt): DropPlace | null
  drop(d: DragData, statusId: Uuid, at: DropAt): void
}

const KEY: InjectionKey<BoardContext> = Symbol('board')

export function provideBoard(c: BoardContext): void {
  provide(KEY, c)
}

export function useBoard(): BoardContext {
  const c = inject(KEY, null)
  if (!c) throw new Error('useBoard() is only available inside BoardView')
  return c
}

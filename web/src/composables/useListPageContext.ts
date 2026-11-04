// The list page's shared state (10.2), provided by ListPage and read by its views, the toolbar
// controls and the bulk bar.
import { inject, provide, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { FilterResult, TaskFilters, ViewConfig } from '../lib/taskQuery'
import type { CreateTaskRequest, ListSummary, Space, Status, Task, Uuid, ViewKind } from '../types'

export interface ListPageContext {
  listId: ComputedRef<Uuid>
  view: ComputedRef<ViewKind>
  list: ComputedRef<ListSummary | null>
  space: ComputedRef<Space | null>
  /** Display order. */
  statuses: ComputedRef<Status[]>
  canEdit: ComputedRef<boolean>
  /** Every task of the list, pending deletes excluded. */
  allTasks: ComputedRef<Task[]>
  /** Shared by the three views, persisted. */
  filters: Ref<TaskFilters>
  /** Transient. */
  search: Ref<string>
  /** This user, list and view; persisted on change. */
  config: Ref<ViewConfig>
  /** keepVisibleIds (8.1); cleared when the filters change. */
  createdThisSession: Ref<Set<Uuid>>
  result: ComputedRef<FilterResult>
  /** filterDefaults(filters), also published as ui.createDefaults. */
  createDefaults: ComputedRef<Partial<CreateTaskRequest>>
  loading: ComputedRef<boolean>
  error: ComputedRef<unknown>
  reload(): Promise<void>
  /** Views teleport toolbar controls here: <Teleport defer :to="'#' + toolbarEndId">. */
  toolbarEndId: string
}

const KEY: InjectionKey<ListPageContext> = Symbol('ListPageContext')

export function provideListPageContext(ctx: ListPageContext): void {
  provide(KEY, ctx)
}

/** The context of the enclosing ListPage. Throws outside one. */
export function useListPageContext(): ListPageContext {
  const ctx = inject(KEY, null)
  if (!ctx) throw new Error('useListPageContext() must be called inside ListPage.')
  return ctx
}

/** The context, or null outside a list page (for components that also render elsewhere). */
export function useOptionalListPageContext(): ListPageContext | null {
  return inject(KEY, null)
}

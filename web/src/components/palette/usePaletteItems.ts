// What the palette lists (8.11). An empty query shows Recent (5 tasks, 3 lists of this workspace)
// and every action, with up to 5 "Switch to <workspace>" items (typing finds the others); a typed
// one shows the server's tasks (8, or up to 20 after "Show all N results"), the matching lists and
// spaces (6 together), the matching actions (6) and, last, "Create task “…”" (the actions come from
// usePaletteActions). Every option knows how to activate itself; all but "Show all" and "Try again"
// close the palette first. While the palette is open (`active`), the local index (the list, space
// and action names it matches) is folded in idle time, so the first keystroke only compares strings.
import { computed, onScopeDispose, watch, type Component, type ComputedRef, type Ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { CircleAlert, ChevronDown, History, Plus } from '@lucide/vue'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { noop } from '../../lib/async'
import { useRecents } from '../../stores/recents'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { IsoDate, Space, Status, Task, Uuid } from '../../types'
import { matches, prepare, startsWord } from './paletteMatch'
import { usePaletteActions, type PaletteAction } from './usePaletteActions'
import type { PaletteSearch } from './usePaletteSearch'

export const TASKS_SHOWN = 8

/** "Switch to <workspace>" items on an empty query: someone in many workspaces types to find the others. */
const SWITCHES_SHOWN = 5
const LOCAL_MAX = 6
const RECENT_TASKS = 5
const RECENT_LISTS = 3

export type PaletteOptionKind = 'task' | 'list' | 'space' | 'action' | 'more' | 'create' | 'retry'

export interface PaletteOption {
  /** Unique within the palette and safe in a DOM id. */
  key: string
  kind: PaletteOptionKind
  label: string
  /** Whether the label shows the query's match in weight 600. */
  highlight: boolean
  /** The location: "Work / Admin" for tasks, the space for lists. */
  meta?: string
  icon?: Component
  status?: Status
  space?: Space
  due?: IsoDate | null
  done?: boolean
  kbd?: string
  /** Theme items: the current choice. */
  current?: boolean
  /** Keeps the palette open (Show all, Try again). */
  stay?: boolean
  run(o: { mod: boolean }): void
}

export interface PaletteGroup {
  key: 'recent' | 'tasks' | 'lists' | 'spaces' | 'actions'
  label: string
  options: PaletteOption[]
}

/** Runs `fn` once the browser is idle (within 200ms at the latest); returns a cancel. */
function whenIdle(fn: () => void): () => void {
  if (typeof requestIdleCallback === 'function') {
    const id = requestIdleCallback(fn, { timeout: 200 })
    return () => cancelIdleCallback(id)
  }
  const id = setTimeout(fn, 0)
  return () => clearTimeout(id)
}

export function usePaletteItems(o: {
  query: Ref<string>
  expanded: Ref<boolean>
  search: PaletteSearch
  close(): void
  /** The palette is open: its local index is prepared then. */
  active?: Ref<boolean>
}): { groups: ComputedRef<PaletteGroup[]>; noResults: ComputedRef<boolean>; trimmed: ComputedRef<string> } {
  const ui = useUi()
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const recents = useRecents()
  const panel = useTaskPanel()
  const router = useRouter()
  const route = useRoute()
  const actions = usePaletteActions()

  const trimmed = computed(() => o.query.value.trim())

  // ---- Local index ----

  /** The texts a typed query matches locally: list and space names, action labels and keywords. */
  function localTexts(): string[] {
    const out: string[] = []
    for (const { list } of workspaces.allLists) out.push(list.name)
    for (const space of workspaces.current?.spaces ?? []) out.push(space.name)
    for (const a of actions.value) {
      out.push(a.label)
      if (a.keywords) out.push(a.keywords)
    }
    return out
  }

  let cancelPrepare: (() => void) | null = null
  if (o.active) {
    watch(
      o.active,
      (on) => {
        cancelPrepare?.()
        cancelPrepare = on ? whenIdle(() => prepare(localTexts())) : null
      },
      { immediate: true },
    )
    onScopeDispose(() => cancelPrepare?.())
  }

  function go(path: string): void {
    void router.push(path).catch(noop)
  }

  /** Close first, so focus is back on the opener before the next layer or the panel takes it. */
  function closing(fn: () => void): () => void {
    return () => {
      o.close()
      fn()
    }
  }

  function pathOf(listId: Uuid): string | undefined {
    const loc = workspaces.findList(listId)
    return loc ? `${loc.space.name} / ${loc.list.name}` : undefined
  }

  function openTask(t: Pick<Task, 'id'> & Partial<Task>, mod: boolean): void {
    o.close()
    // A search result may come from a list this page never loaded: the panel can show it at once.
    if (t.listId && t.statusId && !tasks.get(t.id)) tasks.upsert(t as Task)
    const panelRoute = route.name === 'home' || route.name === 'list'
    if (!mod && panelRoute) panel.open(t.id)
    else go(`/t/${t.id}`)
  }

  function taskOption(t: Task): PaletteOption {
    return {
      key: `task-${t.id}`,
      kind: 'task',
      label: t.title,
      highlight: true,
      meta: pathOf(t.listId),
      status: workspaces.statusById(t.statusId),
      due: t.dueDate,
      done: tasks.isDone(t),
      run: ({ mod }) => openTask(t, mod),
    }
  }

  function listOption(listId: Uuid, highlight: boolean, keyPrefix = 'list'): PaletteOption | null {
    const loc = workspaces.allLists.find((x) => x.list.id === listId)
    if (!loc) return null
    return {
      key: `${keyPrefix}-${listId}`,
      kind: 'list',
      label: loc.list.name,
      highlight,
      meta: loc.space.name,
      space: loc.space,
      run: closing(() => go(`/l/${listId}`)),
    }
  }

  function spaceOption(space: Space): PaletteOption | null {
    const first = [...space.lists].sort((a, b) => a.position - b.position)[0]
    if (!first && !workspaces.canEdit()) return null
    return {
      key: `space-${space.id}`,
      kind: 'space',
      label: space.name,
      highlight: true,
      meta: 'Space',
      space,
      run: closing(() => (first ? go(`/l/${first.id}`) : ui.openNewList(space.id))),
    }
  }

  function actionOption(a: PaletteAction, highlight: boolean): PaletteOption {
    return {
      key: `action-${a.key}`,
      kind: 'action',
      label: a.label,
      highlight,
      icon: a.icon,
      kbd: a.kbd,
      current: a.current,
      run: closing(a.run),
    }
  }

  // ---- Groups ----

  const recentGroup = computed<PaletteGroup | null>(() => {
    const lists = new Set(workspaces.allLists.map((x) => x.list.id))
    const options: PaletteOption[] = []
    for (const r of recents.tasks) {
      if (options.length >= RECENT_TASKS) break
      if (!lists.has(r.listId) || tasks.isPendingDelete(r.id)) continue
      const live = tasks.get(r.id)
      options.push({
        key: `recent-task-${r.id}`,
        kind: 'task',
        label: live?.title ?? r.title,
        highlight: false,
        meta: pathOf(live?.listId ?? r.listId),
        icon: History,
        run: ({ mod }) => openTask(live ?? { id: r.id }, mod),
      })
    }
    let listCount = 0
    for (const id of recents.lists) {
      if (listCount >= RECENT_LISTS) break
      const opt = listOption(id, false, 'recent-list')
      if (!opt) continue
      options.push(opt)
      listCount++
    }
    return options.length ? { key: 'recent', label: 'Recent', options } : null
  })

  const taskResults = computed<Task[]>(() => {
    const q = trimmed.value
    const s = o.search
    if (!q) return []
    // An older answer, while the new one is on its way: only what still matches by title.
    if (s.resultsFor.value !== q) return s.results.value.filter((t) => matches(t.title, q))
    return s.results.value.filter((t) => !tasks.isPendingDelete(t.id))
  })

  const groups = computed<PaletteGroup[]>(() => {
    const q = trimmed.value
    if (!q) {
      const out: PaletteGroup[] = []
      if (recentGroup.value) out.push(recentGroup.value)
      let switches = 0
      const shown = actions.value.filter((a) => !a.key.startsWith('switch-') || switches++ < SWITCHES_SHOWN)
      out.push({ key: 'actions', label: 'Actions', options: shown.map((a) => actionOption(a, false)) })
      return out
    }
    const out: PaletteGroup[] = []

    // Tasks, in server order.
    const found = taskResults.value
    const taskOptions = (o.expanded.value ? found : found.slice(0, TASKS_SHOWN)).map(taskOption)
    if (!o.expanded.value && found.length > TASKS_SHOWN && o.search.resultsFor.value === q) {
      taskOptions.push({
        key: 'more',
        kind: 'more',
        label: `Show all ${found.length} results`,
        highlight: false,
        icon: ChevronDown,
        stay: true,
        run: () => {
          o.expanded.value = true
        },
      })
    }
    if (o.search.failed.value && o.search.resultsFor.value === q) {
      taskOptions.push({
        key: 'retry',
        kind: 'retry',
        label: "Couldn't search tasks",
        highlight: false,
        icon: CircleAlert,
        stay: true,
        run: () => o.search.retry(),
      })
    }
    if (taskOptions.length) out.push({ key: 'tasks', label: 'Tasks', options: taskOptions })

    // Lists, then spaces: six together.
    const lists: PaletteOption[] = []
    for (const { list } of workspaces.allLists) {
      if (lists.length >= LOCAL_MAX) break
      if (!matches(list.name, q)) continue
      const opt = listOption(list.id, true)
      if (opt) lists.push(opt)
    }
    if (lists.length) out.push({ key: 'lists', label: 'Lists', options: lists })
    const spaces: PaletteOption[] = []
    const ws = workspaces.current
    for (const space of ws ? [...ws.spaces].sort((a, b) => a.position - b.position) : []) {
      if (lists.length + spaces.length >= LOCAL_MAX) break
      if (!matches(space.name, q)) continue
      const opt = spaceOption(space)
      if (opt) spaces.push(opt)
    }
    if (spaces.length) out.push({ key: 'spaces', label: 'Spaces', options: spaces })

    // Actions, then the create row, always last.
    const acts = actions.value
      .filter((a) => matches(a.label, q) || (a.keywords ? startsWord(a.keywords, q) : false))
      .slice(0, LOCAL_MAX)
      .map((a) => actionOption(a, true))
    if (workspaces.canEdit()) {
      acts.push({
        key: 'create',
        kind: 'create',
        label: `Create task “${q}”`,
        highlight: false,
        icon: Plus,
        run: closing(() => ui.openQuickCreate({ title: q })),
      })
    }
    if (acts.length) out.push({ key: 'actions', label: 'Actions', options: acts })
    return out
  })

  /** A typed query that found nothing (the create row aside), once the server has answered. */
  const noResults = computed(() => {
    const q = trimmed.value
    if (!q || o.search.searching.value || o.search.resultsFor.value !== q) return false
    return groups.value.every((g) => g.options.every((opt) => opt.kind === 'create'))
  })

  return { groups, noResults, trimmed }
}

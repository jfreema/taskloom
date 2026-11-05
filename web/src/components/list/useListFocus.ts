// The List's roving tab stop and focus rescue (8.7, 9.6).
// - One tab stop: the last focused row, else the first task row (not a group header, whose
//   "Select all" checkbox would be the first stop); only its controls are tabbable.
// - Focus that a re-render takes away (a row that moved to another group, was deleted, completed
//   and hidden after the hold, or moved by Alt+arrows) is put back on the row, or on the next task
//   row of its group, else the previous one, else the group header, else the nearest row.
// - The row being worked on (it has focus, or its own status picker changed it) that lands in
//   another group is followed: it scrolls into view (block 'nearest') and is highlighted for a
//   second (`movedKey`), so a status change never loses the task.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { firstNavKey, focusRow } from './useListKeyboard'
import { headerKey, type ListModel } from './useListModel'

export interface ListFocus {
  /** The nav key of the row that is the tab stop. */
  tabStop: ComputedRef<string | null>
  /** The nav key of the row that last held focus, while it is still shown. */
  remembered(): string | null
  /** The row just followed into another group: it is highlighted while this holds its key. */
  movedKey: Readonly<Ref<string | null>>
  /** A change made from this row's own control (its status picker): follow the row if it moves. */
  follow(key: string): void
  onFocusIn(e: FocusEvent): void
  onFocusOut(e: FocusEvent): void
}

/** How long a followed row stays highlighted. */
const MOVED_MS = 1000

export function useListFocus(root: Ref<HTMLElement | null>, model: ListModel): ListFocus {
  const tabStopKey = ref<string | null>(null)
  const tabStop = computed(() => {
    const keys = model.navKeys.value
    const k = tabStopKey.value
    return k && keys.includes(k) ? k : firstNavKey(keys)
  })

  function remembered(): string | null {
    const k = tabStopKey.value
    return k && model.navKeys.value.includes(k) ? k : null
  }

  /** Focus is (or was, until a re-render took it away) inside the grid. */
  let focusInGrid = false
  let lastKey: string | null = null

  function onFocusIn(e: FocusEvent): void {
    focusInGrid = true
    const row = e.target instanceof Element ? e.target.closest<HTMLElement>('[data-row-key]') : null
    const key = row?.dataset.rowKey
    if (key) {
      lastKey = key
      tabStopKey.value = key
    }
  }

  function onFocusOut(e: FocusEvent): void {
    const next = e.relatedTarget
    if (next instanceof Node && root.value?.contains(next)) return
    const from = e.target instanceof Element ? e.target : null
    // A focused row removed by a re-render may report a focusout: it is no longer in the document.
    queueMicrotask(() => {
      if (from?.isConnected) focusInGrid = false
    })
  }

  /** Each nav key's group in the last render, for choosing where lost focus goes. */
  let groupOfKey = new Map<string, string>()

  function snapshotGroups(): void {
    const m = new Map<string, string>()
    for (const g of model.groups.value) {
      if (g.hasHeader) m.set(headerKey(g.key), g.key)
      for (const r of g.rows) m.set(r.key, g.key)
    }
    groupOfKey = m
  }

  function rescueTarget(lost: string, keys: string[], old: string[], groups: Map<string, string>): string | null {
    const present = new Set(keys)
    const group = groups.get(lost)
    const i = old.indexOf(lost)
    if (i >= 0) {
      for (let j = i + 1; j < old.length && groups.get(old[j]) === group; j++) {
        if (old[j].startsWith('t:') && present.has(old[j])) return old[j]
      }
      for (let j = i - 1; j >= 0 && groups.get(old[j]) === group; j--) {
        if (old[j].startsWith('t:') && present.has(old[j])) return old[j]
      }
    }
    if (group && present.has(headerKey(group))) return headerKey(group)
    if (i >= 0) {
      for (let j = i + 1; j < old.length; j++) if (present.has(old[j])) return old[j]
      for (let j = i - 1; j >= 0; j--) if (present.has(old[j])) return old[j]
    }
    return keys[0] ?? null
  }

  // ---- Following a row into another group ----

  const movedKey = ref<string | null>(null)
  let movedTimer: ReturnType<typeof setTimeout> | undefined
  /** Set by follow() for the render of the change it announces. */
  let followKey: string | null = null

  function follow(key: string): void {
    followKey = key
    // The change has re-rendered by the next tick (the watcher below has run): the request lapses.
    void nextTick(() => {
      if (followKey === key) followKey = null
    })
  }

  function highlight(key: string): void {
    movedKey.value = key
    clearTimeout(movedTimer)
    movedTimer = setTimeout(() => {
      if (movedKey.value === key) movedKey.value = null
    }, MOVED_MS)
  }
  onBeforeUnmount(() => clearTimeout(movedTimer))

  watch(
    () => model.navKeys.value,
    (keys, old) => {
      const before = groupOfKey
      snapshotGroups()
      const active = document.activeElement
      const lost = !active || active === document.body || !active.isConnected
      const working = followKey ?? (focusInGrid ? lastKey : null)
      followKey = null
      if (working && keys.includes(working)) {
        const was = before.get(working)
        const now = groupOfKey.get(working)
        if (was !== undefined && now !== undefined && was !== now) {
          root.value?.querySelector(`[data-row-key="${CSS.escape(working)}"]`)?.scrollIntoView?.({ block: 'nearest' })
          highlight(working)
        }
      }
      if (focusInGrid && lastKey && lost) {
        const target = keys.includes(lastKey) ? lastKey : rescueTarget(lastKey, keys, old ?? [], before)
        if (!target || !focusRow(root.value, target)) focusInGrid = false
      }
    },
    { flush: 'post' },
  )
  onMounted(snapshotGroups)

  return { tabStop, remembered, movedKey, follow, onFocusIn, onFocusOut }
}

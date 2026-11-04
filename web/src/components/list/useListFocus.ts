// The List's roving tab stop and focus rescue (8.7, 9.6).
// - One tab stop: the last focused row, else the first; only its controls are tabbable.
// - Focus that a re-render takes away (a row that moved to another group, was deleted, completed
//   and hidden after the hold, or moved by Alt+arrows) is put back on the row, or on the next task
//   row of its group, else the previous one, else the group header, else the nearest row.
import { computed, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { focusRow } from './useListKeyboard'
import { headerKey, type ListModel } from './useListModel'

export interface ListFocus {
  /** The nav key of the row that is the tab stop. */
  tabStop: ComputedRef<string | null>
  onFocusIn(e: FocusEvent): void
  onFocusOut(e: FocusEvent): void
}

export function useListFocus(root: Ref<HTMLElement | null>, model: ListModel): ListFocus {
  const tabStopKey = ref<string | null>(null)
  const tabStop = computed(() => {
    const keys = model.navKeys.value
    const k = tabStopKey.value
    return k && keys.includes(k) ? k : (keys[0] ?? null)
  })

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

  function rescueTarget(lost: string, keys: string[], old: string[]): string | null {
    const present = new Set(keys)
    const group = groupOfKey.get(lost)
    const i = old.indexOf(lost)
    if (i >= 0) {
      for (let j = i + 1; j < old.length && groupOfKey.get(old[j]) === group; j++) {
        if (old[j].startsWith('t:') && present.has(old[j])) return old[j]
      }
      for (let j = i - 1; j >= 0 && groupOfKey.get(old[j]) === group; j--) {
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

  watch(
    () => model.navKeys.value,
    (keys, old) => {
      const active = document.activeElement
      const lost = !active || active === document.body || !active.isConnected
      if (focusInGrid && lastKey && lost) {
        const target = keys.includes(lastKey) ? lastKey : rescueTarget(lastKey, keys, old ?? [])
        if (!target || !focusRow(root.value, target)) focusInGrid = false
      }
      snapshotGroups()
    },
    { flush: 'post' },
  )
  onMounted(snapshotGroups)

  return { tabStop, onFocusIn, onFocusOut }
}

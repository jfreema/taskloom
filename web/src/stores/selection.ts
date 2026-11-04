// Bulk selection in the List view (8.7): one list at a time, top-level tasks only.
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import type { Uuid } from '../types'

export const useSelection = defineStore('selection', () => {
  const listId = ref<Uuid | null>(null)
  const ids = ref(new Set<Uuid>())
  const anchorId = ref<Uuid | null>(null)
  /** Touch selection mode, entered from the row menu's "Select" on coarse pointers. */
  const mode = ref(false)
  const count = computed(() => ids.value.size)

  function has(id: Uuid): boolean {
    return ids.value.has(id)
  }

  function toggle(id: Uuid): void {
    const next = new Set(ids.value)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    ids.value = next
    anchorId.value = id
    if (next.size === 0) mode.value = false
  }

  /** Replaces the selection. The anchor becomes the last id. */
  function select(list: Uuid[]): void {
    ids.value = new Set(list)
    anchorId.value = list.length ? list[list.length - 1] : null
    if (list.length === 0) mode.value = false
  }

  function add(list: Uuid[]): void {
    if (!list.length) return
    const next = new Set(ids.value)
    for (const id of list) next.add(id)
    ids.value = next
  }

  function remove(list: Uuid[]): void {
    if (!list.length || !list.some((id) => ids.value.has(id))) return
    const next = new Set(ids.value)
    for (const id of list) next.delete(id)
    ids.value = next
    if (anchorId.value && !next.has(anchorId.value) && list.includes(anchorId.value)) anchorId.value = null
    if (next.size === 0) mode.value = false
  }

  /**
   * Shift+click: adds the range from the anchor to `toId` in visible order. Without an anchor in
   * `orderedIds` it toggles `toId` instead. The anchor stays where it was.
   */
  function selectRange(toId: Uuid, orderedIds: Uuid[]): void {
    const from = anchorId.value ? orderedIds.indexOf(anchorId.value) : -1
    const to = orderedIds.indexOf(toId)
    if (from < 0 || to < 0) {
      toggle(toId)
      return
    }
    const [a, b] = from <= to ? [from, to] : [to, from]
    add(orderedIds.slice(a, b + 1))
  }

  function clear(): void {
    if (ids.value.size) ids.value = new Set()
    anchorId.value = null
    mode.value = false
  }

  /** A list change clears the selection. */
  function setList(next: Uuid | null): void {
    if (listId.value === next) return
    listId.value = next
    clear()
  }

  function reset(): void {
    listId.value = null
    clear()
  }

  return { listId, ids, anchorId, count, mode, has, toggle, select, add, remove, selectRange, clear, setList, reset }
})

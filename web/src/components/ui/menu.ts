// The UiMenu item type (spec 7.10), and the parts of UiMenu that hold no state: how items split
// into blocks, how typing letters finds an item, and how focus moves between a level's stops.
import type { Component } from 'vue'

export type MenuItem =
  | { kind?: 'item'; id: string; label: string; icon?: Component; kbd?: string; hint?: string; danger?: boolean; disabled?: boolean; onSelect: () => void }
  | { kind: 'checkbox'; id: string; label: string; icon?: Component; checked: boolean; disabled?: boolean; onSelect: () => void }
  | { kind: 'radio'; id: string; label: string; icon?: Component; checked: boolean; disabled?: boolean; onSelect: () => void }
  | { kind: 'submenu'; id: string; label: string; icon?: Component; disabled?: boolean; items: MenuItem[] }
  | { kind: 'swatches'; id: string; label: string; value: string; onSelect: (hex: string) => void }
  | { kind: 'separator'; id: string }
  | { kind: 'label'; id: string; label: string }

/** The items that render as a row: everything but separators, labels and swatches. */
export type MenuActionItem = Exclude<MenuItem, { kind: 'separator' | 'label' | 'swatches' }>

export type SubmenuItem = Extract<MenuItem, { kind: 'submenu' }>
export type SwatchesItem = Extract<MenuItem, { kind: 'swatches' }>

/** The id of the Back row of a submenu shown in place (phones). */
export const BACK_ID = '__back'

export type MenuBlock =
  | { type: 'separator'; key: string }
  | { type: 'items'; key: string; items: MenuItem[] }
  | { type: 'group'; key: string; label: string; items: MenuItem[] }

/**
 * Items split at separators and label items (a label heads the items after it, up to the next
 * separator or label). Empty groups, and separators at the ends or next to each other, are dropped.
 */
export function menuBlocks(items: readonly MenuItem[]): MenuBlock[] {
  const out: MenuBlock[] = []
  let cur: Extract<MenuBlock, { items: MenuItem[] }> | null = null
  for (const item of items) {
    if (item.kind === 'separator') {
      cur = null
      if (out.length && out[out.length - 1].type !== 'separator') out.push({ type: 'separator', key: item.id })
    } else if (item.kind === 'label') {
      cur = { type: 'group', key: item.id, label: item.label, items: [] }
      out.push(cur)
    } else {
      if (!cur) {
        cur = { type: 'items', key: `items-${item.id}`, items: [] }
        out.push(cur)
      }
      cur.items.push(item)
    }
  }
  const kept = out.filter((b) => b.type === 'separator' || b.items.length > 0)
  while (kept.length && kept[kept.length - 1].type === 'separator') kept.pop()
  while (kept.length && kept[0].type === 'separator') kept.shift()
  return kept.filter((b, i) => b.type !== 'separator' || kept[i - 1]?.type !== 'separator')
}

/**
 * Typeahead: letters typed within 600ms of each other build a prefix. The returned function takes
 * the key, the item labels in order and the current index, and returns the index to move to, or -1.
 * Repeating one letter cycles through the items that start with it.
 */
export function createTypeahead(): (key: string, labels: readonly string[], current: number) => number {
  let typed = ''
  let typedAt = 0
  return (key, labels, current) => {
    const now = Date.now()
    const k = key.toLowerCase()
    typed = now - typedAt > 600 ? k : typed + k
    typedAt = now
    if (!labels.length) return -1
    const same = [...typed].every((c) => c === typed[0])
    const query = same ? typed[0] : typed
    const from = same ? current + 1 : Math.max(current, 0)
    for (let n = 0; n < labels.length; n++) {
      const i = (from + n) % labels.length
      if (labels[i].toLowerCase().startsWith(query)) return i
    }
    return -1
  }
}

/** A level's focus stops in order: its item rows and each swatches row (data-menu-stop). */
export function menuStops(list: HTMLElement | null): HTMLElement[] {
  return list ? Array.from(list.querySelectorAll<HTMLElement>('[data-menu-stop]')) : []
}

/** The index of the stop that has focus (or holds it, for a swatches row), else -1. */
export function focusedStop(stops: readonly HTMLElement[]): number {
  const active = document.activeElement
  return active ? stops.findIndex((el) => el === active || el.contains(active)) : -1
}

/** Focuses a stop: an item row itself, a swatches row through its checked (else first) swatch. */
export function focusMenuStop(el: HTMLElement | undefined): void {
  if (!el) return
  const target = el.hasAttribute('data-menu-swatches')
    ? (el.querySelector<HTMLElement>('[aria-checked="true"]') ?? el.querySelector<HTMLElement>('[role="menuitemradio"]'))
    : el
  target?.focus({ preventScroll: true })
  target?.scrollIntoView({ block: 'nearest' })
}

// The UiListbox option type (spec 7.10), the base of every picker, and the pure part of UiListbox:
// filtering and the rows it shows.
import type { Component } from 'vue'

export interface ListboxOption {
  value: string
  label: string
  /** Second line or right-aligned hint. */
  description?: string
  icon?: Component
  /** Group heading; groups keep first-appearance order. */
  group?: string
  disabled?: boolean
  /** Extra search terms, e.g. an email. */
  keywords?: string[]
}

export type ListboxRow =
  | { kind: 'clear'; key: string; label: string }
  | { kind: 'option'; key: string; option: ListboxOption }
  | { kind: 'create'; key: string; query: string }

export interface ListboxSection {
  key: string
  group: string | null
  rows: ListboxRow[]
}

/** Lower case without accents, so "jose" finds "José". */
export function foldText(s: string): string {
  return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

/** The default filter: the query appears in the label or a keyword, ignoring case and accents. */
export function matchesOption(o: ListboxOption, q: string): boolean {
  const needle = foldText(q)
  return [o.label, ...(o.keywords ?? [])].some((t) => foldText(t).includes(needle))
}

/**
 * The rows for a query (already trimmed): the clear row first (when its label matches), the
 * matching options by group in first-appearance order, then the create row when creatable and no
 * option's label equals the query.
 */
export function listboxSections(
  options: readonly ListboxOption[],
  q: string,
  o: { clearLabel?: string; creatable?: boolean; filter?: (o: ListboxOption, q: string) => boolean } = {},
): ListboxSection[] {
  const test = o.filter ?? matchesOption
  const index = new Map(options.map((opt, i) => [opt.value, i]))
  const matches = q ? options.filter((opt) => test(opt, q)) : options
  const out: ListboxSection[] = []
  if (o.clearLabel && (!q || foldText(o.clearLabel).includes(foldText(q)))) {
    out.push({ key: 'clear', group: null, rows: [{ kind: 'clear', key: 'clear', label: o.clearLabel }] })
  }
  const groups = new Map<string, ListboxSection>()
  for (const opt of matches) {
    const name = opt.group ?? ''
    let section = groups.get(name)
    if (!section) {
      section = { key: `g-${out.length}-${name}`, group: opt.group ?? null, rows: [] }
      groups.set(name, section)
      out.push(section)
    }
    section.rows.push({ kind: 'option', key: `o-${index.get(opt.value) ?? 0}`, option: opt })
  }
  const exact = options.some((opt) => foldText(opt.label) === foldText(q))
  if (o.creatable && q && !exact) out.push({ key: 'create', group: null, rows: [{ kind: 'create', key: 'create', query: q }] })
  return out
}

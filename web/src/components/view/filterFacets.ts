// The filter facets of a list page (7.12): their names, the values a facet chip stands for, and
// immutable edits of TaskFilters. 'me' in `assignees` is the "Assigned to me" chip and the Filter
// popover's "Me": the same value, so checking one checks the other, and it never gets a facet chip.
import { DUE_FILTER_LABEL, type DueFilter, type TaskFilters } from '../../lib/taskQuery'
import { PRIORITY_LABEL, type Priority, type Uuid } from '../../types'

export type FacetKey = keyof TaskFilters

export const FACETS: readonly FacetKey[] = ['statusIds', 'assignees', 'priorities', 'tagIds', 'due']

export const FACET_LABEL: Record<FacetKey, string> = {
  statusIds: 'Status',
  assignees: 'Assignee',
  priorities: 'Priority',
  tagIds: 'Tags',
  due: 'Due',
}

export function cloneFilters(f: TaskFilters): TaskFilters {
  return {
    statusIds: [...f.statusIds],
    assignees: [...f.assignees],
    priorities: [...f.priorities],
    tagIds: [...f.tagIds],
    due: [...f.due],
  }
}

function valuesOf(f: TaskFilters, key: FacetKey): readonly string[] {
  return f[key]
}

export function hasValue(f: TaskFilters, key: FacetKey, value: string): boolean {
  return valuesOf(f, key).includes(value)
}

/** Adds the value to its facet, or removes it when it is there. */
export function toggleValue(f: TaskFilters, key: FacetKey, value: string): TaskFilters {
  const list = valuesOf(f, key)
  const next = list.includes(value) ? list.filter((v) => v !== value) : [...list, value]
  return { ...cloneFilters(f), [key]: next } as TaskFilters
}

/** The values a facet chip stands for: all of them, except 'me', which has its own chip. */
export function chipValues(f: TaskFilters, key: FacetKey): string[] {
  const list = valuesOf(f, key)
  return key === 'assignees' ? list.filter((v) => v !== 'me') : [...list]
}

/** Clears what the facet's chip stands for ('me' stays: it belongs to "Assigned to me"). */
export function clearFacet(f: TaskFilters, key: FacetKey): TaskFilters {
  const next = cloneFilters(f)
  if (key === 'assignees') next.assignees = next.assignees.filter((v) => v === 'me')
  else next[key] = []
  return next
}

/** The facets that get a chip, in toolbar order. */
export function chipFacets(f: TaskFilters): FacetKey[] {
  return FACETS.filter((k) => chipValues(f, k).length > 0)
}

export interface FacetLookups {
  statusName: (id: Uuid) => string | undefined
  memberName: (id: Uuid) => string
  tagName: (id: Uuid) => string | undefined
}

/** A value's name, for the chip tooltip and the checkbox rows. */
export function valueLabel(key: FacetKey, value: string, l: FacetLookups): string {
  switch (key) {
    case 'statusIds':
      return l.statusName(value) ?? 'Deleted status'
    case 'assignees':
      return value === 'me' ? 'Me' : value === 'unassigned' ? 'Unassigned' : l.memberName(value)
    case 'priorities':
      return PRIORITY_LABEL[value as Priority] ?? value
    case 'tagIds':
      return l.tagName(value) ?? 'Deleted tag'
    case 'due':
      return DUE_FILTER_LABEL[value as DueFilter] ?? value
  }
}

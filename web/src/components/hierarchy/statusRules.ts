// The status editor's rules (8.13), mirrored from the server (5.5) so the editor can disable a
// control before the server would refuse it: a list keeps at least one Done status and at least
// one status that isn't Done. Also the in-section moves (Alt+Up/Down and the row menu), placed at
// the midpoint between the new neighbours (lib/positions.ts) and announced as "Review moved up, 2 of 3".
import { between } from '../../lib/positions'
import { STATUS_CATEGORY_LABEL, type Status, type StatusCategory } from '../../types'

export const NEED_DONE = 'A list needs at least one Done status.'
export const NEED_OPEN = "A list needs at least one status that isn't Done."

export const CATEGORIES: readonly StatusCategory[] = ['NotStarted', 'Active', 'Done']

/** The list's statuses (display order) split into the three sections, each in position order. */
export function sectionsOf(statuses: readonly Status[]): Record<StatusCategory, Status[]> {
  const out: Record<StatusCategory, Status[]> = { NotStarted: [], Active: [], Done: [] }
  for (const s of statuses) out[s.category].push(s)
  return out
}

function counts(statuses: readonly Status[]): { done: number; open: number } {
  let done = 0
  for (const s of statuses) if (s.category === 'Done') done++
  return { done, open: statuses.length - done }
}

/** Why `status` can't be deleted, or null when it can. */
export function deleteBlocked(status: Status, statuses: readonly Status[]): string | null {
  const { done, open } = counts(statuses)
  if (status.category === 'Done' && done <= 1) return NEED_DONE
  if (status.category !== 'Done' && open <= 1) return NEED_OPEN
  return null
}

/** Why `status` can't move to `to`, or null when it can. */
export function moveBlocked(status: Status, to: StatusCategory, statuses: readonly Status[]): string | null {
  if (to === status.category) return null
  const { done, open } = counts(statuses)
  if (status.category === 'Done' && done <= 1) return NEED_DONE
  if (to === 'Done' && open <= 1) return NEED_OPEN
  return null
}

export interface InSectionMove {
  position: number
  /** 1-based place in the section after the move. */
  place: number
  of: number
}

/** The new position for moving `status` one place up or down inside its section; null at the edge. */
export function stepMove(status: Status, dir: 'up' | 'down', statuses: readonly Status[]): InSectionMove | null {
  const section = sectionsOf(statuses)[status.category]
  const i = section.findIndex((s) => s.id === status.id)
  if (i === -1) return null
  if (dir === 'up') {
    if (i === 0) return null
    return { position: between(section[i - 2]?.position ?? null, section[i - 1].position), place: i, of: section.length }
  }
  if (i === section.length - 1) return null
  return { position: between(section[i + 1].position, section[i + 2]?.position ?? null), place: i + 2, of: section.length }
}

/** A position at the end of section `to` (a status moved there goes last). */
export function endOf(to: StatusCategory, statuses: readonly Status[], except?: string): number {
  const section = sectionsOf(statuses)[to].filter((s) => s.id !== except)
  return between(section[section.length - 1]?.position ?? null, null)
}

export function sectionLabel(c: StatusCategory): string {
  return STATUS_CATEGORY_LABEL[c]
}

export function movedText(name: string, dir: 'up' | 'down', m: InSectionMove): string {
  return `${name} moved ${dir}, ${m.place} of ${m.of}`
}

// The status sets a new list can start with (8.13 NewListDialog), mirrored from the server's
// ListStatus.ForPreset so each card can list its statuses as text before anything is created.
import type { Status, StatusPreset } from '../../types'

export type StatusChoice = StatusPreset | 'copy'

export interface PresetCard {
  value: StatusChoice
  label: string
  description: string
}

export const PRESET_STATUSES: Record<StatusPreset, readonly string[]> = {
  Simple: ['To do', 'In progress', 'Done'],
  Review: ['To do', 'In progress', 'Review', 'Done'],
  Requests: ['New', 'In progress', 'Waiting', 'Done'],
}

/** "To do · In progress · Done". */
export function statusLine(names: readonly string[]): string {
  return names.join(' · ')
}

/** The cards in the order the dialog shows them; Simple is the default. */
export const PRESET_CARDS: readonly PresetCard[] = [
  { value: 'Simple', label: 'Simple', description: statusLine(PRESET_STATUSES.Simple) },
  { value: 'Review', label: 'With review', description: statusLine(PRESET_STATUSES.Review) },
  { value: 'Requests', label: 'Requests', description: statusLine(PRESET_STATUSES.Requests) },
]

/** A list's statuses as text, in display order. */
export function statusesText(statuses: readonly Status[]): string {
  return statusLine(statuses.map((s) => s.name))
}

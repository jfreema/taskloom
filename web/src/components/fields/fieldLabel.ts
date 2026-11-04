// Shared by the field components (7.11): trigger names and the props every picker takes.
import { formatNames } from '../../lib/format'

/**
 * A picker trigger's accessible name (7.11, 11.1): "Due date: Tomorrow, change", or
 * "Due date: none, add" when the field is empty.
 */
export function triggerLabel(field: string, value?: string | null): string {
  const v = value?.trim()
  return v ? `${field}: ${v}, change` : `${field}: none, add`
}

/** Names inside a trigger name: "Jared Doe", "Jared Doe and Ana Moreno", "A, B, C and 2 others". */
export function joinNames(names: readonly string[]): string {
  return formatNames([...names], 3)
}

/** The props every picker shares (7.11). */
export interface SharedFieldProps {
  /** Guests, or a task being deleted: the value renders as static text or an icon, with no button. */
  readonly?: boolean
  /** Positions the popover against this element instead of the trigger. */
  anchor?: HTMLElement | null
  /** Renders no trigger: the view opens the picker through v-model:open and passes `anchor`. */
  hideTrigger?: boolean
}

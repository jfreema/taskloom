// Types shared by UiPopover, its panel and the components built on it (UiMenu, UiSelect, pickers).
import type { ComponentPublicInstance } from 'vue'

export type PopoverCloseReason = 'escape' | 'outside' | 'select' | 'programmatic'

export type PopoverRole = 'dialog' | 'listbox' | 'menu' | 'none'

/** An element, or any object with a rectangle (a virtual anchor, e.g. a caret or a pointer position). */
export type PopoverAnchor = HTMLElement | { getBoundingClientRect(): DOMRect }

/** What the trigger slot spreads on its element: `<button v-bind="props">`. */
export interface PopoverTriggerProps {
  ref: (el: Element | ComponentPublicInstance | null) => void
  'aria-haspopup'?: 'dialog' | 'listbox' | 'menu'
  'aria-expanded': 'true' | 'false'
  'aria-controls'?: string
  onClick: (e: MouseEvent) => void
  onKeydown: (e: KeyboardEvent) => void
}

/** The DOM element behind a template ref that may be a component instance. */
export function toElement(x: Element | ComponentPublicInstance | null | undefined): HTMLElement | null {
  if (!x) return null
  if (x instanceof HTMLElement) return x
  const el = (x as ComponentPublicInstance).$el as unknown
  return el instanceof HTMLElement ? el : null
}

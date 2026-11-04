// Coloured flag icons for the priority menu (7.2): UiMenu draws an item's `icon` through UiIcon,
// which passes size, stroke width and fill; these wrap lucide's Flag with the priority's colour
// token and the filled shape of Urgent and High.
import { defineComponent, h, type Component } from 'vue'
import { Flag } from '@lucide/vue'
import type { Priority } from '../../types'

const COLOR: Record<Priority, string> = {
  Urgent: 'var(--priority-urgent)',
  High: 'var(--priority-high)',
  Normal: 'var(--priority-normal)',
  Low: 'var(--priority-low)',
  None: 'var(--text-tertiary)',
}

function glyph(p: Priority): Component {
  const filled = p === 'Urgent' || p === 'High'
  return defineComponent({
    name: `PriorityGlyph${p}`,
    inheritAttrs: false,
    setup(_, { attrs }) {
      return () => h(Flag as Component, { ...attrs, fill: filled ? 'currentColor' : 'none', style: { color: COLOR[p] } })
    },
  })
}

export const PRIORITY_GLYPHS: Record<Priority, Component> = {
  Urgent: glyph('Urgent'),
  High: glyph('High'),
  Normal: glyph('Normal'),
  Low: glyph('Low'),
  None: glyph('None'),
}

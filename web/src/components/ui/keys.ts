// Key legends for UiKbd and tooltips (spec 7.10 UiKbd). 'Mod' is ⌘ on Apple platforms and Ctrl
// elsewhere; 'Alt' is ⌥ or Alt; 'Shift' is ⇧ or Shift. '+' joins keys, a space separates the steps
// of a sequence ('G H' reads "G then H"). ⌘ ⌥ ⇧ and arrows are the only non-lucide glyphs allowed (7.1).

import { isApple } from '../../lib/platform'

export const IS_APPLE = isApple

interface Legend {
  /** What the key cap shows. */
  display: string
  /** What a screen reader hears. */
  spoken: string
  modifier: boolean
}

const ARROWS: Record<string, Legend> = {
  arrowup: { display: '↑', spoken: 'Up arrow', modifier: false },
  arrowdown: { display: '↓', spoken: 'Down arrow', modifier: false },
  arrowleft: { display: '←', spoken: 'Left arrow', modifier: false },
  arrowright: { display: '→', spoken: 'Right arrow', modifier: false },
}

const NAMED: Record<string, string> = {
  enter: 'Enter',
  return: 'Enter',
  escape: 'Esc',
  esc: 'Esc',
  backspace: 'Backspace',
  delete: 'Delete',
  del: 'Delete',
  space: 'Space',
  tab: 'Tab',
  pageup: 'Page Up',
  pagedown: 'Page Down',
  home: 'Home',
  end: 'End',
  contextmenu: 'Menu',
}

function legend(raw: string, apple: boolean): Legend {
  const key = raw.trim()
  const lower = key.toLowerCase()
  switch (lower) {
    case 'mod':
      return apple ? { display: '⌘', spoken: 'Command', modifier: true } : { display: 'Ctrl', spoken: 'Ctrl', modifier: true }
    case 'meta':
    case 'cmd':
    case 'command':
      return apple ? { display: '⌘', spoken: 'Command', modifier: true } : { display: 'Win', spoken: 'Windows', modifier: true }
    case 'ctrl':
    case 'control':
      return { display: 'Ctrl', spoken: 'Ctrl', modifier: true }
    case 'alt':
    case 'option':
      return apple ? { display: '⌥', spoken: 'Option', modifier: true } : { display: 'Alt', spoken: 'Alt', modifier: true }
    case 'shift':
      return apple ? { display: '⇧', spoken: 'Shift', modifier: true } : { display: 'Shift', spoken: 'Shift', modifier: true }
  }
  if (ARROWS[lower]) return ARROWS[lower]
  if (NAMED[lower]) return { display: NAMED[lower], spoken: NAMED[lower], modifier: false }
  if (key.length === 1) return { display: key.toUpperCase(), spoken: key.toUpperCase(), modifier: false }
  return { display: key, spoken: key, modifier: false }
}

export interface KeyStep {
  /** One key cap, for example "⌘K" or "Ctrl+K". */
  display: string
  /** For example "Command+K". */
  spoken: string
}

/** Splits 'Mod+K', 'G H' or 'Shift+Enter' into the caps UiKbd draws. */
export function keySteps(keys: string, apple: boolean = IS_APPLE): KeyStep[] {
  return keys
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((combo) => {
      const parts = combo === '+' ? ['+'] : combo.split('+').filter((p) => p !== '')
      const legends = parts.map((p) => legend(p, apple))
      const display = apple
        ? legends.map((l) => l.display).join('')
        : legends.map((l) => l.display).join('+')
      return { display, spoken: legends.map((l) => l.spoken).join('+') }
    })
}

/** The whole shortcut as a screen reader should hear it: "Ctrl+K", "G then H". */
export function spokenKeys(keys: string, apple: boolean = IS_APPLE): string {
  return keySteps(keys, apple)
    .map((s) => s.spoken)
    .join(' then ')
}

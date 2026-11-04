// Keyboard shortcuts (9.5): the single list behind the shortcuts dialog, tooltips and the palette's
// kbd hints, and the matcher `useShortcut` uses. Key strings use the UiKbd format: "Mod+K", "G H"
// (a sequence), "Shift+F10", "Alt+ArrowUp", "?".
import { isApple } from './platform'

export type ShortcutGroup = 'Global' | 'Lists and boards' | 'Task' | 'Calendar' | 'Text'

export interface ShortcutInfo {
  keys: string
  label: string
  group: ShortcutGroup
}

export const SHORTCUTS: ShortcutInfo[] = [
  // Global
  { keys: 'Mod+K', label: 'Open the command palette', group: 'Global' },
  { keys: '/', label: 'Search this view, or open the command palette', group: 'Global' },
  { keys: 'N', label: 'New task', group: 'Global' },
  { keys: '?', label: 'Show keyboard shortcuts', group: 'Global' },
  { keys: '[', label: 'Show or hide the sidebar', group: 'Global' },
  { keys: 'G H', label: 'Go to Home', group: 'Global' },
  { keys: 'Mod+Z', label: 'Undo the latest action', group: 'Global' },
  { keys: 'Escape', label: 'Close the top menu or dialog, the task, or the selection', group: 'Global' },
  // Lists and boards
  { keys: 'J', label: 'Next row or card (also Down arrow)', group: 'Lists and boards' },
  { keys: 'K', label: 'Previous row or card (also Up arrow)', group: 'Lists and boards' },
  { keys: 'ArrowRight', label: 'Expand subtasks, or the next column', group: 'Lists and boards' },
  { keys: 'ArrowLeft', label: 'Collapse subtasks, or the previous column', group: 'Lists and boards' },
  { keys: 'Home', label: 'First row', group: 'Lists and boards' },
  { keys: 'End', label: 'Last row', group: 'Lists and boards' },
  { keys: 'Enter', label: 'Open the focused task', group: 'Lists and boards' },
  { keys: 'X', label: 'Select or deselect the row (also Space)', group: 'Lists and boards' },
  { keys: 'Mod+A', label: 'Select every visible task', group: 'Lists and boards' },
  { keys: 'Mod+Enter', label: 'Complete or reopen', group: 'Lists and boards' },
  { keys: 'S', label: 'Change the status', group: 'Lists and boards' },
  { keys: 'A', label: 'Change the assignees', group: 'Lists and boards' },
  { keys: 'D', label: 'Change the due date', group: 'Lists and boards' },
  { keys: 'P', label: 'Change the priority', group: 'Lists and boards' },
  { keys: 'F2', label: 'Rename', group: 'Lists and boards' },
  { keys: 'Delete', label: 'Delete, with undo', group: 'Lists and boards' },
  { keys: 'Shift+F10', label: 'Open the row or card menu', group: 'Lists and boards' },
  { keys: 'Alt+ArrowUp', label: 'Move up one place', group: 'Lists and boards' },
  { keys: 'Alt+ArrowDown', label: 'Move down one place', group: 'Lists and boards' },
  { keys: 'Alt+ArrowLeft', label: 'Move the card to the previous column', group: 'Lists and boards' },
  { keys: 'Alt+ArrowRight', label: 'Move the card to the next column', group: 'Lists and boards' },
  { keys: '1', label: 'Urgent, in the priority menu', group: 'Lists and boards' },
  { keys: '2', label: 'High, in the priority menu', group: 'Lists and boards' },
  { keys: '3', label: 'Normal, in the priority menu', group: 'Lists and boards' },
  { keys: '4', label: 'Low, in the priority menu', group: 'Lists and boards' },
  { keys: '0', label: 'Clear the priority, in the priority menu', group: 'Lists and boards' },
  // Task
  { keys: 'J', label: 'Next task', group: 'Task' },
  { keys: 'K', label: 'Previous task', group: 'Task' },
  { keys: 'Mod+Enter', label: 'Complete or reopen', group: 'Task' },
  { keys: 'S', label: 'Change the status', group: 'Task' },
  { keys: 'A', label: 'Change the assignees', group: 'Task' },
  { keys: 'D', label: 'Change the due date', group: 'Task' },
  { keys: 'P', label: 'Change the priority', group: 'Task' },
  { keys: 'Mod+Backspace', label: 'Delete the task, with undo', group: 'Task' },
  { keys: 'Escape', label: 'Close the task', group: 'Task' },
  // Calendar
  { keys: 'ArrowRight', label: 'Next day (arrow keys move between days)', group: 'Calendar' },
  { keys: 'ArrowDown', label: 'Same day next week', group: 'Calendar' },
  { keys: 'Home', label: 'Start of the week', group: 'Calendar' },
  { keys: 'End', label: 'End of the week', group: 'Calendar' },
  { keys: 'T', label: 'Go to today', group: 'Calendar' },
  { keys: 'PageUp', label: 'Previous month', group: 'Calendar' },
  { keys: 'PageDown', label: 'Next month', group: 'Calendar' },
  { keys: 'Enter', label: "Go into the day's tasks", group: 'Calendar' },
  { keys: 'N', label: 'New task on the focused day', group: 'Calendar' },
  // Text
  { keys: 'Enter', label: 'Send a comment', group: 'Text' },
  { keys: 'Shift+Enter', label: 'New line in a comment', group: 'Text' },
  { keys: 'Mod+Enter', label: 'Create and open, in quick create', group: 'Text' },
]

export interface Chord {
  /** event.key to match: a lower-case letter, another single character, ' ' for Space, or a key name. */
  key: string
  mod: boolean
  ctrl: boolean
  meta: boolean
  alt: boolean
  shift: boolean
}

const KEY_ALIASES: Record<string, string> = {
  esc: 'Escape',
  escape: 'Escape',
  space: ' ',
  spacebar: ' ',
  up: 'ArrowUp',
  down: 'ArrowDown',
  left: 'ArrowLeft',
  right: 'ArrowRight',
  del: 'Delete',
  return: 'Enter',
  pgup: 'PageUp',
  pgdn: 'PageDown',
  plus: '+',
}

/** Parses one chord, "Mod+Shift+K". */
export function parseChord(text: string): Chord {
  const chord: Chord = { key: '', mod: false, ctrl: false, meta: false, alt: false, shift: false }
  const parts = text === '+' ? ['+'] : text.split('+').filter((p) => p !== '')
  for (let i = 0; i < parts.length; i++) {
    const raw = parts[i]
    const lower = raw.toLowerCase()
    const isLast = i === parts.length - 1
    if (!isLast || parts.length > 1) {
      if (lower === 'mod') { chord.mod = true; continue }
      if (lower === 'ctrl' || lower === 'control') { chord.ctrl = true; continue }
      if (lower === 'meta' || lower === 'cmd' || lower === 'command') { chord.meta = true; continue }
      if (lower === 'alt' || lower === 'option') { chord.alt = true; continue }
      if (lower === 'shift') { chord.shift = true; continue }
    }
    if (raw.length === 1) chord.key = /[a-z]/i.test(raw) ? lower : raw
    else chord.key = KEY_ALIASES[lower] ?? raw
  }
  return chord
}

/** Parses a shortcut into its sequence of chords: "G H" → two chords, "Mod+K" → one. */
export function parseShortcut(keys: string): Chord[] {
  return keys
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map(parseChord)
}

/** True when the chord has no Ctrl, Cmd or Alt (Shift alone doesn't count). */
export function isSingleKey(chord: Chord): boolean {
  return !chord.mod && !chord.ctrl && !chord.meta && !chord.alt
}

/** Character keys are the ones WCAG 2.1.4 covers: letters, digits, punctuation and symbols (plus F2, 9.5). */
export function isCharacterKey(chord: Chord): boolean {
  return (chord.key.length === 1 && chord.key !== ' ') || chord.key === 'F2'
}

type KeyEventLike = Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'metaKey' | 'altKey' | 'shiftKey'>

/**
 * Matches one chord against a keyboard event, on `event.key` so shortcuts follow the keyboard layout:
 * - letters match case-insensitively, with Shift matched exactly (Caps Lock doesn't matter); with
 *   a modifier on a non-Latin layout, the physical key (`event.code`) is used instead;
 * - other characters ("?", "/", "[", digits) match the character typed, whatever Shift, AltGr or
 *   Option the layout needed;
 * - named keys (Enter, ArrowUp, F2…) match their modifiers exactly.
 * `Mod` is Cmd on Apple platforms and Ctrl elsewhere.
 */
export function matchChord(e: KeyEventLike, c: Chord): boolean {
  const key = e.key
  if (!key || !c.key) return false
  const wantCtrl = c.ctrl || (c.mod && !isApple)
  const wantMeta = c.meta || (c.mod && isApple)

  if (/^[a-z]$/.test(c.key)) {
    let k = key.length === 1 ? key.toLowerCase() : ''
    const physical = e.code === `Key${c.key.toUpperCase()}`
    // Option on macOS and non-Latin layouts change the character; fall back to the physical key.
    if (k !== c.key && physical && (wantCtrl || wantMeta || c.alt) && !/^[a-z]$/i.test(key)) k = c.key
    if (k !== c.key) return false
    return e.ctrlKey === wantCtrl && e.metaKey === wantMeta && e.altKey === c.alt && e.shiftKey === c.shift
  }

  if (c.key.length === 1 && c.key !== ' ') {
    if (key !== c.key) return false
    if (wantCtrl || wantMeta) return e.ctrlKey === wantCtrl && e.metaKey === wantMeta && e.altKey === c.alt
    if (e.metaKey) return false
    if (e.ctrlKey && !e.altKey) return false // Ctrl+Alt is AltGr: a typed character, not a Ctrl shortcut
    if (c.alt && !e.altKey) return false
    return true
  }

  if (key !== c.key) return false
  return e.ctrlKey === wantCtrl && e.metaKey === wantMeta && e.altKey === c.alt && e.shiftKey === c.shift
}

/** Matches a one-chord shortcut ("Mod+K", "n", "Alt+ArrowUp"). Sequences ("G H") never match here. */
export function matchShortcut(e: KeyEventLike, keys: string): boolean {
  const chords = parseShortcut(keys)
  return chords.length === 1 && matchChord(e, chords[0])
}

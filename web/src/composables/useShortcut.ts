// Keyboard shortcuts (9.5). One document keydown listener dispatches to registrations by focus scope:
// layer → panel → grid → view → global, and the latest registration first within a scope. Only scopes
// that contain the focus are active. The first handler that doesn't return false stops the event.
// The layer stack's own Esc listener is registered first (useLayerStack loads before this module
// installs its listener), so Esc with an open layer never reaches these scopes.
import { getCurrentScope, onScopeDispose, ref, watch, type Ref } from 'vue'
import { isCharacterKey, isSingleKey, matchChord, parseShortcut, type Chord } from '../lib/shortcuts'
import { readString, writeString } from '../lib/storage'
import { useLayerStack } from './useLayerStack'

export type ShortcutScope = 'global' | 'view' | 'grid' | 'panel' | 'layer'

export interface ShortcutOptions {
  /** Where focus must be for the shortcut to fire. Default 'global'. */
  scope?: ShortcutScope
  /**
   * The scope's element. `grid`: the view's item container. `layer`: the layer's root (default: the
   * top layer). `panel`: the panel's focus root (default: the panel registration that has one, else
   * `[data-task-panel-root]` or `[data-shortcut-scope="panel"]`).
   */
  el?: Ref<HTMLElement | null>
  /** An extra condition, checked when the keys match. */
  when?: () => boolean
  /** Also fire while focus is in a text field (Mod+K). Default false. */
  allowInInputs?: boolean
  /**
   * A key of the focused widget, such as an open picker's number keys: it works even when
   * single-key shortcuts are off, which turns off only the keys 9.5 lists (WCAG 2.1.4 exempts
   * keys that are active only while their component has focus). Default false.
   */
  widget?: boolean
}

type Handler = (e: KeyboardEvent) => void | boolean

interface Registration {
  id: number
  alternatives: Chord[][]
  handler: Handler
  scope: ShortcutScope
  el?: Ref<HTMLElement | null>
  when?: () => boolean
  allowInInputs: boolean
  widget: boolean
}

type KeySnapshot = Pick<KeyboardEvent, 'key' | 'code' | 'ctrlKey' | 'metaKey' | 'altKey' | 'shiftKey'>

const SINGLE_KEY_STORAGE = 'taskloom.shortcuts.singleKey'
const SEQUENCE_MS = 1000
const SCOPE_RANK: Record<ShortcutScope, number> = { layer: 0, panel: 1, grid: 2, view: 3, global: 4 }
/** TaskPanelHost's focus root carries data-task-panel-root. */
const PANEL_SELECTOR = '[data-shortcut-scope="panel"], [data-task-panel-root]'
const GRID_SELECTOR = '[data-shortcut-scope="grid"]'
const MODIFIER_KEYS = new Set(['Shift', 'Control', 'Alt', 'Meta', 'AltGraph', 'CapsLock', 'Fn', 'OS'])
/** Input types that take typed text; checkboxes, radios and buttons don't. */
const TEXT_INPUT_TYPES = new Set([
  'text',
  'search',
  'email',
  'url',
  'tel',
  'password',
  'number',
  'date',
  'datetime-local',
  'month',
  'time',
  'week',
])

/**
 * Single-key shortcuts (letters, digits, punctuation and F2) are on unless the user turned them
 * off in Settings › Appearance (WCAG 2.1.4). Stored in `taskloom.shortcuts.singleKey`.
 */
export const shortcutsEnabled: Ref<boolean> = ref(readString(SINGLE_KEY_STORAGE) !== 'off')
watch(shortcutsEnabled, (on) => writeString(SINGLE_KEY_STORAGE, on ? 'on' : 'off'))

/** True when the target takes typed text: input, textarea, select, contenteditable, combobox or textbox. */
export function isTypingTarget(t: EventTarget | null): boolean {
  if (typeof Element === 'undefined' || !(t instanceof Element)) return false
  if (t instanceof HTMLInputElement) return TEXT_INPUT_TYPES.has(t.type)
  if (t instanceof HTMLTextAreaElement || t instanceof HTMLSelectElement) return true
  if (t instanceof HTMLElement && t.isContentEditable) return true
  const role = t.getAttribute('role')
  return role === 'combobox' || role === 'textbox'
}

let registrations: Registration[] = []
let nextId = 0
let listening = false
let lastKey: { e: KeySnapshot; at: number } | null = null

function contains(el: Element | null | undefined, node: Node | null): boolean {
  return !!el && !!node && el.contains(node)
}

function scopeElement(reg: Registration): Element | null {
  const own = reg.el?.value
  if (own) return own
  if (reg.scope === 'layer') return useLayerStack().top.value?.el() ?? null
  if (reg.scope === 'panel') {
    for (let i = registrations.length - 1; i >= 0; i--) {
      const r = registrations[i]
      if (r.scope === 'panel' && r.el?.value) return r.el.value
    }
    return document.querySelector(PANEL_SELECTOR)
  }
  return null
}

function inAnyGrid(node: Node | null): boolean {
  if (!node) return false
  for (const r of registrations) if (r.scope === 'grid' && contains(r.el?.value, node)) return true
  return node instanceof Element ? !!node.closest(GRID_SELECTOR) : !!node.parentElement?.closest(GRID_SELECTOR)
}

function isScopeActive(reg: Registration, focus: Node | null): boolean {
  switch (reg.scope) {
    case 'global':
      return true
    case 'view':
      return contains(document.getElementById('main'), focus) && !inAnyGrid(focus)
    case 'grid': {
      const own = reg.el?.value
      if (own) return contains(own, focus)
      return focus instanceof Element ? !!focus.closest(GRID_SELECTOR) : false
    }
    case 'layer':
    case 'panel':
      return contains(scopeElement(reg), focus)
  }
}

/** The alternative that matches this event (with the previous key for sequences), or null. */
function matchRegistration(reg: Registration, e: KeyboardEvent, prev: KeySnapshot | null): Chord[] | null {
  for (const alt of reg.alternatives) {
    if (alt.length === 1) {
      if (matchChord(e, alt[0])) return alt
    } else if (alt.length === 2 && prev && matchChord(prev, alt[0]) && matchChord(e, alt[1])) {
      return alt
    }
  }
  return null
}

function onKeydown(e: KeyboardEvent): void {
  if (e.defaultPrevented || e.isComposing || e.key === 'Process' || e.key === 'Dead' || e.key === 'Unidentified') return
  if (MODIFIER_KEYS.has(e.key)) return
  if (e.repeat && !e.key.startsWith('Arrow')) return
  const target = e.target instanceof Node ? e.target : null
  const focus: Node | null = document.activeElement ?? target
  const typing = isTypingTarget(e.target) || isTypingTarget(document.activeElement)
  const now = performance.now()
  const prev = lastKey && now - lastKey.at <= SEQUENCE_MS ? lastKey.e : null
  const layers = useLayerStack()
  const blockedByOverlay =
    layers.hasModal.value || (focus instanceof Element && !!focus.closest('#overlay-root'))

  const ordered = registrations
    .slice()
    .sort((a, b) => SCOPE_RANK[a.scope] - SCOPE_RANK[b.scope] || b.id - a.id)

  for (const reg of ordered) {
    const alt = matchRegistration(reg, e, prev)
    if (!alt) continue
    if (typing && !reg.allowInInputs) continue
    if (alt.every(isSingleKey)) {
      if (!shortcutsEnabled.value && !reg.widget && alt.some(isCharacterKey)) continue
      // A modal layer (or an open popover holding focus) owns single keys; page shortcuts wait.
      if ((reg.scope === 'global' || reg.scope === 'view') && blockedByOverlay) continue
    }
    if (!isScopeActive(reg, focus)) continue
    if (reg.when && !reg.when()) continue
    if (reg.handler(e) === false) continue
    e.preventDefault()
    e.stopPropagation()
    lastKey = null
    return
  }
  lastKey = {
    e: { key: e.key, code: e.code, ctrlKey: e.ctrlKey, metaKey: e.metaKey, altKey: e.altKey, shiftKey: e.shiftKey },
    at: now,
  }
}

function ensureListener(): void {
  if (listening || typeof document === 'undefined') return
  // Touch the layer stack so its module (and its Esc listener) is set up before ours.
  useLayerStack()
  listening = true
  document.addEventListener('keydown', onKeydown)
}

/**
 * Registers a shortcut: `keys` like 'n', 'Mod+k', 'g h', 'Alt+ArrowLeft', or several alternatives.
 * The handler returns false to pass the event on. Unregisters when the calling scope (component)
 * is disposed; the returned function unregisters earlier.
 */
export function useShortcut(keys: string | string[], handler: Handler, o: ShortcutOptions = {}): () => void {
  ensureListener()
  const list = Array.isArray(keys) ? keys : [keys]
  const reg: Registration = {
    id: ++nextId,
    alternatives: list.map(parseShortcut).filter((alt) => alt.length > 0),
    handler,
    scope: o.scope ?? 'global',
    el: o.el,
    when: o.when,
    allowInInputs: o.allowInInputs ?? false,
    widget: o.widget ?? false,
  }
  registrations = [...registrations, reg]
  let removed = false
  const unregister = () => {
    if (removed) return
    removed = true
    registrations = registrations.filter((r) => r !== reg)
  }
  if (getCurrentScope()) onScopeDispose(unregister)
  return unregister
}

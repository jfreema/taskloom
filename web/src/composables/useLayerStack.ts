// The layer stack (spec 7.7). Every open popover, menu, drawer and dialog registers a layer.
// - Esc closes only the top layer (a widget inside it can consume Esc first with preventDefault).
//   One Esc closes at most one layer: if the top layer closed itself while the key went down, the
//   layer below stays open.
// - Outside clicks close non-modal layers above the clicked layer.
// - While any modal layer is open, #app is inert and body scrolling is locked; modal layers must
//   teleport into #overlay-root, the sibling of #app that index.html provides.
// - When a layer closes, focus returns to the element that had focus when it opened, if that
//   element is still in the document (else to the opener of the layer it was opened from), and
//   otherwise to the layer's returnFocusTo fallback.
import { computed, getCurrentInstance, onBeforeUnmount, ref, shallowRef, watch, type ComputedRef, type Ref, type WatchSource } from 'vue'

export type LayerCloseReason = 'escape' | 'outside'

/** Which z-index band a layer starts from (7.7). */
export type LayerLevel = 'drawer' | 'modal' | 'popover'

/**
 * The z-index bands of 7.7: the same numbers as --z-drawer, --z-modal and --z-popover in
 * tokens.css. A layer sits at its band, or just above the layer under it when that one is higher,
 * so a confirm opened over a popover still covers it, and a picker opened from a dialog sits above
 * the dialog. Toasts (--z-toast 90) and the tooltip (100) stay above every layer.
 */
export const LEVEL_Z: Readonly<Record<LayerLevel, number>> = { drawer: 60, modal: 70, popover: 80 }

export interface LayerConfig {
  /** Modal layers make #app inert, lock scrolling and trap focus. */
  modal: boolean
  /** The layer's root element: the outside-click boundary and the `layer` shortcut scope. */
  el: () => HTMLElement | null | undefined
  /** Called by the stack for Esc on the top layer, or an outside click on a non-modal layer. */
  close: (reason: LayerCloseReason) => void
  /** Where focus goes when the opener is no longer in the document. */
  returnFocusTo?: () => HTMLElement | null | undefined
  /** false leaves focus where it is when the layer closes. Default true. */
  returnFocus?: boolean
  /** false: outside clicks don't close it. Default true. Modal layers never close on outside clicks. */
  closeOnOutside?: boolean
  /** Elements that count as inside the layer for outside clicks, typically its trigger. */
  ignore?: () => ReadonlyArray<Element | null | undefined>
  /** The z-index band. Default 'modal' for modal layers and 'popover' otherwise. */
  level?: LayerLevel
}

export interface Layer extends LayerConfig {
  readonly id: string
  /** The element that had focus when the layer opened. */
  readonly opener: HTMLElement | null
  /**
   * The opener, then the openers of the layer it was opened from, and so on: a dialog opened from
   * a menu item can return focus to the menu's trigger once the item has gone.
   */
  readonly openers: readonly HTMLElement[]
}

export interface LayerHandle {
  readonly id: string
  readonly isTop: ComputedRef<boolean>
  /** Unregisters the layer and returns focus. Calling it twice does nothing. */
  remove(o?: { returnFocus?: boolean }): void
}

const stack = shallowRef<readonly Layer[]>([])
const top = computed<Layer | null>(() => stack.value[stack.value.length - 1] ?? null)
const hasModal = computed(() => stack.value.some((l) => l.modal))

function levelOf(l: Pick<LayerConfig, 'modal' | 'level'>): LayerLevel {
  return l.level ?? (l.modal ? 'modal' : 'popover')
}

/** Each open layer's z-index: its band, or one above the layer under it when that is higher. */
const zIndexes = computed(() => {
  const map = new Map<string, number>()
  let below = 0
  for (const l of stack.value) {
    const z = Math.max(LEVEL_Z[levelOf(l)], below + 1)
    map.set(l.id, z)
    below = z
  }
  return map
})
let seq = 0
let listening = false
let scrollLocked = false

function contains(layer: Layer, target: Node): boolean {
  const el = layer.el()
  if (el && el.contains(target)) return true
  const extra = layer.ignore?.()
  return !!extra && extra.some((x) => !!x && x.contains(target))
}

function isUsable(el: HTMLElement): boolean {
  return el.isConnected && !el.closest('[inert]')
}

function lockScroll(): void {
  if (scrollLocked) return
  scrollLocked = true
  const html = document.documentElement
  const scrollbar = window.innerWidth - html.clientWidth
  html.style.overflow = 'hidden'
  if (scrollbar > 0) html.style.paddingRight = `${scrollbar}px`
}

function unlockScroll(): void {
  if (!scrollLocked) return
  scrollLocked = false
  const html = document.documentElement
  html.style.overflow = ''
  html.style.paddingRight = ''
}

/** Applied synchronously, so focus can return to the app in the same tick a modal closes. */
function syncModalState(): void {
  const app = document.getElementById('app')
  if (hasModal.value) {
    app?.setAttribute('inert', '')
    lockScroll()
  } else {
    app?.removeAttribute('inert')
    unlockScroll()
  }
}

// The top layer when the current Esc went down (recorded in the capture phase). A widget can close
// its own layer from an Esc handler (a date picker's `cancel`), and Vue may unmount that layer
// before the event bubbles up here; the Esc is then spent and must not close the layer below too.
let escapeAt: { event: KeyboardEvent; layerId: string | null } | null = null

function onKeydownCapture(e: KeyboardEvent): void {
  if (e.key === 'Escape') escapeAt = { event: e, layerId: top.value?.id ?? null }
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing) return
  const layer = top.value
  const startedOn = escapeAt?.event === e ? escapeAt.layerId : (layer?.id ?? null)
  escapeAt = null
  if (!layer && !startedOn) return
  // Esc belongs to the top layer, so it never reaches the shortcut scopes below (9.5), even when
  // that layer already closed itself.
  e.preventDefault()
  e.stopImmediatePropagation()
  if (layer && startedOn === layer.id) layer.close('escape')
}

function onPointerDown(e: PointerEvent): void {
  const layers = stack.value
  if (!layers.length || !(e.target instanceof Node)) return
  const target = e.target
  let hit = -1
  for (let i = layers.length - 1; i >= 0; i--) {
    if (contains(layers[i], target)) {
      hit = i
      break
    }
  }
  // Close from the top down; a modal layer shields everything below it.
  for (let i = layers.length - 1; i > hit; i--) {
    const layer = layers[i]
    if (layer.modal) break
    if (layer.closeOnOutside === false) continue
    layer.close('outside')
  }
}

// A layer closed by an Enter keydown (an input's Enter, a menu item) returns focus while the key is
// still down. The browser then delivers the Enter keypress to the opener, and a button opener
// would be clicked again (reopening the menu or dialog). That one keypress is swallowed.
let enterDown = false

function onKeyState(e: KeyboardEvent): void {
  if (e.key === 'Enter') enterDown = e.type === 'keydown'
}

function swallowEnterPress(): void {
  const swallow = (e: KeyboardEvent): void => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    e.stopImmediatePropagation()
    done()
  }
  const done = (): void => {
    clearTimeout(timer)
    document.removeEventListener('keypress', swallow, true)
    document.removeEventListener('keyup', done, true)
  }
  const timer = setTimeout(done, 500)
  document.addEventListener('keypress', swallow, true)
  document.addEventListener('keyup', done, true)
}

function ensureListeners(): void {
  if (listening || typeof document === 'undefined') return
  listening = true
  document.addEventListener('keydown', onKeydown)
  document.addEventListener('keydown', onKeydownCapture, true)
  document.addEventListener('keydown', onKeyState, true)
  document.addEventListener('keyup', onKeyState, true)
  document.addEventListener('pointerdown', onPointerDown, true)
}
// Registered when the module loads (App.vue's hosts import it), so the stack's Esc handler is the
// first document listener and runs before any shortcut handler registered later.
ensureListeners()

/** Where focus goes when the layer closes: its nearest opener still usable, else its fallback. */
function returnTargetOf(layer: Layer): HTMLElement | null {
  const opener = layer.openers.find(isUsable)
  if (opener) return opener
  const fallback = layer.returnFocusTo?.() ?? null
  return fallback && isUsable(fallback) ? fallback : null
}

function removeLayer(id: string, o?: { returnFocus?: boolean }): void {
  const layers = stack.value
  const index = layers.findIndex((l) => l.id === id)
  if (index === -1) return
  const layer = layers[index]
  const active = document.activeElement
  // Focus counts as inside when it is in this layer or in a layer opened on top of it.
  const focusInside =
    !active ||
    active === document.body ||
    !active.isConnected ||
    layers.slice(index).some((l) => contains(l, active))
  stack.value = layers.filter((l) => l.id !== id)
  syncModalState()
  const wantsReturn = (o?.returnFocus ?? layer.returnFocus) !== false
  if (!wantsReturn || !focusInside) return
  const target = returnTargetOf(layer)
  if (!target) return
  if (enterDown) swallowEnterPress()
  target.focus({ preventScroll: true })
}

function push(config: LayerConfig): LayerHandle {
  ensureListeners()
  const active = typeof document !== 'undefined' ? document.activeElement : null
  const opener = active instanceof HTMLElement && active !== document.body ? active : null
  const owner = opener ? layerOf(opener) : null
  const openers = opener ? [opener, ...(owner?.openers ?? [])] : []
  const layer: Layer = { ...config, id: `layer-${++seq}`, opener, openers }
  stack.value = [...stack.value, layer]
  syncModalState()
  let removed = false
  return {
    id: layer.id,
    isTop: computed(() => top.value?.id === layer.id),
    remove(o) {
      if (removed) return
      removed = true
      removeLayer(layer.id, o)
    },
  }
}

/** The topmost layer that contains the node (its element or its ignored elements). */
function layerOf(node: Node | null | undefined): Layer | null {
  if (!node) return null
  const layers = stack.value
  for (let i = layers.length - 1; i >= 0; i--) if (contains(layers[i], node)) return layers[i]
  return null
}

/**
 * Whether a focus trap on `container` lets focus go to `target`: inside the container, inside any
 * layer stacked above the container's layer (a picker opened from a dialog), or inside a region
 * marked data-layer-allow (the notifications region).
 */
function allowsFocus(container: HTMLElement, target: Node): boolean {
  if (container.contains(target)) return true
  if (target instanceof Element && target.closest('[data-layer-allow]')) return true
  const layers = stack.value
  const index = layers.findIndex((l) => {
    const el = l.el()
    return !!el && (el === container || el.contains(container))
  })
  if (index === -1) return false
  for (let i = index + 1; i < layers.length; i++) if (contains(layers[i], target)) return true
  return false
}

/** The element focus would return to if the layer closed now (null when there is none). */
function returnTarget(id: string): HTMLElement | null {
  const layer = stack.value.find((l) => l.id === id)
  return layer ? returnTargetOf(layer) : null
}

export function useLayerStack(): {
  layers: ComputedRef<readonly Layer[]>
  top: ComputedRef<Layer | null>
  hasModal: ComputedRef<boolean>
  push(config: LayerConfig): LayerHandle
  isTop(id: string): boolean
  layerOf(node: Node | null | undefined): Layer | null
  allowsFocus(container: HTMLElement, target: Node): boolean
  returnTarget(id: string): HTMLElement | null
} {
  return {
    layers: computed(() => stack.value),
    top,
    hasModal,
    push,
    isTop: (id) => top.value?.id === id,
    layerOf,
    allowsFocus,
    returnTarget,
  }
}

/**
 * Registers a layer while `active` is true and removes it when it turns false or the component
 * unmounts. The opener is captured when `active` turns true, before focus moves into the layer.
 */
export function useLayer(
  active: WatchSource<boolean>,
  config: LayerConfig,
): {
  id: Ref<string | null>
  isTop: ComputedRef<boolean>
  /** Position in the stack (0 = bottom), -1 while not registered. */
  index: ComputedRef<number>
  /**
   * The z-index to render at (see LEVEL_Z). It keeps its last value after the layer closes, so a
   * leave transition doesn't drop under the layer below.
   */
  z: ComputedRef<number>
} {
  let handle: LayerHandle | null = null
  const id = ref<string | null>(null)
  watch(
    active,
    (on) => {
      if (on && !handle) {
        handle = push(config)
        id.value = handle.id
      } else if (!on && handle) {
        handle.remove()
        handle = null
        id.value = null
      }
    },
    { immediate: true },
  )
  if (getCurrentInstance()) {
    onBeforeUnmount(() => {
      handle?.remove()
      handle = null
      id.value = null
    })
  }
  let lastZ = LEVEL_Z[levelOf(config)]
  return {
    id,
    isTop: computed(() => !!id.value && top.value?.id === id.value),
    index: computed(() => (id.value ? stack.value.findIndex((l) => l.id === id.value) : -1)),
    z: computed(() => {
      const z = id.value ? zIndexes.value.get(id.value) : undefined
      if (z !== undefined) lastZ = z
      return lastZ
    }),
  }
}

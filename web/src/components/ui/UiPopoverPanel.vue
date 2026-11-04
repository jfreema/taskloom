<script setup lang="ts">
// Internal to UiPopover: the floating surface. It is mounted only while the popover is open, so
// useFloating and autoUpdate exist only then (7.10). It registers the non-modal layer (7.7), places
// the initial focus, closes when focus leaves it, and on phones takes the viewport width minus 16px
// and at most 60dvh while staying anchored.
import { computed, onMounted, ref } from 'vue'
import { autoUpdate, flip, offset as offsetMiddleware, shift, size, useFloating, type Placement } from '@floating-ui/vue'
import { useLayer, useLayerStack } from '../../composables/useLayerStack'
import { tabbables, useFocusTrap } from '../../composables/useFocusTrap'
import type { PopoverAnchor, PopoverCloseReason, PopoverRole } from './popover'

type Dismiss = Exclude<PopoverCloseReason, 'programmatic' | 'select'>

const props = defineProps<{
  /** The popover's open state: the layer is removed as soon as it turns false, before the fade ends. */
  active: boolean
  reference: PopoverAnchor | null
  trigger: HTMLElement | null
  placement: Placement
  offset: number | { mainAxis?: number; crossAxis?: number }
  matchWidth: boolean
  width?: number | 'auto'
  role: PopoverRole
  ariaLabel?: string
  trapFocus: boolean
  initialFocus: string
  closeOnOutside: boolean
  returnFocus: boolean
}>()

const emit = defineEmits<{ dismiss: [reason: Dismiss] }>()

const el = ref<HTMLElement | null>(null)
const reference = computed(() => props.reference)
const stack = useLayerStack()

// The size middleware publishes the room left (and the anchor's width) as custom properties; the
// stylesheet turns them into max-height and min-width, so attribute styles (a menu's min-width)
// are never overwritten.
const middleware = computed(() => [
  offsetMiddleware(props.offset),
  flip({ padding: 8 }),
  shift({ padding: 8 }),
  size({
    padding: 8,
    apply({ availableHeight, rects, elements }) {
      const s = elements.floating.style
      s.setProperty('--popover-max-h', `${Math.max(0, Math.floor(availableHeight))}px`)
      s.setProperty('--popover-anchor-w', `${Math.round(rects.reference.width)}px`)
    },
  }),
])

const { floatingStyles, placement: resolved } = useFloating(reference, el, {
  placement: () => props.placement,
  strategy: 'fixed',
  transform: false,
  middleware,
  whileElementsMounted: autoUpdate,
})

const side = computed(() => resolved.value.split('-')[0])
const origin = computed(() => {
  const [s, align] = resolved.value.split('-')
  const across = s === 'top' || s === 'bottom'
  const edge = align === 'start' ? (across ? 'left' : 'top') : align === 'end' ? (across ? 'right' : 'bottom') : 'center'
  const from = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' }[s] ?? 'top'
  return across ? `${edge} ${from}` : `${from} ${edge}`
})

const layer = useLayer(() => props.active, {
  modal: false,
  level: 'popover',
  el: () => el.value,
  close: (reason) => emit('dismiss', reason),
  returnFocusTo: () => props.trigger ?? (props.reference instanceof HTMLElement ? props.reference : null),
  returnFocus: props.returnFocus,
  closeOnOutside: props.closeOnOutside,
  ignore: () => [props.trigger],
})
useFocusTrap(el, computed(() => props.active && props.trapFocus))

const style = computed(() => ({
  ...floatingStyles.value,
  zIndex: layer.z.value,
  transformOrigin: origin.value,
  '--popover-w': typeof props.width === 'number' ? `${props.width}px` : undefined,
}))
const containerRole = computed(() => (props.role === 'dialog' || props.role === 'menu' ? props.role : undefined))

function placeFocus(): void {
  const root = el.value
  if (!root || props.initialFocus === 'none') return
  let target: HTMLElement | null = null
  if (props.initialFocus === 'container') target = root
  else if (props.initialFocus === 'first') target = root.querySelector<HTMLElement>('[data-autofocus]')
  else target = root.querySelector<HTMLElement>(props.initialFocus)
  target = target ?? tabbables(root)[0] ?? root
  target.focus({ preventScroll: true })
}

onMounted(placeFocus)

/** The element focus returns to when this popover closes (the opener, else the trigger). */
function returnTarget(): HTMLElement | null {
  return layer.id.value ? stack.returnTarget(layer.id.value) : props.trigger
}

/**
 * Tab past either end leaves the popover: focus goes back to the opener and the popover closes. A
 * forward Tab then continues from the opener to the next element; Shift+Tab stops on the opener.
 * Menus handle Tab themselves (UiMenu), and trapped popovers wrap.
 */
function onKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Tab' || e.defaultPrevented || props.trapFocus || props.role === 'menu') return
  const root = el.value
  if (!root) return
  const items = tabbables(root)
  const current = document.activeElement
  const leaving = !items.length || (e.shiftKey ? current === items[0] || current === root : current === items[items.length - 1])
  if (!leaving) return
  const target = props.returnFocus ? returnTarget() : null
  if (target) {
    target.focus({ preventScroll: true })
    if (e.shiftKey) e.preventDefault()
  }
  emit('dismiss', 'outside')
}

/** Focus moving somewhere else (not the trigger, not a layer stacked above) closes the popover. */
function onFocusOut(e: FocusEvent): void {
  if (!props.active || props.trapFocus) return
  const next = e.relatedTarget
  const root = el.value
  if (!(next instanceof Node) || !root || root.contains(next)) return
  if (props.trigger?.contains(next)) return
  // The anchor too: a submenu stays open while focus is back on the item that opened it.
  if (props.reference instanceof HTMLElement && props.reference.contains(next)) return
  if (stack.allowsFocus(root, next)) return
  emit('dismiss', 'outside')
}

defineExpose({ el, returnTarget })
</script>

<template>
  <div
    ref="el"
    class="ui-popover"
    :class="{ 'is-closing': !active, 'match-width': matchWidth }"
    :role="containerRole"
    :aria-label="containerRole ? ariaLabel : undefined"
    :data-side="side"
    :style="style"
    tabindex="-1"
    @keydown="onKeydown"
    @focusout="onFocusOut"
  >
    <slot />
  </div>
</template>

<style scoped>
.ui-popover {
  display: flex;
  flex-direction: column;
  width: var(--popover-w, auto);
  min-width: 0;
  max-width: calc(100vw - 16px);
  max-height: var(--popover-max-h, calc(100dvh - 16px));
  overflow: auto;
  overscroll-behavior: contain;
  background: var(--bg-raised);
  border: 1px solid var(--border-floating);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-3);
  color: var(--text-primary);
  outline: none;
}

.ui-popover.match-width {
  min-width: var(--popover-anchor-w, 0);
}

.ui-popover.is-closing {
  pointer-events: none;
}

/* Phones: still anchored, but the viewport width minus 16px and at most 60dvh. */
@media (max-width: 639px) {
  .ui-popover,
  .ui-popover.match-width {
    width: calc(100vw - 16px);
    max-height: min(var(--popover-max-h, 60dvh), 60dvh);
  }
}
</style>

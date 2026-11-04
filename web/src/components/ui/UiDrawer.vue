<script setup lang="ts">
// Drawer (spec 7.10): a panel from the left, the right or the bottom.
// - Modal (default): teleported to #overlay-root with a scrim, role="dialog", a modal layer (7.7)
//   with focus trap, inert #app, scroll lock and focus return. Esc and the scrim close it when
//   `dismissible`.
// - modal=false: no scrim, trap or inert; an <aside role="complementary"> at --z-panel, rendered
//   in place (so it turns inert with the app when a dialog opens). Esc closes it while focus is
//   inside and no layer is open; focus then goes back to what opened it.
// - Left and right drawers slide 24px and fade; bottom sheets rise, radius 16 at the top, at most
//   85dvh, with a decorative 36×4 grabber.
// - Focus starts on the panel itself, or on `initialFocus` (a CSS selector) when given.
import { computed, nextTick, ref, watch } from 'vue'
import { useLayer, useLayerStack } from '../../composables/useLayerStack'
import { useFocusTrap } from '../../composables/useFocusTrap'

export type DrawerCloseReason = 'escape' | 'scrim' | 'programmatic'

const open = defineModel<boolean>('open', { required: true })

const props = withDefaults(
  defineProps<{
    side: 'left' | 'right' | 'bottom'
    /** Left: var(--drawer-w); right: min(640px, 100vw); bottom: full width. */
    width?: string
    modal?: boolean
    ariaLabel?: string
    ariaLabelledby?: string
    dismissible?: boolean
    /** A CSS selector for the element that takes focus on open; default the panel itself. */
    initialFocus?: string
  }>(),
  {
    width: undefined,
    modal: true,
    ariaLabel: undefined,
    ariaLabelledby: undefined,
    dismissible: true,
    initialFocus: undefined,
  },
)

const emit = defineEmits<{ close: [reason: DrawerCloseReason]; 'after-leave': [] }>()

const root = ref<HTMLElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const stack = useLayerStack()
let pendingReason: DrawerCloseReason | null = null

function requestClose(reason: DrawerCloseReason): void {
  if (!open.value || (reason !== 'programmatic' && !props.dismissible)) return
  pendingReason = reason
  open.value = false
}

const modalOpen = computed(() => open.value && props.modal)

const layer = useLayer(modalOpen, {
  modal: true,
  level: 'drawer',
  el: () => root.value,
  close: (reason) => {
    if (reason === 'escape') requestClose('escape')
  },
})
useFocusTrap(panel, modalOpen)

const panelWidth = computed(() => {
  if (props.side === 'bottom') return undefined
  return props.width ?? (props.side === 'left' ? 'var(--drawer-w)' : 'min(640px, 100vw)')
})

// A non-modal drawer isn't a layer, so it returns focus itself: to what had focus when it opened,
// when focus was inside it as it closed.
let opener: HTMLElement | null = null
let shownPanel: HTMLElement | null = null

function returnFocusFromAside(): void {
  const a = document.activeElement
  const inside = !a || a === document.body || !a.isConnected || !!shownPanel?.contains(a)
  const target = opener
  opener = null
  shownPanel = null
  if (inside && target?.isConnected && !target.closest('[inert]')) target.focus({ preventScroll: true })
}

watch(
  open,
  (on, was) => {
    if (on) {
      if (!props.modal) {
        const a = document.activeElement
        opener = a instanceof HTMLElement && a !== document.body ? a : null
      }
      void nextTick(() => {
        const p = panel.value
        if (!p) return
        shownPanel = p
        const target = props.initialFocus ? p.querySelector<HTMLElement>(props.initialFocus) : null
        ;(target ?? p).focus({ preventScroll: true })
      })
    } else if (was) {
      if (!props.modal) returnFocusFromAside()
      emit('close', pendingReason ?? 'programmatic')
      pendingReason = null
    }
  },
  { immediate: true, flush: 'post' },
)

/** Non-modal drawers aren't layers: they close on Esc only while focus is inside and nothing is above. */
function onAsideKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing || stack.top.value) return
  e.preventDefault()
  requestClose('escape')
}

let downOnScrim = false
function onScrimDown(e: PointerEvent): void {
  downOnScrim = e.target === e.currentTarget
}
function onScrimClick(e: MouseEvent): void {
  if (downOnScrim && e.target === e.currentTarget) requestClose('scrim')
  downOnScrim = false
}

defineExpose({ panel, close: () => requestClose('programmatic') })
</script>

<template>
  <Teleport to="#overlay-root" :disabled="!modal">
    <Transition :name="`ui-drawer-${side}`" @after-leave="emit('after-leave')">
      <div
        v-if="open && modal"
        ref="root"
        class="ui-drawer-root"
        :class="`side-${side}`"
        :style="{ zIndex: layer.z.value }"
      >
        <div class="scrim" aria-hidden="true" @pointerdown="onScrimDown" @click="onScrimClick" />
        <div
          ref="panel"
          class="panel"
          role="dialog"
          aria-modal="true"
          :aria-label="ariaLabel"
          :aria-labelledby="ariaLabelledby"
          :style="{ width: panelWidth }"
          tabindex="-1"
        >
          <div v-if="side === 'bottom'" class="grabber" aria-hidden="true" />
          <slot />
        </div>
      </div>
      <aside
        v-else-if="open"
        ref="panel"
        class="panel panel--static"
        :class="`side-${side}`"
        :aria-label="ariaLabel"
        :aria-labelledby="ariaLabelledby"
        :style="{ width: panelWidth }"
        tabindex="-1"
        @keydown="onAsideKeydown"
      >
        <div v-if="side === 'bottom'" class="grabber" aria-hidden="true" />
        <slot />
      </aside>
    </Transition>
  </Teleport>
</template>

<style scoped>
.ui-drawer-root {
  position: fixed;
  inset: 0;
  z-index: var(--z-drawer);
  display: flex;
}

.scrim {
  position: absolute;
  inset: 0;
  background: var(--bg-scrim);
}

.panel {
  position: relative;
  display: flex;
  flex-direction: column;
  max-width: 100vw;
  height: 100%;
  background: var(--bg-raised);
  color: var(--text-primary);
  box-shadow: var(--shadow-5);
  overflow: hidden;
  outline: none;
}

.side-left {
  justify-content: flex-start;
}

.side-left .panel {
  border-right: 1px solid var(--border-floating);
}

.side-right {
  justify-content: flex-end;
}

.side-right .panel {
  border-left: 1px solid var(--border-floating);
}

.side-bottom {
  align-items: flex-end;
}

.side-bottom .panel {
  width: 100%;
  height: auto;
  max-height: 85dvh;
  padding-bottom: env(safe-area-inset-bottom, 0px);
  border-top: 1px solid var(--border-floating);
  border-radius: var(--radius-2xl) var(--radius-2xl) 0 0;
}

.grabber {
  flex-shrink: 0;
  width: 36px;
  height: 4px;
  margin: var(--space-2) auto var(--space-1);
  border-radius: var(--radius-full);
  background: var(--border-default);
}

/* Non-modal: an aside over the page at --z-panel, without a scrim. */
.panel--static {
  position: fixed;
  top: 0;
  bottom: 0;
  z-index: var(--z-panel);
  box-shadow: var(--shadow-3);
}

.panel--static.side-left {
  left: 0;
  border-right: 1px solid var(--border-subtle);
}

.panel--static.side-right {
  right: 0;
  border-left: 1px solid var(--border-subtle);
}

.panel--static.side-bottom {
  top: auto;
  left: 0;
  right: 0;
  height: auto;
  max-height: 85dvh;
  border-top: 1px solid var(--border-subtle);
  border-radius: var(--radius-2xl) var(--radius-2xl) 0 0;
}

/*
 * Slide 24px with a fade (7.8); bottom sheets rise. Exits run at about 70% of the entrance. The
 * transitioned root lists the same durations so Vue waits for its children.
 */
.ui-drawer-left-enter-active,
.ui-drawer-right-enter-active,
.ui-drawer-bottom-enter-active,
.ui-drawer-left-enter-active .panel,
.ui-drawer-right-enter-active .panel,
.ui-drawer-bottom-enter-active .panel,
.ui-drawer-left-enter-active .scrim,
.ui-drawer-right-enter-active .scrim,
.ui-drawer-bottom-enter-active .scrim {
  transition:
    opacity var(--dur-3) var(--ease-out),
    transform var(--dur-3) var(--ease-out);
}

.ui-drawer-left-leave-active,
.ui-drawer-right-leave-active,
.ui-drawer-bottom-leave-active,
.ui-drawer-left-leave-active .panel,
.ui-drawer-right-leave-active .panel,
.ui-drawer-bottom-leave-active .panel,
.ui-drawer-left-leave-active .scrim,
.ui-drawer-right-leave-active .scrim,
.ui-drawer-bottom-leave-active .scrim {
  transition:
    opacity calc(var(--dur-3) * 0.7) var(--ease-in),
    transform calc(var(--dur-3) * 0.7) var(--ease-in);
}

.ui-drawer-left-enter-from .scrim,
.ui-drawer-left-leave-to .scrim,
.ui-drawer-right-enter-from .scrim,
.ui-drawer-right-leave-to .scrim,
.ui-drawer-bottom-enter-from .scrim,
.ui-drawer-bottom-leave-to .scrim,
.ui-drawer-left-enter-from .panel,
.ui-drawer-left-leave-to .panel,
.ui-drawer-right-enter-from .panel,
.ui-drawer-right-leave-to .panel,
.ui-drawer-bottom-enter-from .panel,
.ui-drawer-bottom-leave-to .panel,
.ui-drawer-left-enter-from.panel--static,
.ui-drawer-left-leave-to.panel--static,
.ui-drawer-right-enter-from.panel--static,
.ui-drawer-right-leave-to.panel--static,
.ui-drawer-bottom-enter-from.panel--static,
.ui-drawer-bottom-leave-to.panel--static {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .ui-drawer-left-enter-from .panel,
  .ui-drawer-left-leave-to .panel,
  .ui-drawer-left-enter-from.panel--static,
  .ui-drawer-left-leave-to.panel--static {
    transform: translateX(-24px);
  }

  .ui-drawer-right-enter-from .panel,
  .ui-drawer-right-leave-to .panel,
  .ui-drawer-right-enter-from.panel--static,
  .ui-drawer-right-leave-to.panel--static {
    transform: translateX(24px);
  }

  .ui-drawer-bottom-enter-from .panel,
  .ui-drawer-bottom-leave-to .panel,
  .ui-drawer-bottom-enter-from.panel--static,
  .ui-drawer-bottom-leave-to.panel--static {
    transform: translateY(24px);
  }
}
</style>

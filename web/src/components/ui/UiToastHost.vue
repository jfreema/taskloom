<script setup lang="ts">
// Renders useToast()'s queue (spec 7.10): bottom-left, 16px from the edges, 360px wide, on
// --bg-inverse. On phones it is full width minus 32px and sits above the FAB. Hover or focus pauses
// every timer. It lives in #overlay-root (marked data-layer-allow), so its buttons stay usable while
// a modal dialog makes #app inert.
// - Each toast is a role="status" (role="alert" for danger) element holding the message and its
//   buttons. They are plain divs: a list item can't take another role.
// - Mod+Z runs the newest Undo (9.2). It is registered here, once for the app, and only when focus
//   isn't in a text field, where the browser's own undo applies. With nothing to undo the key
//   passes on.
// - On phones dialogs are bottom sheets (UiDialog, and UiDrawer from the bottom; both mark their
//   panel data-sheet). While one is open the toasts rise above it when they fit there, so they
//   never cover its fields; above a full-height sheet there is no room and they keep their place.
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { CircleAlert, CircleCheck, X } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useLayerStack } from '../../composables/useLayerStack'
import { useShortcut } from '../../composables/useShortcut'
import { useToast, useToastHost } from '../../composables/useToast'
import UiIcon from './UiIcon.vue'
import UiIconButton from './UiIconButton.vue'

const { toasts, runAction, dismiss, pauseAll, resumeAll } = useToastHost()
const { undoLatest } = useToast()

useShortcut('Mod+Z', () => undoLatest())

function onFocusOut(e: FocusEvent): void {
  const region = e.currentTarget as HTMLElement
  if (!(e.relatedTarget instanceof Node) || !region.contains(e.relatedTarget)) resumeAll('focus')
}

const SHEET_GAP = 8
const TOP_MARGIN = 16

const layers = useLayerStack()
const { isPhone } = useBreakpoint()
const host = ref<HTMLElement | null>(null)
/** The topmost open bottom sheet (phones only). */
const sheet = computed<HTMLElement | null>(() => {
  if (!isPhone.value) return null
  const all = layers.layers.value
  for (let i = all.length - 1; i >= 0; i--) {
    const panel = all[i].el()?.querySelector<HTMLElement>(':scope > [data-sheet]')
    if (panel) return panel
  }
  return null
})
/** The host's distance from the bottom edge while it sits above a sheet, else null. */
const lift = ref<number | null>(null)

function measure(): void {
  const panel = sheet.value
  const el = host.value
  if (!panel || !el) {
    lift.value = null
    return
  }
  // The sheet sits on the bottom edge; offsetHeight ignores its rise transform.
  const above = panel.offsetHeight + SHEET_GAP
  lift.value = el.offsetHeight <= window.innerHeight - above - TOP_MARGIN ? above : null
}

let observer: ResizeObserver | null = null
function stopWatching(): void {
  observer?.disconnect()
  observer = null
  window.removeEventListener('resize', measure)
}
watch(
  sheet,
  (panel) => {
    stopWatching()
    if (panel) {
      if (typeof ResizeObserver !== 'undefined') {
        observer = new ResizeObserver(measure)
        observer.observe(panel)
        if (host.value) observer.observe(host.value)
      }
      window.addEventListener('resize', measure, { passive: true })
    }
    measure()
  },
  { flush: 'post' },
)
onBeforeUnmount(stopWatching)

const hostStyle = computed(() => (lift.value === null ? undefined : { bottom: `${lift.value}px` }))
</script>

<template>
  <Teleport to="#overlay-root">
    <section
      ref="host"
      class="ui-toast-host"
      :style="hostStyle"
      aria-label="Notifications"
      data-layer-allow
      @pointerenter="pauseAll('hover')"
      @pointerleave="resumeAll('hover')"
      @focusin="pauseAll('focus')"
      @focusout="onFocusOut"
    >
      <TransitionGroup name="toast" tag="div" class="list">
        <div
          v-for="t in toasts"
          :key="t.id"
          class="toast surface-inverse"
          :class="`tone-${t.tone}`"
          :role="t.tone === 'danger' ? 'alert' : 'status'"
        >
          <UiIcon v-if="t.tone === 'danger'" class="tone-icon danger" :icon="CircleAlert" :size="16" />
          <UiIcon v-else-if="t.tone === 'success'" class="tone-icon success" :icon="CircleCheck" :size="16" />
          <p class="message">{{ t.message }}</p>
          <button v-if="t.action" type="button" class="action" @click="runAction(t.id)">{{ t.action.label }}</button>
          <UiIconButton class="close" :icon="X" label="Dismiss" size="xs" tone="inverse" @click="dismiss(t.id)" />
        </div>
      </TransitionGroup>
    </section>
  </Teleport>
</template>

<style scoped>
.ui-toast-host {
  position: fixed;
  left: var(--space-4);
  bottom: calc(var(--space-4) + env(safe-area-inset-bottom, 0px));
  z-index: var(--z-toast);
  width: 360px;
  max-width: calc(100vw - 32px);
  pointer-events: none;
}

.list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.toast {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  width: 100%;
  min-height: 44px;
  padding: var(--space-2) var(--space-2) var(--space-2) var(--space-3);
  border: 1px solid var(--border-floating);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-4);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  pointer-events: auto;
}

.tone-icon {
  margin-top: 5px;
}

.tone-icon.danger {
  color: var(--text-inverse-danger);
}

.tone-icon.success {
  color: var(--text-inverse-accent);
}

.message {
  flex: 1;
  min-width: 0;
  padding: 5px 0;
  overflow-wrap: anywhere;
}

.action {
  flex-shrink: 0;
  height: var(--control-sm);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-inverse-accent);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  white-space: nowrap;
  transition: background-color var(--dur-1) var(--ease-out);
}

.action:hover {
  background: color-mix(in srgb, var(--text-inverse) 12%, transparent);
}

.close {
  margin-top: 2px;
}

/* Rise 8px with a fade (7.8). */
.toast-enter-active {
  transition: opacity var(--dur-3) var(--ease-out), transform var(--dur-3) var(--ease-out);
}

.toast-leave-active {
  transition: opacity calc(var(--dur-3) * 0.7) var(--ease-in);
}

.toast-enter-from,
.toast-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .toast-enter-from {
    transform: translateY(8px);
  }

  .toast-move {
    transition: transform var(--dur-3) var(--ease-in-out);
  }

  .ui-toast-host {
    transition: bottom var(--dur-3) var(--ease-out);
  }
}

/* Phones: full width minus 32px, above the 56px FAB. */
@media (max-width: 639px) {
  .ui-toast-host {
    left: var(--space-4);
    right: var(--space-4);
    width: auto;
    bottom: calc(var(--space-4) + 56px + var(--space-4) + env(safe-area-inset-bottom, 0px));
  }
}
</style>

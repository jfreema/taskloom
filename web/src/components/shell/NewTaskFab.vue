<script setup lang="ts">
// The phone "New task" button (3.6, 7.12): a 56px round accent button, bottom-right, 16px plus the
// safe area from the edges, at --z-bulkbar. Shown on Home and list pages below 640px for Member+.
// Hidden while tasks are selected, while a modal layer is open and while a text field has focus
// (the on-screen keyboard is up). Opens quick create with the page's defaults; focus comes back to
// the button when quick create closes.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Plus } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useLayerStack } from '../../composables/useLayerStack'
import { isTypingTarget } from '../../composables/useShortcut'
import { useSelection } from '../../stores/selection'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import UiIcon from '../ui/UiIcon.vue'
import { PANEL_ROUTE_NAMES } from './shellContext'

const route = useRoute()
const ui = useUi()
const workspaces = useWorkspaces()
const selection = useSelection()
const layers = useLayerStack()
const { isPhone } = useBreakpoint()

const typing = ref(false)

function syncTyping(): void {
  typing.value = isTypingTarget(document.activeElement)
}

// focusout fires before focus lands on the next element; read the result after it settles.
function onFocusOut(): void {
  queueMicrotask(syncTyping)
}

onMounted(() => {
  syncTyping()
  document.addEventListener('focusin', syncTyping)
  document.addEventListener('focusout', onFocusOut)
})
onBeforeUnmount(() => {
  document.removeEventListener('focusin', syncTyping)
  document.removeEventListener('focusout', onFocusOut)
})

const visible = computed(
  () =>
    isPhone.value &&
    typeof route.name === 'string' &&
    PANEL_ROUTE_NAMES.has(route.name) &&
    workspaces.canEdit() &&
    selection.count === 0 &&
    !layers.hasModal.value &&
    !typing.value,
)

const button = ref<HTMLButtonElement | null>(null)
/** Quick create was opened from here: the button hides under the dialog, so the layer stack can't return focus to it. */
let returnFocus = false

function open(): void {
  returnFocus = true
  ui.openQuickCreate(ui.createDefaults ?? undefined)
}

watch(visible, (on) => {
  if (!on || !returnFocus) return
  returnFocus = false
  void nextTick(() => {
    const a = document.activeElement
    if (!a || a === document.body || !a.isConnected) button.value?.focus({ preventScroll: true })
  })
})
</script>

<template>
  <Transition name="fab">
    <button v-if="visible" ref="button" v-tooltip="'New task'" type="button" class="fab" aria-label="New task" @click="open">
      <UiIcon :icon="Plus" :size="24" />
    </button>
  </Transition>
</template>

<style scoped>
.fab {
  position: fixed;
  right: calc(var(--space-4) + env(safe-area-inset-right, 0px));
  bottom: calc(var(--space-4) + env(safe-area-inset-bottom, 0px));
  z-index: var(--z-bulkbar);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  border-radius: var(--radius-full);
  background: var(--accent);
  color: var(--accent-fg);
  box-shadow: var(--shadow-4);
  -webkit-tap-highlight-color: transparent;
  transition: background-color var(--dur-1) var(--ease-out);
}

.fab:hover {
  background: var(--accent-hover);
}

.fab:active {
  background: var(--accent-pressed);
}

.fab-enter-active {
  transition:
    opacity var(--dur-3) var(--ease-out),
    transform var(--dur-3) var(--ease-out);
}

.fab-leave-active {
  transition:
    opacity calc(var(--dur-3) * 0.7) var(--ease-in),
    transform calc(var(--dur-3) * 0.7) var(--ease-in);
}

.fab-enter-from,
.fab-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .fab-enter-from,
  .fab-leave-to {
    transform: scale(0.85);
  }
}
</style>

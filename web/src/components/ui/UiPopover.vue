<script setup lang="ts">
// Anchored popover (spec 7.10): the base of menus, selects and every picker.
// - The trigger slot spreads `props` on its element: the ref, aria-haspopup, aria-expanded,
//   aria-controls, the click handler and ArrowDown to open.
// - The content teleports to #overlay-root (so scroll containers never clip it) and exists only
//   while open. It registers a non-modal layer (7.7): Esc closes the top layer, outside clicks
//   close it, and focus returns to the opener.
// - Floating UI flips and shifts it at the viewport edges and caps its height to the space left.
// - Attributes (class, aria-labelledby, @keydown…) go to the floating panel.
// role 'listbox' puts no role on the panel: the UiListbox inside it is the listbox.
import { computed, shallowRef, useId, watch, type ComponentPublicInstance } from 'vue'
import type { Placement } from '@floating-ui/vue'
import UiPopoverPanel from './UiPopoverPanel.vue'
import { toElement, type PopoverAnchor, type PopoverCloseReason, type PopoverRole, type PopoverTriggerProps } from './popover'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    placement?: Placement
    /** The gap to the anchor in px, or Floating UI's { mainAxis, crossAxis }. */
    offset?: number | { mainAxis?: number; crossAxis?: number }
    /** At least as wide as the trigger. */
    matchWidth?: boolean
    width?: number | 'auto'
    role?: PopoverRole
    ariaLabel?: string
    trapFocus?: boolean
    /** 'first' (an element marked data-autofocus, else the first focusable one), 'container', 'none' or a CSS selector. */
    initialFocus?: 'first' | 'container' | 'none' | (string & {})
    /** Positions against this instead of the trigger (pickers opened by a shortcut, 7.11). */
    anchor?: PopoverAnchor | null
    closeOnOutside?: boolean
    returnFocus?: boolean
  }>(),
  {
    placement: 'bottom-start',
    offset: 6,
    matchWidth: false,
    width: undefined,
    role: 'dialog',
    ariaLabel: undefined,
    trapFocus: false,
    initialFocus: 'first',
    anchor: null,
    closeOnOutside: true,
    returnFocus: true,
  },
)

const emit = defineEmits<{ close: [reason: PopoverCloseReason] }>()

defineSlots<{
  trigger?(p: { open: boolean; toggle: () => void; props: PopoverTriggerProps }): unknown
  default?(p: { close: (reason?: PopoverCloseReason) => void }): unknown
}>()

const contentId = `${useId()}-popover`
const triggerEl = shallowRef<HTMLElement | null>(null)
const panel = shallowRef<InstanceType<typeof UiPopoverPanel> | null>(null)
let pendingReason: PopoverCloseReason | null = null

function setTriggerRef(x: Element | ComponentPublicInstance | null): void {
  triggerEl.value = toElement(x)
}

function requestClose(reason: PopoverCloseReason = 'programmatic'): void {
  if (!open.value) return
  pendingReason = reason
  open.value = false
}

function show(): void {
  open.value = true
}

function toggle(): void {
  if (open.value) requestClose('programmatic')
  else show()
}

function onTriggerKeydown(e: KeyboardEvent): void {
  if (e.key !== 'ArrowDown' || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || open.value) return
  e.preventDefault()
  show()
}

watch(open, (on, was) => {
  if (!on && was) {
    emit('close', pendingReason ?? 'programmatic')
    pendingReason = null
  }
})

const haspopup = computed(() => (props.role === 'none' ? undefined : props.role))

const triggerProps = computed<PopoverTriggerProps>(() => ({
  ref: setTriggerRef,
  'aria-haspopup': haspopup.value,
  'aria-expanded': open.value ? 'true' : 'false',
  'aria-controls': open.value ? contentId : undefined,
  onClick: toggle,
  onKeydown: onTriggerKeydown,
}))

const reference = computed<PopoverAnchor | null>(() => props.anchor ?? triggerEl.value)

function close(reason: PopoverCloseReason = 'select'): void {
  requestClose(reason)
}

defineExpose({
  show,
  close: requestClose,
  /** The trigger element (null without a trigger slot). */
  triggerEl,
  /** The floating panel while open. */
  panelEl: computed(() => panel.value?.el ?? null),
  /** Where focus goes when the popover closes: its opener, else the trigger. */
  returnTarget: (): HTMLElement | null => panel.value?.returnTarget() ?? triggerEl.value,
})
</script>

<template>
  <slot name="trigger" :open="open" :toggle="toggle" :props="triggerProps" />
  <Teleport to="#overlay-root">
    <Transition name="ui-popover">
      <UiPopoverPanel
        v-if="open"
        :id="contentId"
        ref="panel"
        v-bind="$attrs"
        :active="open"
        :reference="reference"
        :trigger="triggerEl"
        :placement="placement"
        :offset="offset"
        :match-width="matchWidth"
        :width="width"
        :role="role"
        :aria-label="ariaLabel"
        :trap-focus="trapFocus"
        :initial-focus="initialFocus"
        :close-on-outside="closeOnOutside"
        :return-focus="returnFocus"
        @dismiss="requestClose"
      >
        <slot :close="close" />
      </UiPopoverPanel>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Fade, slide 4px from the anchor side and scale .98 → 1 (7.8). */
.ui-popover-enter-active {
  transition:
    opacity var(--dur-2) var(--ease-out),
    transform var(--dur-2) var(--ease-out);
}

.ui-popover-leave-active {
  transition:
    opacity calc(var(--dur-2) * 0.7) var(--ease-in),
    transform calc(var(--dur-2) * 0.7) var(--ease-in);
}

.ui-popover-enter-from,
.ui-popover-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .ui-popover-enter-from[data-side="bottom"],
  .ui-popover-leave-to[data-side="bottom"] {
    transform: translateY(-4px) scale(0.98);
  }

  .ui-popover-enter-from[data-side="top"],
  .ui-popover-leave-to[data-side="top"] {
    transform: translateY(4px) scale(0.98);
  }

  .ui-popover-enter-from[data-side="right"],
  .ui-popover-leave-to[data-side="right"] {
    transform: translateX(-4px) scale(0.98);
  }

  .ui-popover-enter-from[data-side="left"],
  .ui-popover-leave-to[data-side="left"] {
    transform: translateX(4px) scale(0.98);
  }
}
</style>

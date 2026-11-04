<script setup lang="ts">
// A UiPopover that mounts only once it is needed (the "lazy popovers" of 8.1 and S10). While
// `lazy` is set and the popover has never opened, only its trigger renders, with the attributes of
// a closed trigger; UiPopover, with its Teleport and Transition, isn't created. The List sets
// `lazy` on rows that neither the pointer nor focus has reached yet, and a row arms its pickers on
// pointerenter or focusin, before any click lands. A click (or ArrowDown) on a trigger that isn't
// armed still works: it arms the popover and opens it on the next tick.
import { computed, nextTick, ref } from 'vue'
import UiPopover from '../ui/UiPopover.vue'
import type { PopoverCloseReason, PopoverRole, PopoverTriggerProps } from '../ui/popover'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(defineProps<{ lazy?: boolean; role?: PopoverRole }>(), { lazy: false, role: 'dialog' })

defineSlots<{
  trigger?(p: { open: boolean; toggle: () => void; props: PopoverTriggerProps }): unknown
  default?(p: { close: (reason?: PopoverCloseReason) => void }): unknown
}>()

const armed = ref(false)
const live = computed(() => !props.lazy || armed.value || open.value)

function arm(): void {
  armed.value = true
  void nextTick(() => {
    open.value = true
  })
}

const closedTrigger = computed<PopoverTriggerProps>(() => ({
  ref: () => {},
  'aria-haspopup': props.role === 'none' ? undefined : props.role,
  'aria-expanded': 'false',
  onClick: arm,
  onKeydown: (e: KeyboardEvent) => {
    if (e.key !== 'ArrowDown' || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
    e.preventDefault()
    arm()
  },
}))
</script>

<template>
  <UiPopover v-if="live" v-model:open="open" v-bind="$attrs" :role="role">
    <template v-if="$slots.trigger" #trigger="s">
      <slot name="trigger" v-bind="s" />
    </template>
    <template #default="s">
      <slot v-bind="s" />
    </template>
  </UiPopover>
  <slot v-else name="trigger" :open="false" :toggle="arm" :props="closedTrigger" />
</template>

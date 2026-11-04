<script setup lang="ts">
// A UiMenu that mounts only once it is needed, as LazyPopover does for popovers: while `lazy` is
// set and the menu has never opened, only its trigger renders (a closed menu button); a click or
// ArrowDown on it arms the menu and opens it on the next tick. Used by the List's row menus and
// priority pickers, so a long list doesn't create a menu, a popover, a Teleport and a Transition
// per row.
import { computed, nextTick, ref } from 'vue'
import type { Placement } from '@floating-ui/vue'
import type { MenuItem } from '../ui/menu'
import type { PopoverAnchor, PopoverTriggerProps } from '../ui/popover'
import UiMenu from '../ui/UiMenu.vue'

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    items: MenuItem[] | (() => MenuItem[])
    lazy?: boolean
    placement?: Placement
    anchor?: PopoverAnchor | null
    ariaLabel?: string
    minWidth?: number
  }>(),
  { lazy: false, placement: 'bottom-start', anchor: null, ariaLabel: undefined, minWidth: 200 },
)

const emit = defineEmits<{ select: [item: MenuItem] }>()

defineSlots<{
  trigger?(p: { open: boolean; toggle: () => void; props: PopoverTriggerProps }): unknown
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
  'aria-haspopup': 'menu',
  'aria-expanded': 'false',
  onClick: arm,
  onKeydown: (e: KeyboardEvent) => {
    if ((e.key !== 'ArrowDown' && e.key !== 'ArrowUp') || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
    e.preventDefault()
    arm()
  },
}))
</script>

<template>
  <UiMenu
    v-if="live"
    v-model:open="open"
    :items="items"
    :placement="placement"
    :anchor="anchor"
    :aria-label="ariaLabel"
    :min-width="minWidth"
    @select="emit('select', $event)"
  >
    <template v-if="$slots.trigger" #trigger="s">
      <slot name="trigger" v-bind="s" />
    </template>
  </UiMenu>
  <slot v-else name="trigger" :open="false" :toggle="arm" :props="closedTrigger" />
</template>

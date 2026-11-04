<script setup lang="ts">
// Menu (spec 7.10): role="menu" with menuitem, menuitemcheckbox and menuitemradio items, groups
// (a label item heads the items after it), separators, swatches items, kbd hints and submenus.
// - Keys: Up and Down move (wrapping), Home and End jump, typing a letter moves to the next item
//   that starts with it, Enter or Space selects, ArrowRight or Enter opens a submenu, ArrowLeft or
//   Esc closes it, Esc or Tab closes the menu.
// - Checkbox items keep the menu open; every other kind closes it.
// - Submenus open on click, Enter, ArrowRight or 150ms of hover, and close on ArrowLeft, Esc, or
//   when the pointer moves to another item of the parent.
// - Phones: a full-width anchored popover; a submenu replaces the list, with a Back row on top.
// - `items` may be a function: it is called when the menu opens (and again when what it reads
//   changes while open, so checkbox items stay current).
import { computed, shallowRef, watch } from 'vue'
import type { Placement } from '@floating-ui/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import UiMenuLevel from './UiMenuLevel.vue'
import type { MenuItem } from './menu'
import type { PopoverAnchor, PopoverTriggerProps } from './popover'

type SubmenuItem = Extract<MenuItem, { kind: 'submenu' }>

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    items: MenuItem[] | (() => MenuItem[])
    placement?: Placement
    anchor?: PopoverAnchor | null
    ariaLabel?: string
    minWidth?: number
  }>(),
  { placement: 'bottom-start', anchor: null, ariaLabel: undefined, minWidth: 200 },
)

const emit = defineEmits<{ select: [item: MenuItem] }>()

defineSlots<{
  trigger?(p: { open: boolean; toggle: () => void; props: PopoverTriggerProps }): unknown
  /** Optional, not in 7.10: text above the items (not focusable), e.g. the account menu's name and email. */
  header?(): unknown
}>()

const { isPhone } = useBreakpoint()
const level = shallowRef<InstanceType<typeof UiMenuLevel> | null>(null)

/** Phones: the submenus entered so far; the last one's items are shown. */
const path = shallowRef<readonly SubmenuItem[]>([])

const resolved = computed<MenuItem[]>(() => {
  if (!open.value) return []
  return typeof props.items === 'function' ? props.items() : props.items
})

/** A submenu entered on a phone, looked up again in the current items so its state stays fresh. */
const current = computed<MenuItem[]>(() => {
  let items = resolved.value
  for (const step of path.value) {
    const found = items.find((i): i is SubmenuItem => i.kind === 'submenu' && i.id === step.id)
    if (!found) return items
    items = found.items
  }
  return items
})

const label = computed(() => (path.value.length ? path.value[path.value.length - 1].label : props.ariaLabel))

watch(open, (on) => {
  if (!on) path.value = []
})
watch(isPhone, () => {
  path.value = []
})

function enter(item: SubmenuItem): void {
  path.value = [...path.value, item]
  level.value?.focusStop('first')
}

function back(): void {
  const left = path.value[path.value.length - 1]
  if (!left) return
  path.value = path.value.slice(0, -1)
  level.value?.focusStop(left.id)
}
</script>

<template>
  <UiMenuLevel
    ref="level"
    v-model:open="open"
    :items="current"
    :placement="placement"
    :anchor="anchor"
    :aria-label="label"
    :min-width="minWidth"
    :inline="isPhone"
    :back="path.length > 0"
    @enter="enter"
    @back="back"
    @select="emit('select', $event)"
  >
    <template v-if="$slots.trigger" #trigger="t">
      <slot name="trigger" :open="t.open" :toggle="t.toggle" :props="t.props" />
    </template>
    <template v-if="$slots.header" #header>
      <slot name="header" />
    </template>
  </UiMenuLevel>
</template>

<script setup lang="ts">
// Internal to UiMenu: one menu surface (a UiPopover with role="menu") and its items. A submenu is
// another UiMenuLevel anchored to its item, so each level is its own layer: Esc and ArrowLeft close
// only the top one, and focus goes back to the item that opened it. On phones (`inline`) submenus
// don't open beside the list: UiMenu swaps the items and this level shows a Back row.
import { computed, nextTick, ref, shallowRef, useId, watch } from 'vue'
import type { Placement } from '@floating-ui/vue'
import UiMenuRows from './UiMenuRows.vue'
import UiPopover from './UiPopover.vue'
import { BACK_ID, createTypeahead, focusMenuStop, focusedStop, menuStops, type MenuActionItem, type MenuItem, type SubmenuItem, type SwatchesItem } from './menu'
import { useSubmenu, type FocusStart } from './menuSubmenu'
import type { PopoverAnchor, PopoverTriggerProps } from './popover'

defineOptions({ name: 'UiMenuLevel' })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    items: MenuItem[]
    placement?: Placement
    offset?: number | { mainAxis?: number; crossAxis?: number }
    anchor?: PopoverAnchor | null
    ariaLabel?: string
    minWidth?: number
    /** 0 for the root menu. */
    depth?: number
    /** Where focus goes when the level opens without its trigger (a shortcut, a submenu). */
    focusStart?: FocusStart
    /** Phones: submenu items ask UiMenu to show their items in place (`enter`). */
    inline?: boolean
    /** Shows a Back row first (a submenu shown in place). */
    back?: boolean
  }>(),
  {
    placement: 'bottom-start',
    offset: 6,
    anchor: null,
    ariaLabel: undefined,
    minWidth: 200,
    depth: 0,
    focusStart: 'first',
    inline: false,
    back: false,
  },
)

const emit = defineEmits<{
  select: [item: MenuItem]
  closeAll: [reason: 'select' | 'tab']
  back: []
  enter: [item: SubmenuItem]
}>()

const uid = useId()
const popover = shallowRef<InstanceType<typeof UiPopover> | null>(null)
const list = ref<HTMLElement | null>(null)
const nested = shallowRef<{ focusStop(target: string): void } | null>(null)

const byId = computed(() => new Map(props.items.map((i) => [i.id, i])))

const {
  subId,
  anchor: subAnchor,
  start: subStart,
  item: openSub,
  open: subOpen,
  cancelHover,
  show: showSub,
  leave: leaveSub,
  hover: hoverSub,
} = useSubmenu({
  byId,
  list,
  inline: () => props.inline,
  enter: (item) => emit('enter', item),
  focusOpen: () => nested.value?.focusStop('first'),
})

// ---- Focus ----

/** 'first', 'last', 'container' or an item id. */
function focusStop(target: string): void {
  const all = menuStops(list.value)
  if (target === 'container') return list.value?.closest<HTMLElement>('[role="menu"]')?.focus({ preventScroll: true })
  if (target === 'first') return focusMenuStop(all[0])
  if (target === 'last') return focusMenuStop(all[all.length - 1])
  focusMenuStop(all.find((el) => el.dataset.menuId === target) ?? all[0])
}

// The root remembers how it was opened: from its trigger by pointer the menu itself takes focus
// (no item lights up under a mouse), by keyboard the first item, by ArrowUp the last.
const openStart = ref<FocusStart>(props.focusStart)

function wrapTrigger(p: PopoverTriggerProps): PopoverTriggerProps {
  return {
    ...p,
    onClick: (e: MouseEvent) => {
      openStart.value = e.detail === 0 ? 'first' : 'container'
      p.onClick(e)
    },
    onKeydown: (e: KeyboardEvent) => {
      const plain = !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
      if (plain && e.key === 'ArrowUp' && !open.value) {
        e.preventDefault()
        openStart.value = 'last'
        open.value = true
        return
      }
      if (plain && e.key === 'ArrowDown') openStart.value = 'first'
      p.onKeydown(e)
    },
  }
}

watch(
  list,
  (el) => {
    if (!el) return
    const start = props.depth === 0 ? openStart.value : props.focusStart
    if (start !== 'none') focusStop(start)
  },
  { flush: 'post' },
)

watch(open, (on) => {
  if (on) return
  leaveSub()
  openStart.value = props.focusStart
})

const menuLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel
  const t = popover.value?.triggerEl
  return t?.getAttribute('aria-label') ?? t?.textContent?.trim() ?? undefined
})

// ---- Choosing ----

function closeAll(reason: 'select' | 'tab'): void {
  if (props.depth > 0) return emit('closeAll', reason)
  if (reason === 'tab') popover.value?.returnTarget()?.focus({ preventScroll: true })
  popover.value?.close(reason === 'tab' ? 'outside' : 'select')
}

function activate(item: MenuItem, how: 'keyboard' | 'pointer'): void {
  if (item.kind === 'separator' || item.kind === 'label' || item.kind === 'swatches' || item.disabled) return
  if (item.kind === 'submenu') return showSub(item, how)
  // Checkbox items keep the menu open; every other kind closes it first.
  if (item.kind !== 'checkbox') closeAll('select')
  item.onSelect()
  emit('select', item)
}

function pickSwatch(item: SwatchesItem, hex: string): void {
  closeAll('select')
  item.onSelect(hex)
  emit('select', item)
}

/** The pointer moves focus with it (not on touch), and hovering opens or closes submenus. */
function onRowPointerMove(item: MenuActionItem, e: PointerEvent): void {
  if (e.pointerType === 'touch') return
  const el = e.currentTarget as HTMLElement
  if (document.activeElement !== el) el.focus({ preventScroll: true })
  hoverSub(item)
}

// ---- Keyboard ----

const typeahead = createTypeahead()

function onKeydown(e: KeyboardEvent): void {
  if (e.defaultPrevented || e.isComposing) return
  const mod = e.altKey || e.ctrlKey || e.metaKey
  const all = menuStops(list.value)
  const n = all.length
  const i = focusedStop(all)
  const id = i >= 0 ? all[i].dataset.menuId : undefined
  const item = id ? byId.value.get(id) : undefined
  const move = (to: number) => {
    e.preventDefault()
    focusMenuStop(all[to])
  }
  switch (e.key) {
    case 'ArrowDown':
      if (!mod && n) move(i < 0 ? 0 : (i + 1) % n)
      return
    case 'ArrowUp':
      if (!mod && n) move(i <= 0 ? n - 1 : i - 1)
      return
    case 'Home':
    case 'PageUp':
      return move(0)
    case 'End':
    case 'PageDown':
      return move(n - 1)
    case 'ArrowRight':
      if (item?.kind === 'submenu' && !item.disabled) {
        e.preventDefault()
        showSub(item, 'keyboard')
      }
      return
    case 'ArrowLeft':
      if (props.depth > 0) open.value = false
      else if (props.back) emit('back')
      else return
      e.preventDefault()
      return
    case 'Enter':
    case ' ':
      if (i < 0) return
      e.preventDefault()
      if (id === BACK_ID) emit('back')
      else if (item) activate(item, 'keyboard')
      return
    case 'Tab':
      // Tab and Shift+Tab close the whole menu; focus goes back to the opener and moves on from there.
      return closeAll('tab')
  }
  // Letters only, and nothing else is consumed: digits stay free for shortcuts like the priority menu's.
  if (e.key.length !== 1 || mod || !/\p{L}/u.test(e.key)) return
  const rows = all.filter((el) => !el.hasAttribute('data-menu-swatches'))
  const to = typeahead(e.key, rows.map((el) => el.dataset.menuLabel ?? ''), focusedStop(rows))
  if (to < 0) return
  e.preventDefault()
  focusMenuStop(rows[to])
}

defineExpose({
  focusStop: (target: string) => void nextTick(() => focusStop(target)),
  returnTarget: () => popover.value?.returnTarget() ?? null,
})
</script>

<template>
  <UiPopover
    ref="popover"
    v-model:open="open"
    role="menu"
    :aria-label="menuLabel"
    :placement="placement"
    :offset="offset"
    :anchor="anchor"
    initial-focus="none"
    class="ui-menu"
    :style="{ minWidth: `${minWidth}px` }"
    @keydown="onKeydown"
  >
    <template v-if="$slots.trigger" #trigger="t">
      <slot name="trigger" :open="t.open" :toggle="t.toggle" :props="wrapTrigger(t.props)" />
    </template>
    <div ref="list" class="list" role="none" @pointerleave="cancelHover">
      <UiMenuRows
        :items="items"
        :uid="uid"
        :back="back"
        :expanded-id="subId"
        @activate="activate($event, 'pointer')"
        @pick="pickSwatch"
        @back="emit('back')"
        @hover="onRowPointerMove"
        @away="leaveSub"
      />
    </div>
    <UiMenuLevel
      v-if="openSub && !inline"
      ref="nested"
      v-model:open="subOpen"
      :items="openSub.items"
      :anchor="subAnchor"
      placement="right-start"
      :offset="{ mainAxis: 2, crossAxis: -5 }"
      :aria-label="openSub.label"
      :min-width="minWidth"
      :depth="depth + 1"
      :focus-start="subStart"
      @select="emit('select', $event)"
      @close-all="closeAll"
    />
  </UiPopover>
</template>

<style scoped>
.list {
  display: flex;
  flex-direction: column;
  padding: var(--space-1);
}
</style>

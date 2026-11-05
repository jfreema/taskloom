<script setup lang="ts">
// Colour picker (spec 7.10): role="radiogroup" with 7 swatches per row, each a role="radio" circle
// named by its colour. Arrow keys move, Enter or Space selects. The selected swatch shows a ring,
// so colour isn't the only cue. A value that isn't one of the colours appears as a "Custom" swatch.
// `menu` is for UiMenu's swatches items: a group of menuitemradio swatches; Up and Down are left to
// the menu, and `pick` fires on every choice (also of the current colour) so the menu can close.
import { computed, nextTick, ref } from 'vue'
import { SWATCHES } from '../../lib/colors'

const model = defineModel<string>({ required: true })

const props = withDefaults(
  defineProps<{
    /** Required in practice; optional in the type so `aria-label="…"` also type-checks (it maps to this prop). */
    ariaLabel?: string
    size?: 20 | 24
    colors?: readonly { name: string; hex: string }[]
    /** Inside a UiMenu (swatches item). */
    menu?: boolean
  }>(),
  { size: 24, colors: () => SWATCHES, menu: false },
)

const emit = defineEmits<{ pick: [hex: string] }>()

const PER_ROW = 7
const root = ref<HTMLElement | null>(null)

const same = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase()

const items = computed(() => {
  const list = props.colors.map((c) => ({ name: c.name, hex: c.hex }))
  if (model.value && !list.some((c) => same(c.hex, model.value))) list.push({ name: 'Custom', hex: model.value })
  return list
})

const selectedIndex = computed(() => items.value.findIndex((c) => same(c.hex, model.value)))
const focusIndex = ref(-1)
const tabIndexOf = (i: number) =>
  props.menu ? -1 : i === (focusIndex.value >= 0 ? focusIndex.value : Math.max(selectedIndex.value, 0)) ? 0 : -1

const swatchRole = computed(() => (props.menu ? 'menuitemradio' : 'radio'))

function focusAt(i: number): void {
  focusIndex.value = i
  void nextTick(() => root.value?.querySelectorAll<HTMLElement>(`[role="${swatchRole.value}"]`)[i]?.focus())
}

function pick(i: number): void {
  const c = items.value[i]
  if (c) {
    model.value = c.hex
    emit('pick', c.hex)
  }
  focusIndex.value = i
}

function onKeydown(e: KeyboardEvent, i: number): void {
  const n = items.value.length
  let next = -1
  if (e.key === 'ArrowRight') next = (i + 1) % n
  else if (e.key === 'ArrowLeft') next = (i - 1 + n) % n
  else if (props.menu && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) return
  else if (e.key === 'ArrowDown') next = i + PER_ROW < n ? i + PER_ROW : i % PER_ROW
  else if (e.key === 'ArrowUp') {
    if (i - PER_ROW >= 0) next = i - PER_ROW
    else {
      const lastRowStart = Math.floor((n - 1) / PER_ROW) * PER_ROW
      next = Math.min(lastRowStart + (i % PER_ROW), n - 1)
    }
  } else if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = n - 1
  else if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    pick(i)
    return
  }
  if (next < 0) return
  e.preventDefault()
  focusAt(next)
}
</script>

<template>
  <div
    ref="root"
    class="ui-color-swatches"
    :role="menu ? 'group' : 'radiogroup'"
    :aria-label="ariaLabel"
    :style="{ '--swatch': `${size}px` }"
    @focusout="focusIndex = -1"
  >
    <button
      v-for="(c, i) in items"
      :key="`${c.name}-${c.hex}`"
      v-tooltip="c.name"
      type="button"
      :role="swatchRole"
      class="swatch"
      :class="{ 'is-selected': i === selectedIndex }"
      :style="{ '--c': c.hex }"
      :aria-label="c.name"
      :aria-checked="i === selectedIndex ? 'true' : 'false'"
      :tabindex="tabIndexOf(i)"
      @click="pick(i)"
      @keydown="onKeydown($event, i)"
      @focus="focusIndex = i"
    />
  </div>
</template>

<style scoped>
.ui-color-swatches {
  display: grid;
  grid-template-columns: repeat(7, var(--swatch));
  gap: var(--space-2);
  padding: var(--space-1);
  width: max-content;
}

.swatch {
  position: relative;
  width: var(--swatch);
  height: var(--swatch);
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: var(--c);
  cursor: pointer;
  transition: box-shadow var(--dur-1) var(--ease-out);
}

.swatch:hover:not(.is-selected) {
  box-shadow: 0 0 0 2px var(--bg-raised), 0 0 0 3px var(--border-control);
}

.swatch.is-selected {
  box-shadow: 0 0 0 2px var(--bg-raised), 0 0 0 4px var(--text-primary);
}

.swatch:focus-visible {
  outline-offset: 4px;
}

.swatch.is-selected:focus-visible {
  outline-offset: 6px;
}

/* Touch (7.5): swatches sit 44px apart, centre to centre, so their 44px hit areas don't overlap. */
@media (pointer: coarse) {
  .ui-color-swatches {
    gap: calc(var(--touch-min) - var(--swatch));
  }

  .swatch::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: var(--touch-min);
    height: var(--touch-min);
    transform: translate(-50%, -50%);
  }
}
</style>

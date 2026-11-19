<script setup lang="ts">
// The list icons (Look 2) as a radiogroup, seven to a row, each drawn in the list's colour (grey
// without one). Like UiColorSwatches: arrow keys move, Home and End jump, Enter or Space selects;
// the chosen icon has a ring, so colour isn't the only cue.
import { computed, nextTick, ref } from 'vue'
import { LIST_ICONS } from '../../lib/listIcons'
import UiIcon from '../ui/UiIcon.vue'

const model = defineModel<string>({ required: true })

defineProps<{
  /** "#RRGGBB" to draw the icons in, or null for grey. */
  color: string | null
  ariaLabel?: string
}>()

const PER_ROW = 7
const root = ref<HTMLElement | null>(null)
const focusIndex = ref(-1)

const selectedIndex = computed(() => Math.max(LIST_ICONS.findIndex((i) => i.name === model.value), 0))
const tabIndexOf = (i: number) => (i === (focusIndex.value >= 0 ? focusIndex.value : selectedIndex.value) ? 0 : -1)

function focusAt(i: number): void {
  focusIndex.value = i
  void nextTick(() => root.value?.querySelectorAll<HTMLElement>('[role="radio"]')[i]?.focus())
}

function onKeydown(e: KeyboardEvent, i: number): void {
  const n = LIST_ICONS.length
  let next = -1
  if (e.key === 'ArrowRight') next = (i + 1) % n
  else if (e.key === 'ArrowLeft') next = (i - 1 + n) % n
  else if (e.key === 'ArrowDown') next = i + PER_ROW < n ? i + PER_ROW : i % PER_ROW
  else if (e.key === 'ArrowUp') next = i - PER_ROW >= 0 ? i - PER_ROW : Math.min(Math.floor((n - 1) / PER_ROW) * PER_ROW + (i % PER_ROW), n - 1)
  else if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = n - 1
  else if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    model.value = LIST_ICONS[i].name
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
    class="list-icon-grid"
    role="radiogroup"
    :aria-label="ariaLabel"
    :style="color ? { '--c': color } : undefined"
    @focusout="focusIndex = -1"
  >
    <button
      v-for="(item, i) in LIST_ICONS"
      :key="item.name"
      v-tooltip="item.label"
      type="button"
      role="radio"
      class="choice"
      :class="{ 'is-selected': i === selectedIndex, 'has-color': !!color }"
      :aria-label="item.label"
      :aria-checked="i === selectedIndex ? 'true' : 'false'"
      :tabindex="tabIndexOf(i)"
      :data-icon="item.name"
      @click="model = item.name"
      @keydown="onKeydown($event, i)"
      @focus="focusIndex = i"
    >
      <UiIcon :icon="item.icon" :size="20" />
    </button>
  </div>
</template>

<style scoped>
.list-icon-grid {
  display: grid;
  grid-template-columns: repeat(7, var(--control-lg));
  gap: var(--space-1);
  width: max-content;
  max-width: 100%;
}

.choice {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--control-lg);
  height: var(--control-lg);
  padding: 0;
  border: 0;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background-color var(--dur-1) var(--ease-out), box-shadow var(--dur-1) var(--ease-out);
}

.choice.has-color {
  color: var(--c);
}

.choice:hover {
  background: var(--bg-hover);
}

.choice.is-selected {
  background: var(--bg-selected);
  box-shadow: inset 0 0 0 2px var(--text-primary);
}

@media (max-width: 400px) {
  .list-icon-grid {
    grid-template-columns: repeat(7, minmax(0, 1fr));
    width: 100%;
  }

  .choice {
    width: 100%;
  }
}
</style>

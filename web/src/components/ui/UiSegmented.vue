<script setup lang="ts">
// Segmented control (spec 7.10): role="radiogroup" with role="radio" buttons and a roving tabindex.
// Arrow keys move and select. A raised thumb slides under the selected segment.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type Component } from 'vue'
import UiIcon from './UiIcon.vue'

export interface SegmentedOption {
  value: string
  label: string
  icon?: Component
  count?: number
}

const model = defineModel<string>({ required: true })

const props = withDefaults(
  defineProps<{
    options: SegmentedOption[]
    /** Required in practice; optional in the type so `aria-label="…"` also type-checks (it maps to this prop). */
    ariaLabel?: string
    size?: 'sm' | 'md'
    /** Labels become aria-labels and tooltips. */
    iconOnly?: boolean
  }>(),
  { size: 'md', iconOnly: false },
)

const root = ref<HTMLElement | null>(null)
const thumb = ref<{ x: number; w: number } | null>(null)
const ready = ref(false)

const selectedIndex = computed(() => props.options.findIndex((o) => o.value === model.value))
const focusIndex = computed(() => (selectedIndex.value >= 0 ? selectedIndex.value : 0))

/** In DOM order (template refs in v-for don't promise an order after updates). */
function segments(): HTMLButtonElement[] {
  return root.value ? Array.from(root.value.querySelectorAll<HTMLButtonElement>(':scope > [role="radio"]')) : []
}

function measure(): void {
  const b = segments()[selectedIndex.value]
  thumb.value = b ? { x: b.offsetLeft, w: b.offsetWidth } : null
}

function choose(i: number, focus: boolean): void {
  const option = props.options[i]
  if (!option) return
  model.value = option.value
  if (focus) void nextTick(() => segments()[i]?.focus())
}

function onKeydown(e: KeyboardEvent, i: number): void {
  const n = props.options.length
  let next = -1
  if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = (i + 1) % n
  else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = (i - 1 + n) % n
  else if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = n - 1
  if (next < 0) return
  e.preventDefault()
  choose(next, true)
}

let observer: ResizeObserver | null = null
onMounted(() => {
  measure()
  if (typeof ResizeObserver !== 'undefined' && root.value) {
    observer = new ResizeObserver(() => measure())
    observer.observe(root.value)
    for (const b of segments()) observer.observe(b)
  }
  // Enable the slide only after the first measurement, so the thumb doesn't fly in on mount.
  requestAnimationFrame(() => {
    ready.value = true
  })
})
onBeforeUnmount(() => observer?.disconnect())
watch(
  () => [model.value, props.options.length],
  () => nextTick(measure),
)
</script>

<template>
  <div
    ref="root"
    class="ui-segmented"
    :class="[`ui-segmented--${size}`, { 'is-ready': ready, 'is-icon-only': iconOnly }]"
    role="radiogroup"
    :aria-label="ariaLabel"
  >
    <span
      v-if="thumb"
      class="thumb"
      aria-hidden="true"
      :style="{ transform: `translateX(${thumb.x}px)`, width: `${thumb.w}px` }"
    />
    <button
      v-for="(option, i) in options"
      :key="option.value"
      v-tooltip="iconOnly ? option.label : null"
      type="button"
      role="radio"
      class="segment"
      :class="{ 'is-selected': i === selectedIndex }"
      :aria-checked="i === selectedIndex ? 'true' : 'false'"
      :aria-label="iconOnly ? option.label : undefined"
      :tabindex="i === focusIndex ? 0 : -1"
      @click="choose(i, false)"
      @keydown="onKeydown($event, i)"
    >
      <UiIcon v-if="option.icon" :icon="option.icon" :size="16" />
      <span v-if="!iconOnly" class="label">{{ option.label }}</span>
      <span v-if="!iconOnly && option.count !== undefined" class="count tabular">{{ option.count }}</span>
    </button>
  </div>
</template>

<style scoped>
.ui-segmented {
  position: relative;
  display: inline-flex;
  align-items: stretch;
  gap: var(--space-0-5);
  height: var(--control-md);
  padding: var(--space-0-5);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  box-shadow: inset 0 0 0 1px var(--border-subtle);
  isolation: isolate;
}

.ui-segmented--sm {
  height: var(--control-sm);
}

.thumb {
  position: absolute;
  top: var(--space-0-5);
  bottom: var(--space-0-5);
  left: 0;
  z-index: 0;
  border-radius: var(--radius-sm);
  /* --bg-card equals --bg-surface in light; in dark it is the lighter surface, so the thumb stands
     off the --bg-subtle track there too (--bg-surface would sit darker than the track). */
  background: var(--bg-card);
  box-shadow: var(--shadow-2), 0 0 0 1px var(--border-subtle);
  pointer-events: none;
}

@media (prefers-reduced-motion: no-preference) {
  .is-ready .thumb {
    transition:
      transform var(--dur-3) var(--ease-in-out),
      width var(--dur-3) var(--ease-in-out);
  }
}

.segment {
  position: relative;
  z-index: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  /* Content width inline; equal shares of the extra space when a layout stretches the control
     (the phone view tabs are full width). */
  flex: 1 0 auto;
  gap: var(--space-1-5);
  min-width: 0;
  padding: 0 10px;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  cursor: pointer;
  transition: color var(--dur-1) var(--ease-out);
}

.ui-segmented--md .segment {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.is-icon-only .segment {
  padding: 0;
  aspect-ratio: 1;
}

.segment:hover {
  color: var(--text-primary);
}

.segment.is-selected {
  color: var(--text-primary);
}

.segment:focus-visible {
  outline-offset: 0;
}

.count {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

@media (pointer: coarse) {
  .segment::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: max(100%, var(--touch-min));
    transform: translateY(-50%);
  }
}
</style>

<script setup lang="ts">
// Progress (spec 7.10): a 4px rounded "thread" (accent on --border-subtle, --success-text when
// complete) or a 14px ring. role="progressbar" unless decorative.
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    value: number
    max: number
    label: string
    variant?: 'thread' | 'ring'
    decorative?: boolean
  }>(),
  { variant: 'thread', decorative: false },
)

const ratio = computed(() => (props.max > 0 ? Math.min(Math.max(props.value / props.max, 0), 1) : 0))
const complete = computed(() => props.max > 0 && props.value >= props.max)
const RADIUS = 5.5
const CIRCUMFERENCE = 2 * Math.PI * RADIUS
const a11y = computed(() =>
  props.decorative
    ? { 'aria-hidden': 'true' as const }
    : {
        role: 'progressbar',
        'aria-label': props.label,
        'aria-valuemin': 0,
        'aria-valuemax': props.max,
        'aria-valuenow': Math.min(Math.max(props.value, 0), props.max),
      },
)
</script>

<template>
  <span v-if="variant === 'thread'" class="ui-progress is-thread" :class="{ 'is-complete': complete }" v-bind="a11y">
    <span class="fill" :style="{ width: `${ratio * 100}%` }" />
  </span>
  <span v-else class="ui-progress is-ring" :class="{ 'is-complete': complete }" v-bind="a11y">
    <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
      <circle class="track" cx="7" cy="7" :r="RADIUS" fill="none" stroke-width="2" />
      <circle
        class="arc"
        cx="7"
        cy="7"
        :r="RADIUS"
        fill="none"
        stroke-width="2"
        stroke-linecap="round"
        :stroke-dasharray="`${CIRCUMFERENCE * ratio} ${CIRCUMFERENCE}`"
        transform="rotate(-90 7 7)"
      />
    </svg>
  </span>
</template>

<style scoped>
.ui-progress {
  display: inline-flex;
  flex-shrink: 0;
}

.is-thread {
  position: relative;
  width: 100%;
  min-width: 24px;
  height: 4px;
  border-radius: var(--radius-full);
  background: var(--border-subtle);
  overflow: hidden;
}

.fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: var(--accent);
  transition:
    width var(--dur-3) var(--ease-out),
    background-color var(--dur-3) var(--ease-out);
}

.is-thread.is-complete .fill {
  background: var(--success-text);
}

.is-ring {
  width: 14px;
  height: 14px;
}

.track {
  stroke: var(--border-subtle);
}

.arc {
  stroke: var(--accent);
  transition: stroke-dasharray var(--dur-3) var(--ease-out);
}

.is-ring.is-complete .arc {
  stroke: var(--success-text);
}
</style>

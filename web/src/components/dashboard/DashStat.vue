<script setup lang="ts">
// A stat tile: a label, a number and a short note under it. `tone` tints the number for a state
// that needs attention (overdue); the note says it in words, so colour is never the only signal.
// An optional sparkline (14 values) runs along the bottom in the tile's tone.
import { computed, type Component } from 'vue'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    label: string
    value: number
    note?: string
    icon: Component
    tone?: 'neutral' | 'accent' | 'danger' | 'success'
    spark?: number[]
    hero?: boolean
  }>(),
  { note: undefined, tone: 'neutral', spark: undefined, hero: false },
)

const formatted = computed(() => new Intl.NumberFormat(undefined, { notation: props.value >= 10_000 ? 'compact' : 'standard' }).format(props.value))

/** The sparkline as an SVG path in a 100 x 24 box. */
const path = computed(() => {
  const s = props.spark
  if (!s || s.length < 2) return null
  const max = Math.max(...s, 1)
  const step = 100 / (s.length - 1)
  return s.map((v, i) => `${i === 0 ? 'M' : 'L'}${(i * step).toFixed(2)},${(22 - (v / max) * 20).toFixed(2)}`).join(' ')
})
</script>

<template>
  <div class="dash-stat" :class="[`tone-${tone}`, { hero }]">
    <div class="top">
      <span class="label">{{ label }}</span>
      <span class="badge" aria-hidden="true"><UiIcon :icon="icon" :size="16" /></span>
    </div>
    <p class="value">{{ formatted }}</p>
    <p v-if="note" class="note">{{ note }}</p>
    <svg v-if="path" class="spark" viewBox="0 0 100 24" preserveAspectRatio="none" aria-hidden="true">
      <path :d="path" />
    </svg>
  </div>
</template>

<style scoped>
.dash-stat {
  --tone: var(--text-tertiary);
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: var(--space-4) var(--space-4) var(--space-3);
  background: var(--bg-surface);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-1);
  overflow: hidden;
}

.tone-accent { --tone: var(--accent); }
.tone-danger { --tone: var(--danger); }
.tone-success { --tone: var(--success-text); }

.top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-secondary);
}

.badge {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: var(--radius-md);
  color: var(--tone);
  background: color-mix(in srgb, var(--tone) 12%, var(--bg-surface));
}

.value {
  margin: var(--space-2) 0 0;
  font-size: var(--fs-3xl);
  line-height: var(--lh-3xl);
  font-weight: var(--fw-semibold);
  letter-spacing: -0.01em;
  color: var(--text-primary);
}

.hero .value {
  font-size: 40px;
  line-height: 46px;
}

.tone-danger .value {
  color: var(--danger-text);
}

.note {
  margin: var(--space-0-5) 0 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.spark {
  display: block;
  width: 100%;
  height: 24px;
  margin-top: var(--space-2);
  overflow: visible;
}

.spark path {
  fill: none;
  stroke: var(--tone);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
  vector-effect: non-scaling-stroke;
}
</style>

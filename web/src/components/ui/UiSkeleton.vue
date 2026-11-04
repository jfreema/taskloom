<script setup lang="ts">
// Loading placeholder (spec 7.10): a --bg-subtle to --border-subtle shimmer every 1.2s, static under
// reduced motion, always aria-hidden. The region that shows skeletons sets aria-busy="true", and
// shows them only after 200ms (useDelayedFlag).
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    variant?: 'text' | 'rect' | 'circle'
    width?: string
    height?: string
    lines?: number
  }>(),
  { variant: 'text', width: undefined, height: undefined, lines: 1 },
)

const count = computed(() => (props.variant === 'text' ? Math.max(1, props.lines) : 1))

function lineWidth(i: number): string | undefined {
  if (props.width && count.value === 1) return props.width
  if (count.value > 1 && i === count.value - 1) return '60%'
  return props.width
}
</script>

<template>
  <span v-if="variant === 'text'" class="ui-skeleton-lines" aria-hidden="true">
    <span
      v-for="i in count"
      :key="i"
      class="ui-skeleton is-text"
      :style="{ width: lineWidth(i - 1), height: height }"
    />
  </span>
  <span
    v-else
    class="ui-skeleton"
    :class="variant === 'circle' ? 'is-circle' : 'is-rect'"
    :style="{ width: width ?? (variant === 'circle' ? '24px' : '100%'), height: height ?? (variant === 'circle' ? width ?? '24px' : '16px') }"
    aria-hidden="true"
  />
</template>

<style scoped>
.ui-skeleton-lines {
  display: flex;
  flex-direction: column;
  width: 100%;
}

.ui-skeleton {
  display: block;
  flex-shrink: 0;
  background-color: var(--bg-subtle);
  background-image: linear-gradient(90deg, var(--bg-subtle) 0%, var(--border-subtle) 50%, var(--bg-subtle) 100%);
  background-size: 200% 100%;
  background-repeat: no-repeat;
}

.is-text {
  width: 100%;
  height: 10px;
  margin: 5px 0;
  border-radius: var(--radius-xs);
}

.is-rect {
  border-radius: var(--radius-sm);
}

.is-circle {
  border-radius: var(--radius-full);
}

@media (prefers-reduced-motion: no-preference) {
  .ui-skeleton {
    animation: loom-shimmer 1.2s linear infinite;
  }
}
</style>

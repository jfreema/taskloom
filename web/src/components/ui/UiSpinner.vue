<script setup lang="ts">
// LoaderCircle rotating every 800ms (spec 7.10). Static under reduced motion.
import { LoaderCircle } from '@lucide/vue'
import UiIcon from './UiIcon.vue'

withDefaults(
  defineProps<{
    size?: 14 | 16 | 20
    /** Screen-reader text when the spinner stands alone. */
    label?: string
  }>(),
  { size: 16, label: undefined },
)
</script>

<template>
  <span class="ui-spinner" :style="{ width: `${size}px`, height: `${size}px` }">
    <UiIcon :icon="LoaderCircle" :size="size" :label="label" />
  </span>
</template>

<style scoped>
.ui-spinner {
  display: inline-flex;
  flex-shrink: 0;
  color: currentColor;
}

@media (prefers-reduced-motion: no-preference) {
  .ui-spinner :deep(svg) {
    animation: loom-spin 800ms linear infinite;
  }
}
</style>

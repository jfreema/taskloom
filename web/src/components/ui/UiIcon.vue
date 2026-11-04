<script setup lang="ts">
// Every icon goes through UiIcon (spec 7.9): 16px and stroke 1.75 by default (2 at 14px and below),
// aria-hidden unless a label is given.
import { computed, type Component } from 'vue'

const props = withDefaults(
  defineProps<{
    icon: Component
    size?: 12 | 14 | 16 | 20 | 24
    strokeWidth?: number
    /** Sets role="img" and aria-label; without it the icon is aria-hidden. */
    label?: string
    /** fill="currentColor" (the filled Flag of Urgent and High). */
    filled?: boolean
  }>(),
  { size: 16, strokeWidth: undefined, label: undefined, filled: false },
)

const stroke = computed(() => props.strokeWidth ?? (props.size <= 14 ? 2 : 1.75))
</script>

<template>
  <component
    :is="icon"
    class="ui-icon"
    :size="size"
    :stroke-width="stroke"
    :fill="filled ? 'currentColor' : 'none'"
    :role="label ? 'img' : undefined"
    :aria-label="label || undefined"
    :aria-hidden="label ? undefined : 'true'"
    focusable="false"
  />
</template>

<style scoped>
.ui-icon {
  display: block;
  flex-shrink: 0;
}
</style>

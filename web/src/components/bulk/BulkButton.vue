<script setup lang="ts">
// A bulk bar button (8.7): a ghost button in the inverse tone (focus ring --focus-ring-inverse).
// It shows its label; below 720px of page width only its icon, with the label kept as its
// accessible name and shown as its tooltip. Menu and popover triggers pass their props through.
import type { Component } from 'vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'

defineProps<{
  icon: Component
  label: string
  iconOnly: boolean
  danger?: boolean
}>()
</script>

<template>
  <UiButton
    v-tooltip="iconOnly ? label : null"
    variant="ghost"
    tone="inverse"
    size="sm"
    class="bulk-button"
    :class="{ 'is-icon-only': iconOnly, 'is-danger': danger }"
    :aria-label="label"
    data-bulk-item
  >
    <UiIcon v-if="iconOnly" :icon="icon" :size="16" />
    <span v-else>{{ label }}</span>
  </UiButton>
</template>

<style scoped>
.bulk-button.is-icon-only {
  width: var(--control-sm);
  padding: 0;
  justify-content: center;
}

.bulk-button.is-danger,
.bulk-button.is-danger:hover {
  color: var(--text-inverse-danger);
}
</style>

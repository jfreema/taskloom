<script setup lang="ts">
// Small status label (spec 7.10): "Invited", "Guest", "Dev only", the filter count.
// Subtle badges use *-subtle backgrounds with *-text text.
import type { Component } from 'vue'
import UiIcon from './UiIcon.vue'

withDefaults(
  defineProps<{
    tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info'
    variant?: 'subtle' | 'solid' | 'outline'
    size?: 18 | 20
    icon?: Component
    dot?: boolean
  }>(),
  { tone: 'neutral', variant: 'subtle', size: 18, icon: undefined, dot: false },
)
</script>

<template>
  <span class="ui-badge" :class="[`tone-${tone}`, `variant-${variant}`, `size-${size}`]">
    <span v-if="dot" class="dot" aria-hidden="true" />
    <UiIcon v-else-if="icon" :icon="icon" :size="12" />
    <span class="text"><slot /></span>
  </span>
</template>

<style scoped>
.ui-badge {
  --badge-ink: var(--text-secondary);
  --badge-bg: var(--bg-subtle);
  --badge-line: var(--border-default);
  --badge-fill: var(--text-secondary);
  --badge-fill-ink: var(--bg-surface);

  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  flex-shrink: 0;
  max-width: 100%;
  height: 18px;
  padding: 0 var(--space-1-5);
  border-radius: var(--radius-full);
  font-size: var(--fs-2xs);
  line-height: var(--lh-2xs);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  vertical-align: middle;
}

.size-20 {
  height: 20px;
  padding: 0 var(--space-2);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.text {
  overflow: hidden;
  text-overflow: ellipsis;
}

.tone-neutral {
  --badge-ink: var(--text-secondary);
  --badge-bg: var(--bg-subtle);
  --badge-line: var(--border-default);
  --badge-fill: var(--text-secondary);
}

.tone-accent {
  --badge-ink: var(--accent-text);
  --badge-bg: var(--accent-subtle);
  --badge-line: var(--accent-text);
  --badge-fill: var(--accent);
  --badge-fill-ink: var(--accent-fg);
}

.tone-success {
  --badge-ink: var(--success-text);
  --badge-bg: var(--success-subtle);
  --badge-line: var(--success-text);
  --badge-fill: var(--success-text);
}

.tone-warning {
  --badge-ink: var(--warning-text);
  --badge-bg: var(--warning-subtle);
  --badge-line: var(--warning-text);
  --badge-fill: var(--warning-text);
}

.tone-danger {
  --badge-ink: var(--danger-text);
  --badge-bg: var(--danger-subtle);
  --badge-line: var(--danger-text);
  --badge-fill: var(--danger);
  --badge-fill-ink: var(--danger-fg);
}

.tone-info {
  --badge-ink: var(--info-text);
  --badge-bg: var(--info-subtle);
  --badge-line: var(--info-text);
  --badge-fill: var(--info-text);
}

.variant-subtle {
  background: var(--badge-bg);
  color: var(--badge-ink);
}

/* Neutral subtle sits on surfaces of almost the same tone: a hairline keeps its edge. */
.variant-subtle.tone-neutral {
  box-shadow: inset 0 0 0 1px var(--border-subtle);
}

.variant-solid {
  background: var(--badge-fill);
  color: var(--badge-fill-ink);
}

.variant-outline {
  background: transparent;
  color: var(--badge-ink);
  box-shadow: inset 0 0 0 1px var(--badge-line);
}

.dot {
  width: 6px;
  height: 6px;
  border-radius: var(--radius-full);
  background: currentColor;
  flex-shrink: 0;
}
</style>

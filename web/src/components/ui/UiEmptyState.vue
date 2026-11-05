<script setup lang="ts">
// Empty state (spec 7.10): a 20px icon in a 44px --accent-subtle circle, a title, an optional
// description (max 360px), actions, and an optional shortcut hint. All centred.
import type { Component } from 'vue'
import UiIcon from './UiIcon.vue'
import UiKbd from './UiKbd.vue'

withDefaults(
  defineProps<{
    icon: Component
    title: string
    description?: string
    size?: 'sm' | 'md'
    /** A shortcut hint under the actions. */
    kbd?: string
  }>(),
  { description: undefined, size: 'md', kbd: undefined },
)
</script>

<template>
  <div class="ui-empty-state" :class="`ui-empty-state--${size}`">
    <span class="badge" aria-hidden="true"><UiIcon :icon="icon" :size="20" /></span>
    <component :is="size === 'md' ? 'h2' : 'p'" class="title">{{ title }}</component>
    <p v-if="description" class="description">{{ description }}</p>
    <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    <p v-if="kbd" class="hint">{{ $slots.actions ? 'or press' : 'Press' }} <UiKbd :keys="kbd" /></p>
  </div>
</template>

<style scoped>
.ui-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: var(--space-2);
  padding: var(--space-8) var(--space-4);
  max-width: 100%;
}

.ui-empty-state--sm {
  padding: var(--space-5) var(--space-3);
}

.badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  margin-bottom: var(--space-1);
  border-radius: var(--radius-full);
  background: var(--accent-subtle);
  color: var(--accent-text);
}

/* Balanced lines: no word left alone on a second line. */
.title,
.description {
  text-wrap: balance;
}

.title {
  max-width: 360px;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.ui-empty-state--md .title {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
}

.description {
  max-width: 360px;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: var(--space-2);
  margin-top: var(--space-2);
}

.hint {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

/* Key hints mean nothing without a keyboard; UiButton hides its kbd below 640px too. */
@media (max-width: 639px) {
  .hint {
    display: none;
  }
}
</style>

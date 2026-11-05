<script setup lang="ts">
// Load failure (spec 7.10, 8.1): a CircleAlert in a --danger-subtle circle and a secondary "Try again".
import { CircleAlert, RotateCcw } from '@lucide/vue'
import UiButton from './UiButton.vue'
import UiIcon from './UiIcon.vue'

withDefaults(
  defineProps<{
    title?: string
    message?: string
    retry?: () => void
  }>(),
  { title: "Couldn't load this", message: 'Check your connection and try again.', retry: undefined },
)
</script>

<template>
  <div class="ui-error-state">
    <span class="badge" aria-hidden="true"><UiIcon :icon="CircleAlert" :size="20" /></span>
    <h2 class="title">{{ title }}</h2>
    <p v-if="message" class="message">{{ message }}</p>
    <div v-if="retry" class="actions">
      <UiButton :icon="RotateCcw" @click="retry()">Try again</UiButton>
    </div>
  </div>
</template>

<style scoped>
.ui-error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: var(--space-2);
  padding: var(--space-8) var(--space-4);
}

.badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  margin-bottom: var(--space-1);
  border-radius: var(--radius-full);
  background: var(--danger-subtle);
  color: var(--danger-text);
}

/* Balanced lines, as in UiEmptyState. */
.title,
.message {
  text-wrap: balance;
}

.title {
  max-width: 360px;
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.message {
  max-width: 360px;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.actions {
  margin-top: var(--space-2);
}
</style>

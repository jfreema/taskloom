<script setup lang="ts">
// Inline alert (spec 7.10): a *-subtle background with a *-text icon, radius 8, padding 12.
// role="status", or role="alert" for danger.
import { computed } from 'vue'
import { CircleAlert, CircleCheck, Info, TriangleAlert, X } from '@lucide/vue'
import UiIcon from './UiIcon.vue'
import UiIconButton from './UiIconButton.vue'

const props = withDefaults(
  defineProps<{
    tone: 'info' | 'warning' | 'danger' | 'success'
    title?: string
    dismissible?: boolean
  }>(),
  { title: undefined, dismissible: false },
)

const emit = defineEmits<{ dismiss: [] }>()

const icon = computed(() => ({ info: Info, warning: TriangleAlert, danger: CircleAlert, success: CircleCheck })[props.tone])
</script>

<template>
  <div class="ui-inline-alert" :class="`tone-${tone}`" :role="tone === 'danger' ? 'alert' : 'status'">
    <UiIcon class="icon" :icon="icon" :size="16" />
    <div class="content">
      <p v-if="title" class="title">{{ title }}</p>
      <div v-if="$slots.default" class="body"><slot /></div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </div>
    <UiIconButton v-if="dismissible" class="dismiss" :icon="X" label="Dismiss" size="xs" @click="emit('dismiss')" />
  </div>
</template>

<style scoped>
.ui-inline-alert {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  padding: var(--space-3);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.tone-info {
  background: var(--info-subtle);
}

.tone-info .icon {
  color: var(--info-text);
}

.tone-warning {
  background: var(--warning-subtle);
}

.tone-warning .icon {
  color: var(--warning-text);
}

.tone-danger {
  background: var(--danger-subtle);
}

.tone-danger .icon {
  color: var(--danger-text);
}

.tone-success {
  background: var(--success-subtle);
}

.tone-success .icon {
  color: var(--success-text);
}

.icon {
  margin-top: 1px;
}

.content {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  flex: 1;
  min-width: 0;
}

.title {
  font-weight: var(--fw-semibold);
}

.body {
  color: var(--text-primary);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin-top: var(--space-1);
}

.dismiss {
  margin: -4px -4px -4px 0;
}
</style>

<script setup lang="ts">
// Subtask progress (7.11): ListTree and "2/5" in 12/16 tabular --text-tertiary, --success-text when
// every subtask is done. Named "2 of 5 subtasks done"; hidden when there are no subtasks.
import { computed } from 'vue'
import { ListTree } from '@lucide/vue'
import UiIcon from '../ui/UiIcon.vue'

const props = defineProps<{ done: number; total: number }>()

const complete = computed(() => props.total > 0 && props.done >= props.total)
const label = computed(() => `${props.done} of ${props.total} ${props.total === 1 ? 'subtask' : 'subtasks'} done`)
</script>

<template>
  <span v-if="total > 0" class="subtask-progress tabular" :class="{ 'is-complete': complete }" role="img" :aria-label="label">
    <UiIcon :icon="ListTree" :size="14" />
    <span aria-hidden="true">{{ done }}/{{ total }}</span>
  </span>
</template>

<style scoped>
.subtask-progress {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-1);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  white-space: nowrap;
}

.subtask-progress.is-complete {
  color: var(--success-text);
}
</style>

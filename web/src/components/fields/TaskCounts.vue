<script setup lang="ts">
// Comment and attachment counts (7.11): MessageSquare n and Paperclip n, each only when above 0, in
// 12px --text-tertiary. Named "3 comments, 1 attachment"; renders nothing when both are 0.
import { computed } from 'vue'
import { MessageSquare, Paperclip } from '@lucide/vue'
import { pluralize } from '../../lib/format'
import type { Task } from '../../types'
import UiIcon from '../ui/UiIcon.vue'

const props = defineProps<{ task: Pick<Task, 'commentCount' | 'attachmentCount'> }>()

const label = computed(() => {
  const parts: string[] = []
  if (props.task.commentCount > 0) parts.push(pluralize(props.task.commentCount, 'comment'))
  if (props.task.attachmentCount > 0) parts.push(pluralize(props.task.attachmentCount, 'attachment'))
  return parts.join(', ')
})
</script>

<template>
  <span v-if="label" class="task-counts tabular" role="img" :aria-label="label">
    <span v-if="task.commentCount > 0" class="count">
      <UiIcon :icon="MessageSquare" :size="14" />
      <span aria-hidden="true">{{ task.commentCount }}</span>
    </span>
    <span v-if="task.attachmentCount > 0" class="count">
      <UiIcon :icon="Paperclip" :size="14" />
      <span aria-hidden="true">{{ task.attachmentCount }}</span>
    </span>
  </span>
</template>

<style scoped>
.task-counts {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-2);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  white-space: nowrap;
}

.count {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
}
</style>

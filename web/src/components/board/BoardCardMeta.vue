<script setup lang="ts">
// A Board card's meta row (8.8): the due date, priority, subtask progress, comment and attachment
// counts, then a spacer and the assignees (20px avatars, 3 at most). It renders nothing when the
// task has none of these, so the card leaves no empty row.
import { computed } from 'vue'
import { useMembers } from '../../stores/members'
import type { Task } from '../../types'
import DueDate from '../fields/DueDate.vue'
import PriorityFlag from '../fields/PriorityFlag.vue'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import TaskCounts from '../fields/TaskCounts.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'

const props = defineProps<{
  task: Task
  /** The task is in a Done status: its due date is never shown as overdue. */
  done: boolean
}>()

const members = useMembers()

const people = computed(() => props.task.assigneeIds.map((uid) => ({ id: uid, displayName: members.nameOf(uid) })))

const shown = computed(() => {
  const t = props.task
  return (
    t.dueDate !== null ||
    t.priority !== 'None' ||
    t.subtaskCount > 0 ||
    t.commentCount > 0 ||
    t.attachmentCount > 0 ||
    t.assigneeIds.length > 0
  )
})
</script>

<template>
  <div v-if="shown" class="meta">
    <DueDate v-if="task.dueDate" :date="task.dueDate" :done="done" size="sm" />
    <PriorityFlag v-if="task.priority !== 'None'" :priority="task.priority" :size="14" />
    <SubtaskProgress :done="task.subtaskDoneCount" :total="task.subtaskCount" />
    <TaskCounts :task="task" />
    <span class="spacer" />
    <UiAvatarStack v-if="people.length" :users="people" :size="20" :max="3" />
  </div>
</template>

<style scoped>
.meta {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  min-height: 20px;
  color: var(--text-secondary);
}

.spacer {
  flex: 1 1 auto;
}
</style>

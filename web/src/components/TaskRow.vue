<script setup lang="ts">
import { computed, ref } from 'vue'
import { useList } from '../stores/list'
import StatusPicker from './StatusPicker.vue'
import PriorityPicker from './PriorityPicker.vue'
import AssigneePicker from './AssigneePicker.vue'
import DueDate from './DueDate.vue'
import InlineInput from './InlineInput.vue'
import type { Task } from '../types'

const props = defineProps<{ task: Task; depth?: number }>()
const emit = defineEmits<{ open: [taskId: string] }>()
const list = useList()

const expanded = ref(false)
const addingSubtask = ref(false)
const subtasks = computed(() => list.subtasksOf(props.task.id))
const done = computed(() => list.statusById(props.task.statusId)?.category === 'Done')
const doneSubtasks = computed(() => subtasks.value.filter((s) => list.statusById(s.statusId)?.category === 'Done').length)

const update = (patch: Partial<Task>) => list.updateTask(props.task.id, patch)

async function addSubtask(title: string) {
  await list.createTask(title, undefined, props.task.id)
  expanded.value = true
}
</script>

<template>
  <div class="task-row" :class="{ done }" :style="{ paddingLeft: `${12 + (depth ?? 0) * 28}px` }" @click="emit('open', task.id)">
    <div class="cell title-cell">
      <button v-if="subtasks.length" class="caret" @click.stop="expanded = !expanded">{{ expanded ? '▾' : '▸' }}</button>
      <span v-else class="caret-space" />
      <StatusPicker :status-id="task.statusId" compact @change="(statusId) => update({ statusId })" />
      <span class="title">{{ task.title }}</span>
      <span v-if="subtasks.length" class="sub-count faint" :title="`${doneSubtasks} of ${subtasks.length} subtasks done`">⑂ {{ doneSubtasks }}/{{ subtasks.length }}</span>
      <button v-if="!depth" class="add-sub hover-only faint" title="Add subtask" @click.stop="addingSubtask = true">＋</button>
    </div>
    <div class="cell" @click.stop><AssigneePicker :assignee-id="task.assigneeId" @change="(assigneeId) => update({ assigneeId })" /></div>
    <div class="cell" @click.stop><DueDate :date="task.dueDate" :done="done" @change="(dueDate) => update({ dueDate })" /></div>
    <div class="cell" @click.stop><PriorityPicker :priority="task.priority" @change="(priority) => update({ priority })" /></div>
  </div>
  <template v-if="expanded">
    <TaskRow v-for="s in subtasks" :key="s.id" :task="s" :depth="(depth ?? 0) + 1" @open="(id) => emit('open', id)" />
  </template>
  <div v-if="addingSubtask" class="task-row adding" :style="{ paddingLeft: `${40 + 28}px` }">
    <InlineInput multi placeholder="Subtask name, then Enter" @submit="addSubtask" @cancel="addingSubtask = false" />
  </div>
</template>

<style scoped>
.task-row { display: grid; grid-template-columns: minmax(200px, 1fr) 110px 120px 90px; align-items: center; border-bottom: 1px solid var(--border); min-height: 38px; cursor: pointer; padding-right: 12px; }
.task-row:hover { background: var(--bg-soft); }
.task-row.adding { display: block; padding-top: 4px; padding-bottom: 4px; }
.task-row.adding input { width: 100%; max-width: 480px; }
.cell { display: flex; align-items: center; gap: 8px; min-width: 0; }
.title-cell { padding-right: 8px; }
.title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.done .title { color: var(--text-muted); text-decoration: line-through; }
.caret { width: 18px; padding: 0; font-size: 11px; color: var(--text-faint); flex: none; }
.caret-space { width: 18px; flex: none; }
.sub-count { font-size: 12px; white-space: nowrap; }
.hover-only { visibility: hidden; }
.task-row:hover .hover-only { visibility: visible; }
@media (max-width: 720px) { .task-row { grid-template-columns: 1fr 40px 80px 36px; } }
</style>

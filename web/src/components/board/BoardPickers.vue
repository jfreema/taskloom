<script setup lang="ts">
// The Board's pickers without a visible trigger (8.8, 7.11): one at a time, opened by S, A, D, P or
// the card menu, anchored to the card, and returning focus to it when they close. Picker edits show
// no toast (9.2).
import { computed } from 'vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { IsoDate, Priority, TaskPatch, Uuid } from '../../types'
import AssigneePicker from '../fields/AssigneePicker.vue'
import DateField from '../fields/DateField.vue'
import PriorityPicker from '../fields/PriorityPicker.vue'
import StatusPicker from '../fields/StatusPicker.vue'
import type { PickTarget } from './boardContext'

const props = defineProps<{ target: PickTarget | null }>()
const open = defineModel<boolean>('open', { default: false })

const tasks = useTasks()
const actions = useTaskActions()

const task = computed(() => (props.target ? (tasks.get(props.target.id) ?? null) : null))

function patch(id: Uuid, p: TaskPatch): void {
  void tasks.update(id, p, { undo: false }).catch(noop)
}

function setStatus(id: Uuid, statusId: Uuid): void {
  void actions.setStatus(id, statusId)
}

function setPriority(id: Uuid, priority: Priority): void {
  patch(id, { priority })
}

// DateField emits the start date it moves (when the new due date falls before it) just before the due date.
let movedStart: IsoDate | null | undefined
function setDue(id: Uuid, dueDate: IsoDate | null): void {
  const p: TaskPatch = { dueDate }
  if (movedStart !== undefined) p.startDate = movedStart
  movedStart = undefined
  patch(id, p)
}
</script>

<template>
  <template v-if="target && task">
    <StatusPicker
      v-if="target.field === 'status'"
      v-model:open="open"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.statusId"
      :list-id="task.listId"
      @update:model-value="setStatus(task.id, $event)"
    />
    <AssigneePicker
      v-else-if="target.field === 'assignees'"
      v-model:open="open"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.assigneeIds"
      :task-ids="[task.id]"
      @update:model-value="patch(task.id, { assigneeIds: $event })"
    />
    <DateField
      v-else-if="target.field === 'dueDate'"
      v-model:open="open"
      hide-trigger
      :anchor="target.anchor"
      kind="due"
      :model-value="task.dueDate"
      :other-date="task.startDate"
      @update:other-date="movedStart = $event"
      @update:model-value="setDue(task.id, $event)"
    />
    <PriorityPicker
      v-else
      v-model:open="open"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.priority"
      @update:model-value="setPriority(task.id, $event)"
    />
  </template>
</template>

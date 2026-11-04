<script setup lang="ts">
// The picker that S, A, D or P opens for a column that isn't shown (hidden in the Columns menu,
// or the Tags and Status columns at narrow widths, 8.7): anchored to the row, with no trigger of
// its own. ListTable opens it with `open(id, field, row)`.
import { computed, ref, shallowRef, watch } from 'vue'
import { useTaskActions, type PickField } from '../../composables/useTaskActions'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { IsoDate, TaskPatch, Uuid } from '../../types'
import AssigneePicker from '../fields/AssigneePicker.vue'
import DateField from '../fields/DateField.vue'
import PriorityPicker from '../fields/PriorityPicker.vue'
import StatusPicker from '../fields/StatusPicker.vue'

const tasks = useTasks()
const actions = useTaskActions()

const target = shallowRef<{ id: Uuid; field: PickField; anchor: HTMLElement } | null>(null)
const isOpen = ref(false)
const task = computed(() => (target.value ? (tasks.get(target.value.id) ?? null) : null))

watch(isOpen, (on) => {
  if (!on) target.value = null
})

function open(id: Uuid, field: PickField, anchor: HTMLElement): void {
  target.value = { id, field, anchor }
  isOpen.value = true
}

function patch(id: Uuid, p: TaskPatch): void {
  void tasks.update(id, p, { undo: false }).catch(noop)
}

/** DateField sends the start date first when the new due date is before it: one PATCH for both. */
let nextStart: IsoDate | null | undefined
function onStartAdjust(v: IsoDate | null): void {
  nextStart = v
}
function onDue(id: Uuid, v: IsoDate | null): void {
  const p: TaskPatch = { dueDate: v }
  if (nextStart !== undefined) p.startDate = nextStart
  nextStart = undefined
  patch(id, p)
}

defineExpose({ open })
</script>

<template>
  <template v-if="target && task">
    <StatusPicker
      v-if="target.field === 'status'"
      v-model:open="isOpen"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.statusId"
      :list-id="task.listId"
      @update:model-value="actions.setStatus(task.id, $event)"
    />
    <AssigneePicker
      v-else-if="target.field === 'assignees'"
      v-model:open="isOpen"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.assigneeIds"
      :task-ids="[task.id]"
      @update:model-value="patch(task.id, { assigneeIds: $event })"
    />
    <DateField
      v-else-if="target.field === 'dueDate'"
      v-model:open="isOpen"
      hide-trigger
      :anchor="target.anchor"
      kind="due"
      :model-value="task.dueDate"
      :other-date="task.startDate"
      @update:other-date="onStartAdjust"
      @update:model-value="onDue(task.id, $event)"
    />
    <PriorityPicker
      v-else
      v-model:open="isOpen"
      hide-trigger
      :anchor="target.anchor"
      :model-value="task.priority"
      @update:model-value="patch(task.id, { priority: $event })"
    />
  </template>
</template>

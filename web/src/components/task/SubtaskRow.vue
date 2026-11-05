<script setup lang="ts">
// One subtask in the task detail (8.10), 36px: the complete circle, the title (opens it), the
// assignees and due date (revealed on hover while empty) and the "..." menu (Open, Delete).
import { computed } from 'vue'
import { Ellipsis } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { IsoDate, Task, TaskPatch, Uuid } from '../../types'
import AssigneePicker from '../fields/AssigneePicker.vue'
import CompleteToggle from '../fields/CompleteToggle.vue'
import DateField from '../fields/DateField.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'
import { useTaskDetailContext } from './taskDetailContext'

const props = defineProps<{ task: Task }>()

const ctx = useTaskDetailContext()
const tasks = useTasks()
const workspaces = useWorkspaces()
const actions = useTaskActions()

const done = computed(() => tasks.isDone(props.task))
const color = computed(() => workspaces.statusById(props.task.statusId)?.color)
const items = () => actions.rowMenuItems(props.task, { variant: 'subtask' })

function patch(p: TaskPatch): void {
  void tasks.update(props.task.id, p, { undo: false }).catch(noop)
}

// A due date before the start date moves the start date too (DateField emits it first): one PATCH.
let startDate: { value: IsoDate | null } | null = null

function onStart(v: IsoDate | null): void {
  startDate = { value: v }
}

function onDue(v: IsoDate | null): void {
  const p: TaskPatch = { dueDate: v }
  if (startDate) p.startDate = startDate.value
  startDate = null
  patch(p)
}
</script>

<template>
  <li class="subtask-row" data-row :class="{ 'is-done': done }">
    <CompleteToggle
      :done="done"
      :label="task.title"
      :color="color"
      :readonly="!ctx.canEdit.value"
      @toggle="void tasks.toggleComplete(task.id)"
    />
    <button v-tooltip="{ text: task.title, overflowOnly: true }" type="button" class="title truncate" @click="ctx.openTask(task.id)">
      {{ task.title }}
    </button>
    <span class="cell cell-assignees">
      <AssigneePicker
        :class="{ reveal: !task.assigneeIds.length }"
        :model-value="task.assigneeIds"
        :task-ids="[task.id]"
        variant="stack"
        :size="20"
        :readonly="!ctx.canEdit.value"
        @update:model-value="(ids: Uuid[]) => patch({ assigneeIds: ids })"
      />
    </span>
    <span class="cell cell-due">
      <DateField
        :class="{ reveal: !task.dueDate }"
        :model-value="task.dueDate"
        kind="due"
        :other-date="task.startDate"
        variant="chip"
        :done="done"
        :readonly="!ctx.canEdit.value"
        @update:other-date="onStart"
        @update:model-value="onDue"
      />
    </span>
    <UiMenu :items="items" placement="bottom-end" :aria-label="`Actions for ${task.title}`">
      <template #trigger="{ props: t }">
        <UiIconButton v-bind="t" class="reveal reveal-touch" :icon="Ellipsis" :label="`More actions for ${task.title}`" size="sm" />
      </template>
    </UiMenu>
  </li>
</template>

<style scoped>
/* A subgrid of SubtasksSection's .rows: the columns (and their gap) are shared by every row. */
.subtask-row {
  display: grid;
  grid-column: 1 / -1;
  grid-template-columns: subgrid;
  align-items: center;
  min-height: var(--row-list);
  padding: 0 var(--space-1) 0 var(--space-2);
  margin: 0 calc(var(--space-2) * -1);
  border-radius: var(--radius-sm);
}

.subtask-row:hover {
  background: var(--bg-hover);
}

.title {
  min-width: 0;
  padding: var(--space-1) 0;
  border: 0;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-align: left;
  cursor: pointer;
}

.title:hover {
  text-decoration: underline;
  text-decoration-color: var(--border-control);
  text-underline-offset: 2px;
}

.is-done .title {
  color: var(--text-tertiary);
}

/* Shared columns (see SubtasksSection), right-aligned, so avatars and dates line up from row to row
   whether or not a row has them (an empty one is only revealed on hover). */
.cell {
  display: flex;
  justify-content: flex-end;
  min-width: 0;
}

.cell-assignees {
  min-width: 48px;
}

/* Touch (7.5): the title button takes the row's full 44px height; the wider column gap (the
   section's) keeps the 44px targets of the toggle, the avatars and the date chip apart. */
@media (pointer: coarse) {
  .title {
    align-self: stretch;
  }
}

/* Touch screens never reveal the empty controls, so they give their room to the title. The field
   triggers are rendered by the field components, hence :deep. */
@media (hover: none) {
  .cell-assignees {
    min-width: 0;
  }

  .cell > :deep(.reveal:not(.reveal-touch)) {
    display: none;
  }
}
</style>

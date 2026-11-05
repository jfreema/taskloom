<script setup lang="ts">
// The top of the task detail (8.10): the status pill with Mark complete / Reopen, the title, the
// property rows (labels in a 112px column, values as ghost triggers) and "Created by … · date".
// S, A, D and P open the pickers through openPicker().
import { computed, reactive } from 'vue'
import { Check, Undo2 } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { useToday } from '../../composables/useToday'
import { noop } from '../../lib/async'
import { formatDateShort, formatTimestamp, toIso } from '../../lib/dates'
import { useMembers } from '../../stores/members'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { IsoDate, Priority, Task, TaskPatch, User, Uuid } from '../../types'
import AssigneePicker from '../fields/AssigneePicker.vue'
import DateField from '../fields/DateField.vue'
import PriorityPicker from '../fields/PriorityPicker.vue'
import StatusPicker from '../fields/StatusPicker.vue'
import TagPicker from '../fields/TagPicker.vue'
import UiButton from '../ui/UiButton.vue'
import TaskTitle from './TaskTitle.vue'

export type PickerField = 'status' | 'assignees' | 'dueDate' | 'priority'

const props = defineProps<{ task: Task; createdBy: User | null; canEdit: boolean; size: 'panel' | 'page' }>()

const tasks = useTasks()
const workspaces = useWorkspaces()
const members = useMembers()
const actions = useTaskActions()
const today = useToday()

const open = reactive<Record<PickerField, boolean>>({ status: false, assignees: false, dueDate: false, priority: false })

const done = computed(() => tasks.isDone(props.task))
const canComplete = computed(() => !!workspaces.doneStatusFor(props.task.listId))
const canReopen = computed(() => !!workspaces.openStatusFor(props.task.listId))

const creator = computed(() => props.createdBy?.displayName ?? members.nameOf(props.task.createdById))
const createdDay = computed(() => {
  const d = new Date(props.task.createdAt)
  return Number.isNaN(d.getTime()) ? '' : formatDateShort(toIso(d), today.value)
})
const createdFull = computed(() => formatTimestamp(props.task.createdAt))

function patch(p: TaskPatch): void {
  void tasks.update(props.task.id, p, { undo: false }).catch(noop)
}

function setStatus(statusId: Uuid): void {
  void actions.setStatus(props.task.id, statusId)
}

function toggleComplete(): void {
  void tasks.toggleComplete(props.task.id)
}

// DateField emits the other date first when the pick crosses it (a start after the due date moves
// the due date); both go out in one PATCH.
let otherDate: { field: 'startDate' | 'dueDate'; value: IsoDate | null } | null = null

function onOtherDate(field: 'startDate' | 'dueDate', value: IsoDate | null): void {
  otherDate = { field, value }
}

function onDate(field: 'startDate' | 'dueDate', value: IsoDate | null): void {
  const p: TaskPatch = { [field]: value }
  if (otherDate && otherDate.field !== field) p[otherDate.field] = otherDate.value
  otherDate = null
  patch(p)
}

function openPicker(field: PickerField): boolean {
  if (!props.canEdit) return false
  for (const k of Object.keys(open) as PickerField[]) open[k] = k === field
  return true
}

defineExpose({ openPicker })
</script>

<template>
  <div class="task-properties" :class="`size-${size}`">
    <div class="status-row">
      <StatusPicker
        v-model:open="open.status"
        :model-value="task.statusId"
        :list-id="task.listId"
        variant="pill"
        :size="28"
        :readonly="!canEdit"
        @update:model-value="setStatus"
      />
      <template v-if="done">
        <span class="completed">Completed</span>
        <UiButton v-if="canEdit && canReopen" variant="secondary" size="sm" :icon="Undo2" @click="toggleComplete">Reopen</UiButton>
      </template>
      <UiButton v-else-if="canEdit && canComplete" variant="secondary" size="sm" :icon="Check" @click="toggleComplete">
        Mark complete
      </UiButton>
    </div>

    <TaskTitle :task="task" :can-edit="canEdit" :size="size" />

    <dl class="props">
      <div class="prop">
        <dt class="label">Assignees</dt>
        <dd class="value">
          <AssigneePicker
            v-model:open="open.assignees"
            :model-value="task.assigneeIds"
            :task-ids="[task.id]"
            variant="field"
            :readonly="!canEdit"
            @update:model-value="(ids: Uuid[]) => patch({ assigneeIds: ids })"
          />
        </dd>
      </div>
      <div class="prop">
        <dt class="label">Dates</dt>
        <dd class="value dates">
          <DateField
            v-if="canEdit || task.startDate"
            :model-value="task.startDate"
            kind="start"
            :other-date="task.dueDate"
            variant="chip"
            :readonly="!canEdit"
            @update:other-date="(v: IsoDate | null) => onOtherDate('dueDate', v)"
            @update:model-value="(v: IsoDate | null) => onDate('startDate', v)"
          />
          <span v-if="canEdit || (task.startDate && task.dueDate)" class="arrow" aria-hidden="true">→</span>
          <DateField
            v-if="canEdit || task.dueDate"
            v-model:open="open.dueDate"
            :model-value="task.dueDate"
            kind="due"
            :other-date="task.startDate"
            variant="chip"
            :done="done"
            :readonly="!canEdit"
            @update:other-date="(v: IsoDate | null) => onOtherDate('startDate', v)"
            @update:model-value="(v: IsoDate | null) => onDate('dueDate', v)"
          />
          <span v-if="!canEdit && !task.startDate && !task.dueDate" class="none">None</span>
        </dd>
      </div>
      <div class="prop">
        <dt class="label">Priority</dt>
        <dd class="value">
          <PriorityPicker
            v-model:open="open.priority"
            :model-value="task.priority"
            variant="label"
            :readonly="!canEdit"
            @update:model-value="(p: Priority) => patch({ priority: p })"
          />
        </dd>
      </div>
      <div class="prop">
        <dt class="label">Tags</dt>
        <dd class="value">
          <TagPicker
            :model-value="task.tagIds"
            variant="field"
            :readonly="!canEdit"
            @update:model-value="(ids: Uuid[]) => patch({ tagIds: ids })"
          />
        </dd>
      </div>
    </dl>

    <p class="created">
      Created by {{ creator }} · <time v-tooltip="createdFull" :datetime="task.createdAt">{{ createdDay }}</time>
    </p>
  </div>
</template>

<style scoped>
.task-properties {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.status-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.completed {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--success-text);
}

.props {
  display: flex;
  flex-direction: column;
  margin: var(--space-1) 0 0;
}

.prop {
  display: grid;
  grid-template-columns: 112px minmax(0, 1fr);
  align-items: center;
  min-height: 32px;
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.value {
  display: flex;
  align-items: center;
  min-width: 0;
  margin: 0 0 0 calc(var(--space-2) * -1);
}

.dates {
  gap: var(--space-1-5);
  flex-wrap: wrap;
  margin-left: 0;
}

.arrow {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
}

.none {
  color: var(--text-tertiary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.created {
  margin: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

@media (max-width: 639px) {
  .prop {
    grid-template-columns: 96px minmax(0, 1fr);
  }
}

/* Touch (7.5): 44px property rows whose triggers fill them. */
@media (max-width: 639px), (pointer: coarse) {
  .prop {
    min-height: var(--touch-min);
  }
}

@media (pointer: coarse) {
  .value :deep(.field-trigger--ghost) {
    min-height: var(--touch-min);
  }
}
</style>

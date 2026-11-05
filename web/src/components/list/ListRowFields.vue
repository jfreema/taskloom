<script setup lang="ts">
// The optional columns of a List task row (8.7): Status (only while not grouped by status),
// Assignees, Due date, Priority and Tags, in that order. It renders the cells themselves (several
// root nodes), so they stay direct children of the row for its flex and phone-grid layouts. Empty
// values show their editor only on hover or focus (`reveal`); read-only rows (guests, a task being
// deleted) leave an empty value's cell blank, since there is nothing to add.
import { computed } from 'vue'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { IsoDate, Task, TaskPatch } from '../../types'
import AssigneePicker from '../fields/AssigneePicker.vue'
import DateField from '../fields/DateField.vue'
import PriorityPicker from '../fields/PriorityPicker.vue'
import StatusPicker from '../fields/StatusPicker.vue'
import TagPicker from '../fields/TagPicker.vue'
import { useListTable } from './listTable'

const props = defineProps<{
  task: Task
  readonly: boolean
  done: boolean
  /** The row is the tab stop: only then are its controls in the tab order. */
  tabStop: boolean
  /** The row isn't armed yet: the pickers render only their triggers (LazyPopover, LazyMenu). */
  lazy: boolean
}>()

const table = useListTable()
const tasks = useTasks()

const cols = computed(() => table.columns.value)
const inner = computed(() => (props.tabStop ? undefined : -1))

function patch(p: TaskPatch): void {
  void tasks.update(props.task.id, p, { undo: false }).catch(noop)
}

/** DateField sends the start date first when the new due date is before it: one PATCH for both. */
let nextStart: IsoDate | null | undefined
function onStartAdjust(v: IsoDate | null): void {
  nextStart = v
}
function onDue(v: IsoDate | null): void {
  const p: TaskPatch = { dueDate: v }
  if (nextStart !== undefined) p.startDate = nextStart
  nextStart = undefined
  patch(p)
}
</script>

<template>
  <div v-if="cols.status" role="gridcell" class="cell cell--status" data-field="status-pill">
    <StatusPicker
      :model-value="task.statusId"
      :list-id="task.listId"
      variant="pill"
      :size="20"
      :readonly="readonly"
      :lazy="lazy"
      :tabindex="inner"
      @update:model-value="table.setStatus(task.id, $event)"
    />
  </div>
  <div
    v-if="cols.assignees"
    role="gridcell"
    class="cell cell--assignees"
    :class="{ 'is-empty': !task.assigneeIds.length }"
    data-field="assignees"
  >
    <AssigneePicker
      v-if="!readonly || task.assigneeIds.length"
      :class="{ reveal: !task.assigneeIds.length }"
      :model-value="task.assigneeIds"
      :task-ids="[task.id]"
      variant="stack"
      :max="table.phoneRows.value ? 2 : 3"
      :size="table.phoneRows.value ? 20 : 24"
      :readonly="readonly"
      :lazy="lazy"
      :tabindex="inner"
      @update:model-value="patch({ assigneeIds: $event })"
    />
  </div>
  <div v-if="cols.dueDate" role="gridcell" class="cell cell--due" :class="{ 'is-empty': !task.dueDate }" data-field="dueDate">
    <DateField
      v-if="!readonly || task.dueDate"
      :class="{ reveal: !task.dueDate }"
      :model-value="task.dueDate"
      :other-date="task.startDate"
      kind="due"
      variant="cell"
      :done="done"
      :readonly="readonly"
      :lazy="lazy"
      :tabindex="inner"
      @update:other-date="onStartAdjust"
      @update:model-value="onDue"
    />
  </div>
  <div
    v-if="cols.priority"
    role="gridcell"
    class="cell cell--priority"
    :class="{ 'is-empty': task.priority === 'None' }"
    data-field="priority"
  >
    <PriorityPicker
      v-if="!readonly || task.priority !== 'None'"
      :class="{ reveal: task.priority === 'None' }"
      :model-value="task.priority"
      variant="label"
      :readonly="readonly"
      :lazy="lazy"
      :tabindex="inner"
      @update:model-value="patch({ priority: $event })"
    />
  </div>
  <div v-if="cols.tags" role="gridcell" class="cell cell--tags" :class="{ 'is-empty': !task.tagIds.length }" data-field="tags">
    <TagPicker
      v-if="!readonly || task.tagIds.length"
      :class="{ reveal: !task.tagIds.length }"
      :model-value="task.tagIds"
      variant="chips"
      :max="2"
      :readonly="readonly"
      :lazy="lazy"
      :tabindex="inner"
      @update:model-value="patch({ tagIds: $event })"
    />
  </div>
</template>

<style scoped>
.cell {
  display: flex;
  flex: none;
  align-items: center;
  min-width: 0;
}

/* The pill and the avatars line up with their column header's text, as the other cells' triggers do. */
.cell--status {
  width: var(--w-status);
  padding-left: var(--space-1-5);
}

.cell--assignees {
  width: var(--w-assignees);
  padding-left: var(--space-1-5);
}

.cell--due {
  width: var(--w-due);
}

.cell--priority {
  width: var(--w-priority);
}

.cell--tags {
  width: var(--w-tags);
  overflow: hidden;
}

@container page (width < 900px) {
  .cell--tags {
    display: none;
  }
}

@container page (width < 720px) {
  .cell--priority :deep(.label),
  .cell--priority :deep(.empty-text) {
    display: none;
  }
}

/* Phones: the second line of the row holds the due date, the flag and the avatars. */
@container page (width < 560px) {
  .cell--status,
  .cell--tags,
  .cell.is-empty {
    display: none;
  }

  .cell--due {
    grid-column: 2;
    grid-row: 2;
    width: auto;
    margin-right: var(--space-2);
  }

  .cell--due :deep(.field-trigger) {
    height: 20px;
    padding: 0;
    font-size: var(--fs-xs);
    line-height: var(--lh-xs);
  }

  .cell--priority {
    grid-column: 3;
    grid-row: 2;
    width: auto;
    margin-right: var(--space-2);
  }

  .cell--priority :deep(.field-trigger) {
    height: 20px;
    padding: 0;
  }

  .cell--assignees {
    grid-column: 4;
    grid-row: 2;
    width: auto;
    padding-left: 0;
  }
}

/* Touch phones: the second line's 20px fields can't each have a 44px target, so they aren't
   targets there: a tap on them opens the task, whose sheet has 44px property rows (7.5). They
   stay in the tab order for a keyboard. */
@media (pointer: coarse) {
  @container page (width < 560px) {
    .cell--due :deep(.field-trigger),
    .cell--priority :deep(.field-trigger),
    .cell--assignees :deep(.field-trigger) {
      pointer-events: none;
    }
  }
}
</style>

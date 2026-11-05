<script setup lang="ts">
// The Name cell of a List task row (8.7): the subtask chevron (or its space), the status icon
// picker, the title (or the rename field, F2 or the pencil), then the subtask progress, the
// comment and attachment counts, the revealed Rename pencil and "Hidden by filters".
// Subtasks are indented 24px and joined to their parent by a 1px thread line.
import { computed } from 'vue'
import { ChevronRight, Pencil } from '@lucide/vue'
import type { Task } from '../../types'
import StatusPicker from '../fields/StatusPicker.vue'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import TaskCounts from '../fields/TaskCounts.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import { useListTable } from './listTable'

const props = defineProps<{
  task: Task
  /** A top-level task (aria-level 1), not a subtask. */
  top: boolean
  hasChildren: boolean
  expanded: boolean
  renaming: boolean
  kept: boolean
  readonly: boolean
  tabStop: boolean
  /** The row isn't armed yet: its status picker renders only its trigger (LazyPopover). */
  lazy: boolean
}>()

const table = useListTable()

/** Controls are tabbable only in the tab stop's row. */
const inner = computed(() => (props.tabStop ? undefined : -1))

function onRenameEditing(on: boolean): void {
  if (!on) table.endRename()
}
</script>

<template>
  <div role="gridcell" class="cell--name">
    <button
      v-if="hasChildren"
      type="button"
      class="chevron"
      :class="{ 'is-open': expanded }"
      :aria-label="expanded ? 'Hide subtasks' : 'Show subtasks'"
      :tabindex="inner"
      @click="table.model.toggleExpanded(task.id)"
    >
      <UiIcon :icon="ChevronRight" :size="14" />
    </button>
    <span v-else class="chevron-space" aria-hidden="true" />
    <span class="status" data-field="status">
      <StatusPicker
        :model-value="task.statusId"
        :list-id="task.listId"
        variant="icon"
        :readonly="readonly"
        :lazy="lazy"
        :tabindex="inner"
        @update:model-value="table.setStatus(task.id, $event)"
      />
    </span>
    <span v-if="renaming" class="rename-field">
      <UiInlineEdit
        :editing="true"
        :model-value="task.title"
        aria-label="Rename task"
        activate="none"
        @update:editing="onRenameEditing"
        @save="table.rename(task.id, $event)"
      />
    </span>
    <span v-else v-tooltip="{ text: task.title, overflowOnly: true }" class="title truncate">{{ task.title }}</span>
    <span v-if="!renaming" class="meta">
      <SubtaskProgress v-if="top && task.subtaskCount > 0" :done="task.subtaskDoneCount" :total="task.subtaskCount" />
      <TaskCounts v-if="task.commentCount > 0 || task.attachmentCount > 0" :task="task" />
      <!-- Hidden until hover or focus, which arm the row: until then a same-size space holds its place. -->
      <span v-if="!readonly && lazy" class="rename-space" aria-hidden="true" />
      <UiIconButton
        v-else-if="!readonly"
        class="rename reveal"
        :icon="Pencil"
        :label="`Rename ${task.title}`"
        size="xs"
        :tabindex="inner"
        @click="table.startRename(task.id)"
      />
      <span v-if="kept" class="kept">Hidden by filters</span>
    </span>
  </div>
</template>

<style scoped>
/* Not a ".cell": the row's own cell rule (flex: none) would win over this one. The inline-size
   containment keeps a long title out of the table's min-content width: the column's minimum is
   --name-min, and the title truncates. */
.cell--name {
  display: flex;
  flex: 1 1 0;
  align-items: center;
  gap: var(--space-1-5);
  min-width: var(--name-min);
  padding-right: var(--space-3);
  contain: inline-size;
}

.chevron,
.chevron-space {
  flex: none;
  width: 20px;
  height: 20px;
}

.chevron {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-tertiary);
  cursor: pointer;
}

.chevron:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.chevron :deep(svg) {
  transition: transform var(--dur-2) var(--ease-out);
}

.chevron.is-open :deep(svg) {
  transform: rotate(90deg);
}

.status {
  display: inline-flex;
  flex: none;
}

.title {
  flex: 0 1 auto;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.is-done .title,
.is-context .title {
  color: var(--text-tertiary);
}

.rename-field {
  flex: 1 1 auto;
  min-width: 0;
}

.rename-space {
  flex: none;
  width: var(--control-xs);
  height: var(--control-xs);
}

.meta {
  display: inline-flex;
  flex: none;
  align-items: center;
  gap: var(--space-2);
}

.kept {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  white-space: nowrap;
}

/* Subtasks: indented 24px, joined to their parent by a 1px thread line under its status icon. */
.is-sub .cell--name {
  position: relative;
  padding-left: var(--space-6);
}

.is-sub .cell--name::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: calc(20px + var(--space-1-5) + 11.5px + var(--touch-shift, 0px));
  width: 1px;
  background: var(--border-default);
}

.is-sub.is-last .cell--name::before {
  bottom: 50%;
}

/* Touch (7.5): the chevron and the status icon are 44px targets side by side (the icon moves right
   by --touch-shift, set by ListTable). */
@media (pointer: coarse) {
  .chevron {
    position: relative;
  }

  .chevron::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: var(--touch-min);
    height: var(--touch-min);
    transform: translate(-50%, -50%);
  }

  .status {
    margin-left: var(--touch-shift, 0px);
  }
}

/* Phones: the first line (32px) of the 56px two-line row; the thread line runs the row's height. */
@container page (width < 560px) {
  .cell--name {
    grid-column: 1 / 6;
    grid-row: 1;
    min-width: 0;
    padding-right: 0;
  }

  .is-sub .cell--name::before {
    bottom: -24px;
  }

  .is-sub.is-last .cell--name::before {
    bottom: 50%;
  }
}

/* Phone rows clip what they paint (content-visibility), and the first line is only 32px: the 44px
   targets of the chevron and the status icon start at the row's top edge instead of centring. */
@media (pointer: coarse) {
  @container page (width < 560px) {
    .chevron::after {
      top: calc((20px - 32px) / 2);
      transform: translateX(-50%);
    }

    .status :deep(.field-trigger)::after {
      top: calc((var(--control-xs) - 32px) / 2);
      transform: translateX(-50%);
    }
  }
}
</style>

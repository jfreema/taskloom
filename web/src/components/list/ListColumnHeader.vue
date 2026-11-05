<script setup lang="ts">
// The List's column header (8.7): the first rowgroup of the treegrid, one row of columnheader
// cells, sticky at the top of the scroll area. Columns don't sort (the Sort menu does), so there
// is no aria-sort. The gutter holds a checkbox for every visible top-level task. Hidden on phones.
import { computed, ref, watchEffect } from 'vue'
import { useSelection } from '../../stores/selection'
import UiCheckbox from '../ui/UiCheckbox.vue'
import { useListTable } from './listTable'

defineProps<{ tabStop: boolean }>()

const table = useListTable()
const selection = useSelection()
const checkbox = ref<InstanceType<typeof UiCheckbox> | null>(null)

const cols = computed(() => table.columns.value)
const ids = computed(() => table.model.renderedTopIds.value)
const selectedCount = computed(() => {
  const set = selection.ids
  let n = 0
  for (const id of ids.value) if (set.has(id)) n++
  return n
})
const all = computed(() => ids.value.length > 0 && selectedCount.value === ids.value.length)
const some = computed(() => selectedCount.value > 0 && !all.value)

// The header checkbox stays out of the tab order: Mod+A does the same from the rows.
watchEffect(
  () => {
    const input = checkbox.value?.el
    if (input) input.tabIndex = -1
  },
  { flush: 'post' },
)

function toggleAll(): void {
  if (all.value) selection.remove(ids.value)
  else selection.add(ids.value)
}
</script>

<template>
  <div role="rowgroup" class="column-header">
    <div role="row" class="header-row">
      <div role="columnheader" class="cell cell--gutter">
        <UiCheckbox
          v-if="table.selectable.value && ids.length"
          ref="checkbox"
          class="check"
          :class="{ 'is-on': selectedCount > 0 }"
          :model-value="all"
          :indeterminate="some"
          aria-label="Select all tasks"
          @change="toggleAll"
        />
        <span v-else class="sr-only">Selection</span>
      </div>
      <div role="columnheader" class="cell cell--name">Name</div>
      <div v-if="cols.status" role="columnheader" class="cell cell--status">Status</div>
      <div v-if="cols.assignees" role="columnheader" class="cell cell--assignees">Assignees</div>
      <div v-if="cols.dueDate" role="columnheader" class="cell cell--due">Due date</div>
      <div v-if="cols.priority" role="columnheader" class="cell cell--priority">Priority</div>
      <div v-if="cols.tags" role="columnheader" class="cell cell--tags">Tags</div>
      <div role="columnheader" class="cell cell--menu"><span class="sr-only">Actions</span></div>
    </div>
  </div>
</template>

<style scoped>
.column-header {
  position: sticky;
  top: 0;
  z-index: calc(var(--z-sticky) + 1);
  background: var(--bg-surface);
}

.header-row {
  display: flex;
  height: var(--colhead-h);
  border-bottom: 1px solid var(--border-subtle);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.cell {
  display: flex;
  flex: none;
  align-items: center;
  min-width: 0;
  padding: 0 var(--space-1-5);
  white-space: nowrap;
}

.cell--gutter {
  justify-content: flex-end;
  width: var(--gutter-w);
  padding: 0 var(--space-1-5) 0 0;
}

/* Revealed like the rows' checkboxes: on hover, or once anything is selected. */
.check {
  opacity: 0;
  transition: opacity var(--dur-1) var(--ease-out);
}

.header-row:hover .check,
.check:focus-within,
.check.is-on {
  opacity: 1;
}

.cell--name {
  flex: 1 1 0;
  min-width: var(--name-min);
  padding-left: calc(20px + var(--space-1-5) + var(--control-xs) + var(--space-1-5) + var(--touch-shift, 0px));
}

.cell--status {
  width: var(--w-status);
}

.cell--assignees {
  width: var(--w-assignees);
}

.cell--due {
  width: var(--w-due);
}

.cell--priority {
  width: var(--w-priority);
}

.cell--tags {
  width: var(--w-tags);
}

.cell--menu {
  width: var(--w-menu);
}

@container page (width < 900px) {
  .cell--tags {
    display: none;
  }
}

@container page (width < 720px) {
  /* Too narrow for the word; the cell keeps its name for screen readers. */
  .cell--priority {
    font-size: 0;
  }
}

@container page (width < 560px) {
  .column-header {
    display: none;
  }
}
</style>

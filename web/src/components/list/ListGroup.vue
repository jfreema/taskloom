<script setup lang="ts">
// One group of the List (8.7): a rowgroup named by the group, holding its header row (none for
// Group by None) and, while expanded, its rows: tasks and subtasks (ListRow), add rows
// (ListInlineAdd) and "Show N more". Fast-changing state (tab stop, selection, the open task, the
// drop indicator) is read here and handed to each row as plain props.
import { computed } from 'vue'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useListPageContext } from '../../composables/useListPageContext'
import { useSelection } from '../../stores/selection'
import type { CreateTaskRequest } from '../../types'
import ListGroupHeader from './ListGroupHeader.vue'
import ListInlineAdd from './ListInlineAdd.vue'
import ListRow from './ListRow.vue'
import { useListTable } from './listTable'
import { headerKey, type AddRowItem, type GroupView } from './useListModel'

const props = defineProps<{
  view: GroupView
  /** The placeholder of the first add row of an empty list (8.7). */
  firstAddKey: string | null
}>()

const table = useListTable()
const page = useListPageContext()
const selection = useSelection()
const panel = useTaskPanel()

const indicator = computed(() => {
  const i = table.dnd.indicator.value
  return i && i.groupKey === props.view.key ? i : null
})

function dropFor(key: string): 'top' | 'bottom' | null {
  const i = indicator.value
  return i && i.key === key ? i.edge : null
}

function defaultsFor(row: AddRowItem): Partial<CreateTaskRequest> {
  // Subtasks take the inherited filter values only; tasks also take the group's value, which wins.
  const inherited = page.createDefaults.value
  return row.parentId ? { ...inherited } : { ...inherited, ...(props.view.addDefaults ?? {}) }
}

function setOpen(key: string, on: boolean): void {
  const open = table.model.openAdd
  if (on) open.value = key
  else if (open.value === key) open.value = null
}

const label = computed(() => (props.view.hasHeader ? props.view.group.label : undefined))
</script>

<template>
  <div role="rowgroup" class="list-group" :class="{ 'is-drop-target': indicator !== null }" :aria-label="label">
    <ListGroupHeader
      v-if="view.hasHeader"
      :view="view"
      :tab-stop="table.tabStop.value === headerKey(view.key)"
      :drop="dropFor(headerKey(view.key))"
    />
    <template v-for="row in view.rows" :key="row.key">
      <ListRow
        v-if="row.kind === 'task'"
        :task="row.task"
        :level="row.level"
        :group-key="row.groupKey"
        :parent-id="row.parentId"
        :has-children="row.hasChildren"
        :expanded="row.expanded"
        :context="row.context"
        :kept="row.kept"
        :last="row.last"
        :tab-stop="table.tabStop.value === row.key"
        :selected="row.level === 1 && selection.ids.has(row.task.id)"
        :current="panel.openId.value === row.task.id"
        :flash="table.flashIds.value.has(row.task.id)"
        :moved="table.movedKey.value === row.key"
        :renaming="table.renamingId.value === row.task.id"
        :drop="dropFor(row.key)"
      />
      <ListInlineAdd
        v-else-if="row.kind === 'add'"
        :open="table.model.openAdd.value === row.key"
        :row-key="row.key"
        :group-key="row.groupKey"
        :parent-id="row.parentId"
        :placement="row.placement"
        :defaults="defaultsFor(row)"
        :tab-stop="table.tabStop.value === row.key"
        :placeholder="row.key === firstAddKey ? 'Add your first task… Enter to add, keep typing for more' : undefined"
        :drop="dropFor(row.key)"
        @update:open="setOpen(row.key, $event)"
      />
      <div v-else role="row" class="more-row" data-row :data-row-key="row.key">
        <div role="gridcell" class="more-cell" :aria-colspan="table.colCount.value">
          <button
            type="button"
            class="more-button focus-inset"
            data-nav
            :tabindex="table.tabStop.value === row.key ? 0 : -1"
            @click="table.model.showMore(row.groupKey)"
          >
            Show {{ row.count }} more
          </button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.list-group {
  position: relative;
}

/* The group a drag would drop into gets a hover tint (9.3). */
.list-group.is-drop-target :deep(.list-row:not(.is-selected):not(.is-current)),
.list-group.is-drop-target :deep(.add-row) {
  background: var(--bg-hover);
}

.more-row {
  display: flex;
  height: var(--row-list);
  border-bottom: 1px solid var(--border-subtle);
}

/* The button's text lines up with the titles. */
.more-cell {
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  padding-left: calc(var(--gutter-w) + 20px + var(--space-1-5) + var(--control-xs) + var(--touch-shift, 0px));
}

.more-button {
  height: var(--control-sm);
  padding: 0 var(--space-1-5);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--accent-text);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  cursor: pointer;
}

.more-button:hover {
  background: var(--bg-hover);
}

@container page (width < 560px) {
  .more-row {
    padding-left: var(--space-3);
  }
}

/* Touch (7.5): "Show N more" is a 44px target, the height of the row. */
@media (pointer: coarse) {
  .more-button {
    position: relative;
  }

  .more-button::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: var(--touch-min);
    transform: translateY(-50%);
  }
}
</style>

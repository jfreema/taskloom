<script setup lang="ts">
// The List's toolbar controls (8.4, 8.7), teleported into the toolbar's end: "Group: <value>",
// "Sort: <value>", "Columns" (a checkbox menu), the "Show completed tasks" toggle and "Expand all
// subtasks" / "Collapse all subtasks". Each choice is stored in the view settings (per user and
// list). Below 720px of page width the buttons are icons with their label in the tooltip; on
// phones they are chips that open full-width menus.
import { computed } from 'vue'
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronsDownUp, ChevronsUpDown, Columns3, Eye, EyeOff, Rows3 } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useListPageContext } from '../../composables/useListPageContext'
import type { GroupBy, SortField } from '../../lib/taskQuery'
import type { MenuItem } from '../ui/menu'
import UiIcon from '../ui/UiIcon.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { ListModel } from './useListModel'

const props = defineProps<{ model: ListModel }>()

const ctx = useListPageContext()
const { isPhone } = useBreakpoint()

const GROUP_LABEL: Record<GroupBy, string> = { status: 'Status', priority: 'Priority', due: 'Due date', none: 'None' }
const GROUP_ORDER: GroupBy[] = ['status', 'priority', 'due', 'none']
const SORT_LABEL: Record<SortField, string> = {
  manual: 'Manual',
  dueDate: 'Due date',
  priority: 'Priority',
  title: 'Name',
  createdAt: 'Created',
}
const SORT_ORDER: SortField[] = ['manual', 'dueDate', 'priority', 'title', 'createdAt']

const config = computed(() => ctx.config.value)
const groupLabel = computed(() => `Group: ${GROUP_LABEL[config.value.groupBy]}`)
const sortLabel = computed(() => `Sort: ${SORT_LABEL[config.value.sort.field]}`)
const manual = computed(() => config.value.sort.field === 'manual')
const sortTip = computed(() =>
  manual.value ? sortLabel.value : `Sorted by ${SORT_LABEL[config.value.sort.field]}. Choose Manual sort to reorder tasks.`,
)
const completedShown = computed(() => config.value.showCompleted)
const expanded = computed(() => props.model.anyExpanded.value)
const expandLabel = computed(() => (expanded.value ? 'Collapse all subtasks' : 'Expand all subtasks'))

function groupItems(): MenuItem[] {
  return [
    { kind: 'label', id: 'group-label', label: 'Group by' },
    ...GROUP_ORDER.map((g) => ({
      kind: 'radio' as const,
      id: `group-${g}`,
      label: GROUP_LABEL[g],
      checked: config.value.groupBy === g,
      onSelect: () => {
        ctx.config.value.groupBy = g
      },
    })),
  ]
}

function sortItems(): MenuItem[] {
  const isManual = config.value.sort.field === 'manual'
  return [
    { kind: 'label', id: 'sort-label', label: 'Sort by' },
    ...SORT_ORDER.map((f) => ({
      kind: 'radio' as const,
      id: `sort-${f}`,
      label: SORT_LABEL[f],
      checked: config.value.sort.field === f,
      onSelect: () => {
        ctx.config.value.sort = { field: f, dir: f === 'manual' ? 'asc' : config.value.sort.dir }
      },
    })),
    { kind: 'separator', id: 'sort-sep' },
    { kind: 'label', id: 'dir-label', label: 'Direction' },
    {
      kind: 'radio',
      id: 'dir-asc',
      label: 'Ascending',
      icon: ArrowUp,
      checked: config.value.sort.dir === 'asc',
      disabled: isManual,
      onSelect: () => {
        ctx.config.value.sort = { ...config.value.sort, dir: 'asc' }
      },
    },
    {
      kind: 'radio',
      id: 'dir-desc',
      label: 'Descending',
      icon: ArrowDown,
      checked: config.value.sort.dir === 'desc' && !isManual,
      disabled: isManual,
      onSelect: () => {
        ctx.config.value.sort = { ...config.value.sort, dir: 'desc' }
      },
    },
  ]
}

function columnItems(): MenuItem[] {
  const c = config.value.columns
  const byStatus = config.value.groupBy === 'status'
  const toggle = (key: keyof typeof c) => () => {
    ctx.config.value.columns = { ...ctx.config.value.columns, [key]: !ctx.config.value.columns[key] }
  }
  return [
    { kind: 'label', id: 'cols-label', label: 'Columns' },
    {
      kind: 'checkbox',
      id: 'col-status',
      label: byStatus ? 'Status (hidden while grouped by status)' : 'Status',
      checked: c.status && !byStatus,
      disabled: byStatus,
      onSelect: toggle('status'),
    },
    { kind: 'checkbox', id: 'col-assignees', label: 'Assignees', checked: c.assignees, onSelect: toggle('assignees') },
    { kind: 'checkbox', id: 'col-due', label: 'Due date', checked: c.dueDate, onSelect: toggle('dueDate') },
    { kind: 'checkbox', id: 'col-priority', label: 'Priority', checked: c.priority, onSelect: toggle('priority') },
    { kind: 'checkbox', id: 'col-tags', label: 'Tags', checked: c.tags, onSelect: toggle('tags') },
  ]
}

function toggleCompleted(): void {
  ctx.config.value.showCompleted = !ctx.config.value.showCompleted
}

function toggleExpanded(): void {
  if (expanded.value) props.model.collapseAll()
  else props.model.expandAll()
}
</script>

<template>
  <div class="list-view-menus" :class="{ 'is-phone': isPhone }">
    <UiMenu :items="groupItems" aria-label="Group by">
      <template #trigger="{ props: t }">
        <button v-bind="t" v-tooltip="groupLabel" type="button" class="tool" :aria-label="groupLabel">
          <UiIcon :icon="Rows3" :size="16" />
          <span class="text">{{ groupLabel }}</span>
        </button>
      </template>
    </UiMenu>
    <UiMenu :items="sortItems" aria-label="Sort by">
      <template #trigger="{ props: t }">
        <button v-bind="t" v-tooltip="sortTip" type="button" class="tool" :class="{ 'is-active': !manual }" :aria-label="sortLabel">
          <UiIcon :icon="ArrowUpDown" :size="16" />
          <span class="text">{{ sortLabel }}</span>
          <UiIcon v-if="!manual" class="dir" :icon="config.sort.dir === 'desc' ? ArrowDown : ArrowUp" :size="14" />
        </button>
      </template>
    </UiMenu>
    <UiMenu :items="columnItems" aria-label="Columns" placement="bottom-end">
      <template #trigger="{ props: t }">
        <button v-bind="t" v-tooltip="'Columns'" type="button" class="tool tool--icon" aria-label="Columns">
          <UiIcon :icon="Columns3" :size="16" />
        </button>
      </template>
    </UiMenu>
    <button
      v-tooltip="'Show completed tasks'"
      type="button"
      class="tool tool--icon"
      :class="{ 'is-active': !completedShown }"
      aria-label="Show completed tasks"
      :aria-pressed="completedShown ? 'true' : 'false'"
      @click="toggleCompleted"
    >
      <UiIcon :icon="completedShown ? Eye : EyeOff" :size="16" />
    </button>
    <button v-tooltip="expandLabel" type="button" class="tool tool--icon" :aria-label="expandLabel" @click="toggleExpanded">
      <UiIcon :icon="expanded ? ChevronsDownUp : ChevronsUpDown" :size="16" />
    </button>
  </div>
</template>

<style scoped>
.list-view-menus {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}

.tool {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-1-5);
  height: var(--control-sm);
  padding: 0 var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.tool:hover,
.tool[aria-expanded="true"] {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.tool.is-active {
  color: var(--accent-text);
}

.tool--icon {
  justify-content: center;
  width: var(--control-sm);
  padding: 0;
}

.dir {
  margin-left: calc(-1 * var(--space-0-5));
}

/* Narrow pages: icons only, the label in the tooltip (8.4). */
@container page (width < 720px) {
  .tool .text,
  .tool .dir {
    display: none;
  }

  .tool {
    justify-content: center;
    width: var(--control-sm);
    padding: 0;
  }
}

/* Phones: round chips, like the rest of the toolbar row. */
.is-phone {
  gap: var(--space-1-5);
}

.is-phone .tool {
  width: auto;
  min-width: var(--control-sm);
  padding: 0 var(--space-2);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.is-phone .tool.is-active {
  border-color: var(--accent);
  background: var(--bg-selected);
  color: var(--accent-text);
}
</style>

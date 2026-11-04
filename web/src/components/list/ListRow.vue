<script setup lang="ts">
// A task row of the List (8.7): top-level (aria-level 1) or subtask (aria-level 2, indented with a
// thread line, no checkbox or grip). The row is the focus target (roving tabindex: only the tab
// stop's controls are in the tab order). A click outside a control opens the task panel.
// Cells: gutter (checkbox and grip, revealed), name (ListRowName), the optional columns
// (ListRowFields) and the row menu.
// Below 560px of page width the row turns into two lines: status, title and progress, then due
// date, priority flag and avatars.
import { computed, ref, watchEffect } from 'vue'
import { Ellipsis, GripVertical } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { useTasks } from '../../stores/tasks'
import type { Task, Uuid } from '../../types'
import LazyMenu from '../fields/LazyMenu.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { useListTable } from './listTable'
import ListRowFields from './ListRowFields.vue'
import ListRowName from './ListRowName.vue'

const props = defineProps<{
  task: Task
  level: 1 | 2
  groupKey: string
  parentId: Uuid | null
  hasChildren: boolean
  expanded: boolean
  context: boolean
  kept: boolean
  /** The last row of its parent's block: the thread line ends here. */
  last: boolean
  tabStop: boolean
  selected: boolean
  /** Open in the task panel. */
  current: boolean
  flash: boolean
  renaming: boolean
  /** The drop indicator this row draws. */
  drop: 'top' | 'bottom' | null
}>()

const table = useListTable()
const tasks = useTasks()
const actions = useTaskActions()

const rowEl = ref<HTMLElement | null>(null)
const gripEl = ref<HTMLElement | null>(null)
const checkbox = ref<InstanceType<typeof UiCheckbox> | null>(null)
const menuOpen = ref(false)
/**
 * The row's pickers and menu mount once the pointer or focus reaches the row (and always on the tab
 * stop, whose controls Tab can reach directly), so a long list mounts only their triggers (S10).
 */
const armed = ref(false)
const lazy = computed(() => !armed.value && !props.tabStop)
function arm(): void {
  armed.value = true
}

const top = computed(() => props.level === 1)
const readonly = computed(() => !table.canEdit.value || tasks.isPendingDelete(props.task.id))
const done = computed(() => tasks.isDone(props.task))
/** Controls are tabbable only in the tab stop's row. */
const inner = computed(() => (props.tabStop ? undefined : -1))
const showGrip = computed(() => top.value && table.dnd.enabled.value && !readonly.value)
const showCheck = computed(() => top.value && table.selectable.value)

// UiCheckbox puts attributes on its wrapper, so its input's tab order is set here.
watchEffect(
  () => {
    const input = checkbox.value?.el
    if (input) input.tabIndex = props.tabStop ? 0 : -1
  },
  { flush: 'post' },
)

const { dragging } = table.dnd.row(
  rowEl,
  gripEl,
  () => ({ kind: 'task', key: `t:${props.task.id}`, taskId: props.task.id, level: props.level, parentId: props.parentId, groupKey: props.groupKey }),
  // An unarmed row's grip is invisible: it becomes draggable once the pointer reaches the row.
  () => props.renaming || readonly.value || lazy.value,
)

function menuItems() {
  return actions.rowMenuItems(props.task, {
    variant: 'row',
    canReorder: top.value && table.canReorder.value,
    siblingIds: () => table.model.siblingIds(props.task.id),
    onAddSubtask: top.value ? () => table.addSubtask(props.task.id) : undefined,
    onRename: () => table.startRename(props.task.id),
  })
}

// ---- Pointer ----

const CONTROLS = 'button, a[href], input, textarea, select, label, [role="checkbox"], [role="button"], [contenteditable], [data-no-open]'

function onClick(e: MouseEvent): void {
  if (props.renaming || e.defaultPrevented) return
  const target = e.target instanceof Element ? e.target : null
  const control = target?.closest(CONTROLS)
  if (control && control !== rowEl.value && rowEl.value?.contains(control)) return
  // A drag-select of the title isn't a click.
  const sel = window.getSelection()
  if (sel && !sel.isCollapsed && rowEl.value?.contains(sel.anchorNode)) return
  table.openTask(props.task.id)
}

/** Right-click (or a long press) opens the row menu, as the ContextMenu key does. */
function onContextMenu(e: MouseEvent): void {
  const target = e.target instanceof Element ? e.target : null
  if (props.renaming || target?.closest('input, textarea')) return
  const button = rowEl.value?.querySelector<HTMLElement>('[data-row-menu]')
  if (!button) return
  e.preventDefault()
  if (!menuOpen.value) button.click()
}

function onCheck(_value: boolean, e: MouseEvent | KeyboardEvent): void {
  table.check(props.task.id, e.shiftKey)
}

</script>

<template>
  <div
    ref="rowEl"
    role="row"
    class="list-row focus-inset"
    :class="{
      'is-sub': !top,
      'is-last': last,
      'is-selected': selected,
      'is-current': current,
      'is-done': done,
      'is-context': context,
      'is-dragging': dragging,
      'flash-new': flash,
      'drop-top': drop === 'top',
      'drop-bottom': drop === 'bottom',
    }"
    data-row
    data-nav
    :data-row-key="`t:${task.id}`"
    :data-task-row="task.id"
    :aria-label="task.title"
    :aria-level="level"
    :aria-expanded="hasChildren ? (expanded ? 'true' : 'false') : undefined"
    :aria-selected="showCheck ? (selected ? 'true' : 'false') : undefined"
    :aria-current="current ? 'true' : undefined"
    :tabindex="tabStop ? 0 : -1"
    @click="onClick"
    @contextmenu="onContextMenu"
    @pointerenter="arm"
    @focusin="arm"
  >
    <div role="gridcell" class="cell cell--gutter">
      <span v-if="showGrip" ref="gripEl" class="grip reveal" data-no-open aria-hidden="true">
        <UiIcon v-if="!lazy" :icon="GripVertical" :size="14" />
      </span>
      <UiCheckbox
        v-if="showCheck"
        ref="checkbox"
        class="check reveal"
        :model-value="selected"
        :aria-label="`Select ${task.title}`"
        @change="onCheck"
      />
    </div>

    <ListRowName
      :task="task"
      :top="top"
      :has-children="hasChildren"
      :expanded="expanded"
      :renaming="renaming"
      :kept="kept"
      :readonly="readonly"
      :tab-stop="tabStop"
      :lazy="lazy"
    />

    <ListRowFields :task="task" :readonly="readonly" :done="done" :tab-stop="tabStop" :lazy="lazy" />

    <div role="gridcell" class="cell cell--menu">
      <LazyMenu v-model:open="menuOpen" :lazy="lazy" :items="menuItems" :aria-label="`Actions for ${task.title}`" placement="bottom-end">
        <template #trigger="{ props: trigger }">
          <UiIconButton
            v-bind="trigger"
            class="reveal reveal-touch"
            data-row-menu
            :icon="Ellipsis"
            :label="`More actions for ${task.title}`"
            size="xs"
            :tabindex="inner"
          />
        </template>
      </LazyMenu>
    </div>
  </div>
</template>

<style scoped>
.list-row {
  position: relative;
  display: flex;
  align-items: stretch;
  height: var(--row-list);
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-surface);
  color: var(--text-primary);
  cursor: default;
  transition: background-color var(--dur-1) var(--ease-out);
}

.list-row:hover {
  background: var(--bg-hover);
}

.list-row.is-selected {
  background: var(--bg-selected);
}

.list-row.is-current {
  background: var(--bg-selected);
  box-shadow: inset 2px 0 0 var(--accent);
}

.list-row.is-dragging {
  opacity: 0.4;
  outline: 1px dashed var(--border-default);
  outline-offset: -1px;
}

/* ---- Cells ---- */
.cell {
  display: flex;
  flex: none;
  align-items: center;
  min-width: 0;
}

.cell--gutter {
  width: var(--gutter-w);
  justify-content: flex-end;
  gap: var(--space-0-5);
  padding-right: var(--space-1-5);
}

.grip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 14px;
  height: var(--control-xs);
  color: var(--text-tertiary);
  cursor: grab;
}

.grip:active {
  cursor: grabbing;
}

/* Once anything is selected, every row shows its checkbox (8.7). */
.has-selection .check {
  opacity: 1;
  visibility: visible;
}

.cell--menu {
  width: var(--w-menu);
  justify-content: center;
}

/* ---- Drop indicator: a 2px --accent line with a 6px dot, on the closest edge ---- */
.drop-top::before,
.drop-bottom::after {
  content: '';
  position: absolute;
  z-index: 1;
  left: var(--gutter-w);
  right: 0;
  height: 6px;
  background:
    radial-gradient(circle at 3px 3px, var(--accent) 3px, transparent 3.5px),
    linear-gradient(var(--accent), var(--accent)) 0 2px / 100% 2px no-repeat;
  pointer-events: none;
}

.drop-top::before {
  top: -4px;
}

.drop-bottom::after {
  bottom: -4px;
}

/* Phones: two lines (56px): status, title and progress; then due date, flag and avatars (8.7).
   The first column indents the second line to the title. Empty cells aren't rendered there, so
   their auto columns collapse; the cells space themselves (no column gap). */
@container page (width < 560px) {
  .list-row {
    --title-indent: calc(20px + var(--space-1-5) + var(--control-xs) + var(--space-1-5));
    display: grid;
    grid-template-columns: var(--title-indent) auto auto auto minmax(0, 1fr) auto;
    grid-template-rows: 32px 20px;
    align-content: start;
    height: 56px;
    padding-left: var(--space-3);
  }

  .list-row.is-sub {
    --title-indent: calc(var(--space-6) + 20px + var(--space-1-5) + var(--control-xs) + var(--space-1-5));
  }

  .cell--gutter {
    display: none;
  }

  .cell--menu {
    grid-column: 6;
    grid-row: 1 / 3;
    width: var(--w-menu);
  }
}
</style>

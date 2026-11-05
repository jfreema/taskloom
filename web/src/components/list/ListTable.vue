<script setup lang="ts">
// The List's treegrid (8.7): `role="treegrid"` named "<List> tasks", multiselectable, whose only
// children are rowgroups: the column header, then one per group.
// - Roving tabindex and focus rescue (useListFocus), keys (useListKeyboard), dragging (useListDnd),
//   the panel's J/K order (setOrder), and following the open task (useFollowOpenTask).
// - Rows mount in batches (useListModel); the grid is aria-busy until they all have.
// - Pickers opened by S/A/D/P use the cell's trigger; for a hidden column, ListPickerFallback opens
//   one anchored to the row.
import { computed, nextTick, onBeforeUnmount, onMounted, onUpdated, ref, shallowRef, watch } from 'vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useListPageContext } from '../../composables/useListPageContext'
import { useTaskActions, type PickField } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useSelection } from '../../stores/selection'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import type { Uuid } from '../../types'
import ListColumnHeader from './ListColumnHeader.vue'
import ListGroup from './ListGroup.vue'
import ListPickerFallback from './ListPickerFallback.vue'
import { provideListTable, type ListColumns } from './listTable'
import { useFollowOpenTask } from './useFollowOpenTask'
import { useListDnd } from './useListDnd'
import { useListFocus } from './useListFocus'
import { focusRow, useListKeyboard } from './useListKeyboard'
import { taskKey, type GroupView, type ListModel } from './useListModel'

const props = defineProps<{
  model: ListModel
  /** The list has no tasks at all: the first Not started group's add row opens (8.7). */
  emptyList: boolean
}>()

const ctx = useListPageContext()
const tasks = useTasks()
const selection = useSelection()
const panel = useTaskPanel()
const actions = useTaskActions()
const ui = useUi()
const { isPhone, coarse } = useBreakpoint()

const root = ref<HTMLElement | null>(null)
const model = props.model

// ---- What the rows share ----

const canEdit = computed(() => ctx.canEdit.value)
const config = computed(() => ctx.config.value)

const columns = computed<ListColumns>(() => {
  const c = config.value.columns
  return {
    status: c.status && config.value.groupBy !== 'status',
    assignees: c.assignees,
    dueDate: c.dueDate,
    priority: c.priority,
    tags: c.tags,
  }
})
const colCount = computed(() => 3 + Object.values(columns.value).filter(Boolean).length)

const selectable = computed(() => canEdit.value && !isPhone.value && (!coarse.value || selection.mode || selection.count > 0))
const canReorder = computed(() => canEdit.value && config.value.sort.field === 'manual' && config.value.groupBy === 'status')
const dragEnabled = computed(() => canReorder.value && !isPhone.value)

/**
 * Two-line phone rows below 560px of page width (the CSS switches on the same width). The window's
 * width is the first guess; the observer's first report, which comes before the first paint, settles
 * it without forcing a layout while the rows mount.
 */
const phoneRows = ref(window.innerWidth < 560)
let resize: ResizeObserver | null = null
onMounted(() => {
  const el = root.value?.parentElement
  if (!el) return
  const measure = () => {
    phoneRows.value = el.clientWidth < 560
  }
  if (typeof ResizeObserver === 'undefined') {
    measure()
    return
  }
  resize = new ResizeObserver(measure)
  resize.observe(el)
})
onBeforeUnmount(() => resize?.disconnect())

const dnd = useListDnd({ listId: ctx.listId, model, enabled: dragEnabled })

// Progressive mounting: once a render is on screen, the model mounts the next batch (useListModel).
onMounted(() => model.rendered())
onUpdated(() => {
  model.rendered()
  follow.rendered()
})
onBeforeUnmount(() => model.detached())

// ---- Roving tab stop and focus rescue ----

const { tabStop, remembered, movedKey, follow: followRow, onFocusIn, onFocusOut } = useListFocus(root, model)

// ---- The panel ----

watch(() => model.taskOrder.value, (ids) => panel.setOrder(ids), { immediate: true })

const follow = useFollowOpenTask(root, () => panel.openId.value, model.mounting)

// ---- Rename, flash, add rows ----

const renamingId = ref<Uuid | null>(null)
const flashIds = shallowRef<ReadonlySet<Uuid>>(new Set())

function startRename(id: Uuid): void {
  if (!canEdit.value || tasks.isPendingDelete(id)) return
  renamingId.value = id
}

function endRename(): void {
  const id = renamingId.value
  renamingId.value = null
  // The field took focus from the pencil, which is gone now: the row gets it back (F2 returns it there too).
  void nextTick(() => {
    const active = document.activeElement
    if (id && (!active || active === document.body || !active.isConnected)) focusRow(root.value, taskKey(id))
  })
}

function created(id: Uuid, where?: { groupKey: string; placement: 'Top' | 'Bottom'; parentId: Uuid | null }): void {
  if (where) model.pin(id, where)
  const next = new Set(flashIds.value)
  next.add(id)
  flashIds.value = next
  setTimeout(() => {
    const rest = new Set(flashIds.value)
    rest.delete(id)
    flashIds.value = rest
  }, 1000)
  // The first subtask of a parent opens its block.
  void nextTick(() => {
    const parentId = tasks.get(id)?.parentTaskId
    if (parentId) model.toggleExpanded(parentId, true)
  })
}

function addSubtask(parentId: Uuid): void {
  const t = tasks.get(parentId)
  if (!t || !canEdit.value) return
  if (t.subtaskCount > 0) model.toggleExpanded(parentId, true)
  model.openAdd.value = `as:${parentId}`
}

function firstAddGroup(): GroupView | undefined {
  const groups = model.groups.value
  return groups.find((g) => g.addDefaults && g.group.status?.category === 'NotStarted') ?? groups.find((g) => g.addDefaults)
}

/** The empty list's add row, which shows the first-task placeholder. */
const firstAddKey = ref<string | null>(null)

function openFirstAdd(): void {
  const g = firstAddGroup()
  if (!g) return
  if (g.collapsed) model.toggleGroup(g.key, false)
  model.openAdd.value = `a:${g.key}`
}

onMounted(() => {
  if (props.emptyList && canEdit.value) {
    const g = firstAddGroup()
    if (g) firstAddKey.value = `a:${g.key}`
    openFirstAdd()
  }
})

watch(
  () => props.emptyList,
  (empty) => {
    if (!empty) firstAddKey.value = null
  },
)

// "New list" asks for the first group's add row (ui.inlineAddRequest).
watch(
  () => ui.inlineAddRequest,
  (req) => {
    if (!req || req.listId !== ctx.listId.value || !canEdit.value) return
    ui.inlineAddRequest = null
    openFirstAdd()
  },
  { immediate: true },
)

// ---- Pickers by shortcut ----

const fallback = ref<InstanceType<typeof ListPickerFallback> | null>(null)

function pick(id: Uuid, field: PickField): void {
  const row = root.value?.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(id)}"]`)
  if (!row) return
  const trigger = row.querySelector<HTMLElement>(`[data-field="${field}"] button`)
  if (trigger && trigger.getClientRects().length > 0) {
    trigger.click()
    return
  }
  fallback.value?.open(id, field, row)
}

// ---- The context ----

provideListTable({
  listId: ctx.listId,
  canEdit,
  columns,
  colCount,
  selectable,
  canReorder,
  phoneRows,
  model,
  dnd,
  tabStop,
  renamingId,
  flashIds,
  movedKey,
  openTask: (id) => panel.open(id),
  check: (id, shift) => {
    if (shift) selection.selectRange(id, model.renderedTopIds.value)
    else selection.toggle(id)
  },
  startRename,
  endRename,
  rename: (id, title) => void actions.rename(id, title),
  setStatus: (id, statusId) => {
    void actions.setStatus(id, statusId)
    followRow(taskKey(id))
  },
  addSubtask,
  pick,
  created,
})

useListKeyboard({ root, model, canEdit, selectable, canReorder, startRename, pick, remembered })

const listName = computed(() => ctx.list.value?.name ?? 'List')

/** Focus the row of a task (the view uses it after its own actions). */
defineExpose({ focusTask: (id: Uuid) => focusRow(root.value, taskKey(id)) })
</script>

<template>
  <div
    ref="root"
    role="treegrid"
    class="list-table"
    :class="{ 'has-selection': selection.count > 0, 'is-guest': !canEdit }"
    :aria-label="`${listName} tasks`"
    aria-multiselectable="true"
    :aria-busy="model.mounting.value ? 'true' : undefined"
    data-shortcut-scope="grid"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
  >
    <ListColumnHeader :tab-stop="false" />
    <ListGroup v-for="g in model.groups.value" :key="g.key" :view="g" :first-add-key="firstAddKey" />
  </div>

  <ListPickerFallback ref="fallback" />
</template>

<style scoped>
.list-table {
  --gutter-w: 40px;
  --name-min: 280px;
  --w-status: 140px;
  --w-assignees: 112px;
  --w-due: 112px;
  --w-priority: 112px;
  --w-tags: 160px;
  --w-menu: 40px;
  --colhead-h: 32px;
  --touch-shift: 0px;
  /* As wide as the page, and never narrower than its columns: wider tables scroll sideways. */
  min-width: min-content;
}

/* Below 900px the Name column may narrow to 200px, so every column fits a 760px page even with the
   Status column (any grouping but status): a laptop window beside the sidebar never scrolls sideways. */
@container page (width < 900px) {
  .list-table {
    --w-tags: 0px;
    --name-min: 200px;
  }
}

@container page (width < 720px) {
  .list-table {
    --w-priority: 48px;
  }
}

@container page (width < 560px) {
  .list-table {
    --gutter-w: 0px;
    --colhead-h: 0px;
  }
}

/* Touch: the row menu's 44px hit area fits its cell, so it never widens the table. The status icon
   moves right by --touch-shift, so its 44px target and the subtask chevron's sit side by side; the
   titles and everything lined up with the icons or titles move with it. */
@media (pointer: coarse) {
  .list-table {
    --w-menu: 44px;
    --touch-shift: var(--space-4);
  }
}
</style>

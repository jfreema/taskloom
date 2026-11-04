<script setup lang="ts">
// A Board card (8.8): an article named by the title, focusable, with data-task-row for the panel.
// - The title (3 lines at most), up to 2 tags, and a meta row: due date, priority, subtask
//   progress, comment and attachment counts, then the assignees.
// - Revealed controls: the complete toggle before the title (in the status colour; faint until
//   the card is hovered or focused, so the title never shifts) and the "..." menu in the corner,
//   which touch screens always show. A right-click opens that menu too.
// - A click opens the panel, unless it lands on a control or ends a text selection.
// - The whole card drags (useDnd, lifted preview) except its controls; while it drags its slot is a
//   dashed placeholder. As a drop target it shows the indicator at its closest edge, unless the
//   drop would leave the card where it is.
// - F2 turns the title into a field (BoardView sets renamingId).
import { computed, ref, watch } from 'vue'
import { Ellipsis } from '@lucide/vue'
import { activeDrag, useDraggable, useDropTarget } from '../../composables/useDnd'
import { useTaskActions } from '../../composables/useTaskActions'
import { useMembers } from '../../stores/members'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status, Task } from '../../types'
import CompleteToggle from '../fields/CompleteToggle.vue'
import DueDate from '../fields/DueDate.vue'
import LazyMenu from '../fields/LazyMenu.vue'
import PriorityFlag from '../fields/PriorityFlag.vue'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import TagList from '../fields/TagList.vue'
import TaskCounts from '../fields/TaskCounts.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import { useBoard } from './boardContext'

const props = withDefaults(
  defineProps<{
    task: Task
    /** The column it renders in (during the completion hold, not yet the task's own status). */
    column: Status
    /** The column's own drop line at this card: the top of the column, or its end. */
    dropHint?: 'top' | 'bottom' | null
  }>(),
  { dropHint: null },
)

const board = useBoard()
const tasks = useTasks()
const members = useMembers()
const workspaces = useWorkspaces()
const actions = useTaskActions()

const el = ref<HTMLElement | null>(null)
const menuOpen = ref(false)

const id = computed(() => props.task.id)
const done = computed(() => tasks.isDone(props.task))
const color = computed(() => workspaces.statusById(props.task.statusId)?.color ?? props.column.color)
const current = computed(() => board.openId.value === id.value)
const renaming = computed(() => board.renamingId.value === id.value)
const flash = computed(() => board.flashIds.value.has(id.value))
const kept = computed(() => board.keptIds.value.has(id.value))
const people = computed(() => props.task.assigneeIds.map((uid) => ({ id: uid, displayName: members.nameOf(uid) })))
const hasMeta = computed(() => {
  const t = props.task
  return (
    t.dueDate !== null ||
    t.priority !== 'None' ||
    t.subtaskCount > 0 ||
    t.commentCount > 0 ||
    t.attachmentCount > 0 ||
    t.assigneeIds.length > 0
  )
})

// ---- Drag and drop ----

const { dragging } = useDraggable(el, {
  data: () => ({ kind: 'task', id: id.value, listId: props.task.listId, fromStatusId: props.task.statusId }),
  preview: 'lift',
  disabled: () => !board.canDrag.value || renaming.value || tasks.isPendingDelete(id.value),
})

const { edge } = useDropTarget(el, {
  data: () => ({ type: 'card', id: id.value }),
  accepts: (d) => board.canDrag.value && d.listId === board.listId.value,
  edges: ['top', 'bottom'],
  onDrop: (d, e) => board.drop(d, props.column.id, { cardId: id.value, edge: e === 'top' ? 'top' : 'bottom' }),
})

watch(edge, (e) => {
  if (e) board.hover.value = { id: id.value, statusId: props.column.id }
  else if (board.hover.value?.id === id.value) board.hover.value = null
})

/** The edge to draw the indicator at, unless dropping there would change nothing. */
const indicator = computed<'top' | 'bottom' | null>(() => {
  if (props.dropHint) return props.dropHint
  const e = edge.value
  const d = activeDrag.value
  if (!d || (e !== 'top' && e !== 'bottom')) return null
  return board.resolveDrop(d, props.column.id, { cardId: id.value, edge: e }) ? e : null
})

// ---- Pointer ----

const CONTROLS = 'button, a[href], input, textarea, select, label, [role="checkbox"], [role="button"], [contenteditable], [data-no-open]'

/**
 * Whether the click went through a control inside the card. It reads the event's path, fixed when
 * the click was dispatched: a control may re-render under the pointer before the click reaches the
 * card (the lazy menu button swaps itself for the real one), leaving e.target detached.
 */
function viaControl(e: MouseEvent): boolean {
  for (const node of e.composedPath()) {
    if (node === el.value) return false
    if (node instanceof Element && node.matches(CONTROLS)) return true
  }
  return false
}

function onClick(e: MouseEvent): void {
  if (renaming.value || e.defaultPrevented || viaControl(e)) return
  const sel = window.getSelection()
  if (sel && !sel.isCollapsed && el.value?.contains(sel.anchorNode)) return
  board.openTask(id.value)
}

function onContextMenu(e: MouseEvent): void {
  const target = e.target instanceof Element ? e.target : null
  if (renaming.value || target?.closest('input, textarea')) return
  const button = el.value?.querySelector<HTMLElement>('[data-card-menu]')
  if (!button) return
  e.preventDefault()
  if (!menuOpen.value) button.click()
}

function toggle(): void {
  void actions.toggleComplete(id.value)
}

function menuItems() {
  return actions.rowMenuItems(props.task, {
    variant: 'card',
    canReorder: true,
    siblingIds: () => board.model.siblingIds(id.value),
    onPick: (field) => board.pick(id.value, field),
  })
}

function onEditing(on: boolean): void {
  if (!on) board.endRename(id.value)
}
</script>

<template>
  <article
    ref="el"
    class="board-card focus-inset"
    :class="{
      'is-done': done,
      'is-current': current,
      'is-dragging': dragging,
      'is-renaming': renaming,
      'flash-new': flash,
      'drop-top': indicator === 'top',
      'drop-bottom': indicator === 'bottom',
    }"
    tabindex="0"
    :aria-label="task.title"
    :aria-current="current ? 'true' : undefined"
    :data-task-row="task.id"
    :data-status-id="column.id"
    data-card
    data-row
    @click="onClick"
    @contextmenu="onContextMenu"
  >
    <div class="head">
      <CompleteToggle
        v-if="board.canEdit.value"
        class="toggle"
        :done="done"
        :label="task.title"
        :color="color"
        :size="16"
        @toggle="toggle"
      />
      <UiInlineEdit
        v-if="renaming"
        class="title-edit"
        editing
        multiline
        activate="none"
        :model-value="task.title"
        aria-label="Task title"
        :maxlength="500"
        @save="board.rename(task.id, $event)"
        @update:editing="onEditing"
      />
      <p v-else class="title">{{ task.title }}</p>
    </div>

    <TagList v-if="task.tagIds.length" class="tags" :tag-ids="task.tagIds" :max="2" />

    <div v-if="hasMeta" class="meta">
      <DueDate v-if="task.dueDate" :date="task.dueDate" :done="done" size="sm" />
      <PriorityFlag v-if="task.priority !== 'None'" :priority="task.priority" :size="14" />
      <SubtaskProgress :done="task.subtaskDoneCount" :total="task.subtaskCount" />
      <TaskCounts :task="task" />
      <span class="spacer" />
      <UiAvatarStack v-if="people.length" :users="people" :size="20" :max="3" />
    </div>

    <p v-if="kept" class="kept">Hidden by filters</p>

    <div v-if="!renaming" class="corner">
      <LazyMenu v-model:open="menuOpen" lazy :items="menuItems" :aria-label="`Actions for ${task.title}`" placement="bottom-end">
        <template #trigger="{ props: t }">
          <UiIconButton
            v-bind="t"
            class="reveal reveal-touch"
            data-card-menu
            :icon="Ellipsis"
            :label="`More actions for ${task.title}`"
            size="xs"
          />
        </template>
      </LazyMenu>
    </div>
  </article>
</template>

<style scoped>
.board-card {
  --card-bg: var(--bg-card);
  --card-tint: transparent;
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px var(--space-3);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-md);
  background: linear-gradient(var(--card-tint), var(--card-tint)), var(--card-bg);
  box-shadow: var(--shadow-1);
  color: var(--text-primary);
  cursor: pointer;
  transition: box-shadow var(--dur-1) var(--ease-out);
}

.board-card:hover {
  --card-tint: var(--bg-hover);
  box-shadow: var(--shadow-2);
}

.board-card.is-current {
  --card-bg: var(--bg-selected);
  box-shadow:
    inset 2px 0 0 var(--accent),
    var(--shadow-1);
}

.board-card.is-current:hover {
  box-shadow:
    inset 2px 0 0 var(--accent),
    var(--shadow-2);
}

/* The source slot while it drags: a dashed placeholder at 40% (8.1). */
.board-card.is-dragging {
  border-style: dashed;
  box-shadow: none;
  opacity: 0.4;
}

.board-card.is-renaming {
  cursor: default;
}

.head {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  min-width: 0;
}

/* Faint until the card is hovered or holds focus; a done card shows its check. */
.toggle {
  flex: none;
  margin-top: 2px;
  opacity: 0.5;
  transition: opacity var(--dur-1) var(--ease-out);
}

.board-card:hover .toggle,
.board-card:focus-within .toggle,
.board-card.is-done .toggle {
  opacity: 1;
}

.title {
  display: -webkit-box;
  flex: 1 1 auto;
  min-width: 0;
  margin: 0;
  overflow: hidden;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  overflow-wrap: anywhere;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 3;
  line-clamp: 3;
}

.is-done .title {
  color: var(--text-tertiary);
}

.title-edit {
  flex: 1 1 auto;
  min-width: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
}

.tags {
  min-width: 0;
}

.meta {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  min-height: 20px;
  color: var(--text-secondary);
}

.spacer {
  flex: 1 1 auto;
}

.kept {
  margin: 0;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

/* The menu sits over the title's end, on the card's own colour once shown, so revealing it moves nothing. */
.corner {
  position: absolute;
  top: 6px;
  right: 6px;
  border-radius: var(--radius-sm);
}

.board-card:hover .corner,
.board-card:focus-within .corner,
.corner:has([aria-expanded="true"]) {
  background: linear-gradient(var(--card-tint), var(--card-tint)), var(--card-bg);
}

/* Touch screens show only the menu (8.1): the title keeps clear of it, and a done card's check is
   just a mark (a tap on it opens the card). */
@media (hover: none) {
  .title {
    padding-right: var(--space-6);
  }

  .board-card:not(.is-done) .toggle {
    display: none;
  }

  .toggle {
    pointer-events: none;
  }
}

/* The drop indicator: a 2px accent line with a 6px dot, centred in the gap at the closest edge. */
.board-card.drop-top::before,
.board-card.drop-bottom::after {
  content: '';
  position: absolute;
  left: -4px;
  right: 0;
  height: 6px;
  pointer-events: none;
  background:
    radial-gradient(circle at 3px 3px, var(--accent) 3px, transparent 3.5px),
    linear-gradient(var(--accent), var(--accent)) 0 2px / 100% 2px no-repeat;
}

.board-card.drop-top::before {
  top: calc(-1px - var(--space-1) - 3px);
}

.board-card.drop-bottom::after {
  bottom: calc(-1px - var(--space-1) - 3px);
}
</style>

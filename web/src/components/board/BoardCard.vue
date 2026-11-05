<script setup lang="ts">
// A Board card (8.8): an article named by the title, focusable, with data-task-row for the panel.
// - The title (3 lines at most), up to 2 tags, and the meta row (BoardCardMeta).
// - Revealed controls: the complete toggle before the title (in the status colour; faint until
//   the card is hovered or focused, so the title never shifts) and the "..." menu in the corner
//   (BoardCardMenu), which touch screens always show. A right-click opens that menu too. They are
//   in the tab order only while focus is in the card, so Tab steps from card to card (as the
//   List's rows do).
// - A parent shown only for its matching subtasks (filters) has a muted title (8.1).
// - A click opens the panel, unless it lands on a control or ends a text selection.
// - Drag and drop (useCardDnd): the whole card drags except its controls; while it drags its slot
//   is a dashed placeholder, and as a drop target it shows the indicator at its closest edge.
// - F2 turns the title into a field (BoardView sets renamingId).
import { computed, ref } from 'vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status, Task } from '../../types'
import CompleteToggle from '../fields/CompleteToggle.vue'
import TagList from '../fields/TagList.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import BoardCardMenu from './BoardCardMenu.vue'
import BoardCardMeta from './BoardCardMeta.vue'
import { useBoard } from './boardContext'
import { useCardDnd, type CardEdge } from './useCardDnd'

const props = withDefaults(
  defineProps<{
    task: Task
    /** The column it renders in (during the completion hold, not yet the task's own status). */
    column: Status
    /** The column's own drop line at this card: the top of the column, or its end. */
    dropHint?: CardEdge | null
  }>(),
  { dropHint: null },
)

const board = useBoard()
const tasks = useTasks()
const workspaces = useWorkspaces()
const actions = useTaskActions()

const el = ref<HTMLElement | null>(null)

const id = computed(() => props.task.id)
const done = computed(() => tasks.isDone(props.task))
const color = computed(() => workspaces.statusById(props.task.statusId)?.color ?? props.column.color)
const current = computed(() => board.openId.value === id.value)
const renaming = computed(() => board.renamingId.value === id.value)
const flash = computed(() => board.flashIds.value.has(id.value))
const kept = computed(() => board.keptIds.value.has(id.value))
const context = computed(() => board.contextIds.value.has(id.value))
/** Focus is on the card or a control inside it: only then are its controls in the tab order. */
const focusWithin = ref(false)

const { dragging, indicator } = useCardDnd(el, {
  task: () => props.task,
  columnId: () => props.column.id,
  renaming: () => renaming.value,
  dropHint: () => props.dropHint,
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
  if (button.getAttribute('aria-expanded') !== 'true') button.click()
}

function toggle(): void {
  void actions.toggleComplete(id.value)
}

function onEditing(on: boolean): void {
  if (!on) board.endRename(id.value)
}

function onFocusIn(): void {
  focusWithin.value = true
}

function onFocusOut(e: FocusEvent): void {
  const next = e.relatedTarget
  if (!(next instanceof Node) || !el.value?.contains(next)) focusWithin.value = false
}
</script>

<template>
  <article
    ref="el"
    class="board-card focus-inset"
    :class="{
      'is-done': done,
      'is-context': context,
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
    @focusin="onFocusIn"
    @focusout="onFocusOut"
  >
    <div class="head">
      <CompleteToggle
        v-if="board.canEdit.value"
        class="toggle"
        :done="done"
        :label="task.title"
        :color="color"
        :size="16"
        :tabindex="focusWithin ? 0 : -1"
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
    <BoardCardMeta :task="task" :done="done" />
    <p v-if="kept" class="kept">Hidden by filters</p>
    <BoardCardMenu v-if="!renaming" :task="task" :focusable="focusWithin" />
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

/* Cards far from the viewport skip style, layout and paint (S10, as the List's rows do), so changes
   to the whole document (a modal's inert, the theme) restyle only the cards on screen. A skipped
   card keeps its last rendered height; one never rendered counts as a one-line card. A card clips
   what it paints, so one that shows a drop indicator (it crosses the card's edge) renders in full,
   and touch screens, where the menu's 44px target crosses the card's corner, don't skip at all. */
@media (pointer: fine) {
  .board-card {
    content-visibility: auto;
    contain-intrinsic-size: auto 68px;
  }

  .board-card.drop-top,
  .board-card.drop-bottom {
    content-visibility: visible;
  }
}

.board-card.is-current {
  --card-bg: var(--bg-selected);
  box-shadow:
    inset 2px 0 0 var(--accent),
    var(--shadow-1);
}

/* Hover only where a pointer hovers: after a tap, touch screens would keep the last card "hovered". */
@media (hover: hover) {
  .board-card:hover {
    --card-tint: var(--bg-hover);
    box-shadow: var(--shadow-2);
  }

  .board-card.is-current:hover {
    box-shadow:
      inset 2px 0 0 var(--accent),
      var(--shadow-2);
  }
}

/* A tap shows that it landed while the finger is down. */
@media (hover: none) {
  .board-card:active:not(.is-renaming) {
    --card-tint: var(--bg-pressed);
  }
}

/* The source slot while it drags: a dashed placeholder at 40% (8.1), without its revealed menu
   (BoardCardMenu's root, which this scoped style reaches). */
.board-card.is-dragging {
  --card-tint: transparent;
  border-style: dashed;
  box-shadow: none;
  opacity: 0.4;
}

.board-card.is-dragging .corner {
  visibility: hidden;
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

/* Faint until the card is hovered or holds focus; a done card shows its check. The 24px target is
   pulled up and left, so the 16px circle sits on the title's first line and lines up with the
   tags and the meta row below. */
.toggle {
  flex: none;
  margin: -2px calc(-1 * var(--space-1)) -2px calc(-1 * var(--space-1));
  opacity: 0.5;
  transition: opacity var(--dur-1) var(--ease-out);
}

/* Keyboard focus reveals it, focus a mouse action moved here in code doesn't (X2, as base.css's
   .reveal): only the hovered card then shows its controls. */
.board-card:hover .toggle,
.board-card:focus-visible .toggle,
.board-card:has(:focus-visible) .toggle,
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

.is-done .title,
.is-context .title {
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

.kept {
  margin: 0;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
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

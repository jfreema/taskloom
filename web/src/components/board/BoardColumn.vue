<script setup lang="ts">
// One Board column (8.8): a section named "<status>, N tasks", 280px on --bg-column.
// - The header stays at the top while the cards scroll under it; the column scrolls on its own and
//   auto-scrolls near its edges during a drag.
// - The add card opens at the top (header "+") or the bottom ("+ Add task", which stays in view at
//   the bottom of a long column).
// - The first 20 (Done) or 50 cards render, then "Show all N". Many cards at once mount in batches
//   (useMountedCards), the next once the column has rendered the one before; "Show all N" waits
//   until they all have.
// - An empty column shows a dashed "No tasks" area.
// - Drops: the whole column appends; the header puts the card at the top; cards take the edge the
//   pointer is closest to. The column gets a --bg-hover tint while it is the target.
import { computed, nextTick, onMounted, onUpdated, ref, watch, type ComponentPublicInstance } from 'vue'
import { Plus } from '@lucide/vue'
import { activeDrag, useAutoScroll, useDropTarget, type DragData } from '../../composables/useDnd'
import { pluralize } from '../../lib/format'
import type { CreateTaskRequest } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import { toElement } from '../ui/popover'
import BoardCard from './BoardCard.vue'
import BoardColumnHeader from './BoardColumnHeader.vue'
import BoardInlineAdd from './BoardInlineAdd.vue'
import { useBoard, type DropAt } from './boardContext'
import type { BoardColumnView } from './useBoardModel'
import { useMountedCards } from './useMountedCards'

const props = defineProps<{
  column: BoardColumnView
  /** The inherited filter values for new tasks. */
  defaults: Partial<CreateTaskRequest>
  /** Opens the add card at the bottom (BoardView, for "New list"'s first task). */
  addRequest?: number
}>()

const board = useBoard()

const sectionEl = ref<HTMLElement | null>(null)
const bodyEl = ref<HTMLElement | null>(null)
const footerButton = ref<HTMLButtonElement | null>(null)
const header = ref<ComponentPublicInstance | null>(null)
const headerEl = computed(() => toElement(header.value))
const addAt = ref<'top' | 'bottom' | null>(null)

const statusId = computed(() => props.column.status.id)
const count = computed(() => props.column.tasks.length)
const label = computed(() => `${props.column.status.name}, ${pluralize(count.value, 'task')}`)
const shown = computed(() => props.column.shown)
const cards = useMountedCards(
  () => shown.value.length,
  () => board.model.columns.value.reduce((n, c) => n + (c.collapsed ? 0 : 1), 0),
)
/** The cards rendered so far (progressive mounting). */
const mounted = computed(() => (cards.count.value >= shown.value.length ? shown.value : shown.value.slice(0, cards.count.value)))
onMounted(cards.rendered)
onUpdated(cards.rendered)
const dragActive = computed(() => activeDrag.value !== null)
const showFooter = computed(() => board.canEdit.value && addAt.value !== 'bottom')

useAutoScroll(bodyEl)

const accepts = (d: DragData) => board.canDrag.value && d.listId === board.listId.value
const { over } = useDropTarget(sectionEl, {
  data: () => ({ type: 'column', statusId: statusId.value }),
  accepts,
  onDrop: (d) => board.drop(d, statusId.value, 'end'),
})
const { over: overHeader } = useDropTarget(headerEl, {
  data: () => ({ type: 'column-start', statusId: statusId.value }),
  accepts,
  onDrop: (d) => board.drop(d, statusId.value, 'start'),
})

/** Where a drop on the column itself (not on a card) lands. */
const target = computed<'start' | 'end' | null>(() => {
  if (!over.value) return null
  if (overHeader.value) return 'start'
  return board.hover.value?.statusId === statusId.value ? null : 'end'
})

function shows(at: DropAt): boolean {
  const d = activeDrag.value
  return !!d && board.resolveDrop(d, statusId.value, at) !== null
}

const startLine = computed(() => target.value === 'start' && shows('start'))
const endLine = computed(() => target.value === 'end' && shows('end'))
const firstId = computed(() => shown.value[0]?.id ?? null)
const lastId = computed(() => shown.value[shown.value.length - 1]?.id ?? null)

function hintFor(id: string): 'top' | 'bottom' | null {
  if (startLine.value && id === firstId.value) return 'top'
  if (endLine.value && id === lastId.value) return 'bottom'
  return null
}

// ---- Adding ----

function openTop(): void {
  addAt.value = 'top'
  void nextTick(() => bodyEl.value?.scrollTo?.({ top: 0 }))
}

function openBottom(): void {
  addAt.value = 'bottom'
}

function closeAdd(refocus: boolean): void {
  const where = addAt.value
  addAt.value = null
  if (!refocus) return
  void nextTick(() => {
    const el = where === 'top' ? headerEl.value?.querySelector<HTMLElement>('[data-add-top]') : footerButton.value
    el?.focus({ preventScroll: true })
  })
}

watch(
  () => props.addRequest,
  (n) => {
    if (n) openBottom()
  },
)

function collapse(): void {
  board.model.setCollapsed(statusId.value, true)
}

defineExpose({ openTop })
</script>

<template>
  <section
    ref="sectionEl"
    class="board-column"
    :class="{ 'is-over': over && dragActive }"
    :aria-label="label"
    :aria-busy="mounted.length < shown.length ? 'true' : undefined"
    :data-column="column.status.id"
  >
    <BoardColumnHeader ref="header" :status="column.status" :count="count" @add="openTop" @collapse="collapse" />

    <div ref="bodyEl" class="body" :class="{ 'has-footer': showFooter }">
      <BoardInlineAdd
        v-if="addAt === 'top'"
        :status-id="column.status.id"
        placement="Top"
        :defaults="defaults"
        @close="closeAdd"
      />

      <div v-if="!shown.length && addAt === null" class="empty" :class="{ 'is-target': target !== null }">No tasks</div>

      <BoardCard v-for="t in mounted" :key="t.id" :task="t" :column="column.status" :drop-hint="hintFor(t.id)" />

      <button
        v-if="column.capped && mounted.length === shown.length"
        type="button"
        class="show-all"
        @click="board.model.showAll(column.status.id)"
      >
        Show all <span class="tabular">{{ count }}</span>
      </button>

      <BoardInlineAdd
        v-if="addAt === 'bottom'"
        :status-id="column.status.id"
        placement="Bottom"
        :defaults="defaults"
        @close="closeAdd"
      />

      <div v-if="showFooter" class="footer">
        <button ref="footerButton" type="button" class="add-task" @click="openBottom">
          <UiIcon :icon="Plus" :size="14" />
          <span>Add task</span>
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.board-column {
  display: flex;
  flex: none;
  flex-direction: column;
  width: 280px;
  max-height: 100%;
  min-height: 0;
  border-radius: var(--radius-lg);
  background: var(--bg-column);
  transition: background-color var(--dur-1) var(--ease-out);
}

/* The drop target column (8.8). */
.board-column.is-over {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
}

.body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  gap: var(--space-2);
  min-height: 0;
  padding: 2px var(--space-2) var(--space-2);
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  scroll-padding: var(--space-2) 0 calc(var(--space-2) + 36px);
}

/* The footer ends the scroll content and holds the bottom padding itself, so that, sticking to the
   bottom of the scrollport, nothing shows through beneath it. */
.body.has-footer {
  padding-bottom: 0;
}

.empty {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: center;
  min-height: 64px;
  border: 1px dashed var(--border-default);
  border-radius: var(--radius-md);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  transition: border-color var(--dur-1) var(--ease-out);
}

.empty.is-target {
  border-color: var(--accent);
  color: var(--text-secondary);
}

.show-all {
  display: flex;
  flex: none;
  align-items: center;
  justify-content: center;
  gap: 0.3em;
  height: var(--control-sm);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--accent-text);
  font: inherit;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  text-align: center;
  cursor: pointer;
  transition: background-color var(--dur-1) var(--ease-out);
}

.show-all:hover {
  background: var(--bg-hover);
}

/* "+ Add task" stays at the bottom of a long column, over the cards that scroll under it. */
.footer {
  position: sticky;
  bottom: 0;
  z-index: 1;
  flex: none;
  margin: 0 calc(-1 * var(--space-2));
  padding: 0 var(--space-2) var(--space-2);
  background: var(--bg-column);
}

.board-column.is-over .footer {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
}

.add-task {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  width: 100%;
  height: var(--control-sm);
  padding: 0 var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.add-task:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

/* Touch (7.5): a 44px target. The button itself grows (not an ::after), so the sticky footer's
   background covers exactly the area that takes the tap. */
@media (pointer: coarse) {
  .add-task {
    height: var(--touch-min);
  }
}
</style>

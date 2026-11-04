<script setup lang="ts">
// Board view (8.8): a region "Board" that scrolls sideways (16px padding, 12px between columns),
// with one column per status in display order and "+ Add status" after the last one (Member+).
// - The view scrolls itself (`data-own-scroll`); each column scrolls its cards on its own.
// - The "Show completed tasks" toggle is teleported into the toolbar's end.
// - States: a skeleton after 200ms of a first load; "Filters are hiding N tasks" above the columns
//   while filters hide tasks; empty columns say so. A failed first load is ListPage's error state.
// - Phones: columns are 85vw wide and snap; dragging is off and the card menu moves cards.
// - Drops go through tasks.move with the visible neighbours of the drop point (useBoardDrop); a
//   drop that would leave the card where it is sends nothing.
// - Focus: the panel's J/K order follows the cards; the open task's card comes into view; a card
//   that leaves while it holds focus hands it to its neighbour (useBoardFocus).
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import BoardAddStatus from '../../components/board/BoardAddStatus.vue'
import BoardCollapsedColumn from '../../components/board/BoardCollapsedColumn.vue'
import BoardColumn from '../../components/board/BoardColumn.vue'
import BoardPickers from '../../components/board/BoardPickers.vue'
import BoardSkeleton from '../../components/board/BoardSkeleton.vue'
import BoardToolbar from '../../components/board/BoardToolbar.vue'
import { provideBoard, type CardHover, type PickTarget } from '../../components/board/boardContext'
import { useBoardDrop } from '../../components/board/useBoardDrop'
import { useBoardFocus } from '../../components/board/useBoardFocus'
import { cardEl, focusEl, useBoardKeyboard } from '../../components/board/useBoardKeyboard'
import { useBoardModel } from '../../components/board/useBoardModel'
import FilterHiddenNote from '../../components/view/FilterHiddenNote.vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { activeDrag, useAutoScroll } from '../../composables/useDnd'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useListPageContext } from '../../composables/useListPageContext'
import { useTaskActions, type PickField } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { emptyFilters } from '../../lib/taskQuery'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import type { Uuid } from '../../types'

const ctx = useListPageContext()
const tasks = useTasks()
const panel = useTaskPanel()
const actions = useTaskActions()
const ui = useUi()
const { isPhone } = useBreakpoint()

const model = useBoardModel()
const boardEl = ref<HTMLElement | null>(null)
useAutoScroll(boardEl)

const canEdit = computed(() => ctx.canEdit.value)
const canDrag = computed(() => canEdit.value && !isPhone.value)
const loading = computed(() => ctx.loading.value)
const showSkeleton = useDelayedFlag(loading)

// ---- Drag and drop ----

const hover = shallowRef<CardHover | null>(null)
watch(activeDrag, (d) => {
  if (!d) hover.value = null
})

const { resolveDrop, drop } = useBoardDrop(model, ctx.listId)

// ---- Rename, flash ----

const renamingId = ref<Uuid | null>(null)
const flashIds = shallowRef<ReadonlySet<Uuid>>(new Set())
const timers = new Set<ReturnType<typeof setTimeout>>()

function startRename(id: Uuid): void {
  if (!canEdit.value || tasks.isPendingDelete(id)) return
  renamingId.value = id
}

function endRename(id: Uuid): void {
  if (renamingId.value === id) renamingId.value = null
  void nextTick(() => {
    const active = document.activeElement
    if (!active || active === document.body || !active.isConnected) focusEl(cardEl(boardEl.value, id))
  })
}

function created(id: Uuid): void {
  flashIds.value = new Set([...flashIds.value, id])
  const timer = setTimeout(() => {
    timers.delete(timer)
    const rest = new Set(flashIds.value)
    rest.delete(id)
    flashIds.value = rest
  }, 1000)
  timers.add(timer)
}

onBeforeUnmount(() => {
  for (const t of timers) clearTimeout(t)
})

// ---- Pickers ----

const pickTarget = shallowRef<PickTarget | null>(null)
const pickOpen = ref(false)

watch(pickOpen, (on) => {
  if (!on) pickTarget.value = null
})

function pick(id: Uuid, field: PickField): void {
  const anchor = cardEl(boardEl.value, id)
  if (!anchor || !canEdit.value) return
  pickTarget.value = { id, field, anchor }
  pickOpen.value = true
}

provideBoard({
  listId: ctx.listId,
  canEdit,
  canDrag,
  model,
  openId: computed(() => panel.openId.value),
  keptIds: computed(() => ctx.result.value.keptIds),
  contextIds: computed(() => ctx.result.value.contextIds),
  renamingId,
  flashIds,
  hover,
  openTask: (id) => panel.open(id),
  pick,
  startRename,
  endRename,
  rename: (id, title) => void actions.rename(id, title),
  created,
  resolveDrop,
  drop,
})

// ---- Focus ----

const { refocus, onFocusIn, onFocusOut } = useBoardFocus(boardEl, model)
useBoardKeyboard({ root: boardEl, model, canEdit, pick, startRename, refocus })

// ---- The panel ----

watch(() => model.order.value, (ids) => panel.setOrder(ids), { immediate: true })

// The open task's card comes into view (J/K in the panel, the palette), without taking focus.
watch(
  () => panel.openId.value,
  (id) => {
    if (!id) return
    void nextTick(() => cardEl(boardEl.value, id)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' }))
  },
  { immediate: true },
)

// "New list" asks for the first task's add card (ui.inlineAddRequest).
const addRequest = ref(0)
watch(
  () => ui.inlineAddRequest,
  (r) => {
    if (!r || r.listId !== ctx.listId.value || !canEdit.value) return
    ui.inlineAddRequest = null
    addRequest.value = r.at
  },
  { immediate: true },
)
const firstOpenKey = computed(() => model.columns.value.find((c) => !c.collapsed)?.key ?? null)

function clearFilters(): void {
  ctx.filters.value = emptyFilters()
  ctx.search.value = ''
}
</script>

<template>
  <div class="board-view" :class="{ 'is-phone': isPhone }" :aria-busy="loading ? 'true' : undefined" data-own-scroll>
    <Teleport defer :to="'#' + ctx.toolbarEndId">
      <BoardToolbar />
    </Teleport>

    <template v-if="loading">
      <BoardSkeleton v-if="showSkeleton" />
    </template>
    <template v-else>
      <FilterHiddenNote class="hidden-note" :hidden-count="ctx.result.value.hiddenCount" @clear="clearFilters" />
      <div
        ref="boardEl"
        role="region"
        aria-label="Board"
        class="board"
        data-shortcut-scope="grid"
        @focusin="onFocusIn"
        @focusout="onFocusOut"
      >
        <template v-for="col in model.columns.value" :key="col.key">
          <BoardCollapsedColumn v-if="col.collapsed" :column="col" />
          <BoardColumn
            v-else
            :column="col"
            :defaults="ctx.createDefaults.value"
            :add-request="col.key === firstOpenKey ? addRequest : 0"
          />
        </template>
        <BoardAddStatus v-if="canEdit" />
      </div>
      <BoardPickers v-model:open="pickOpen" :target="pickTarget" />
    </template>
  </div>
</template>

<style scoped>
.board-view {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.hidden-note {
  flex: none;
  padding-bottom: 0;
}

.board {
  display: flex;
  flex: 1 1 auto;
  align-items: flex-start;
  gap: var(--space-3);
  min-width: 0;
  min-height: 0;
  padding: var(--space-4);
  overflow-x: auto;
  overflow-y: hidden;
  overscroll-behavior-x: contain;
  scroll-padding-inline: var(--space-4);
}

/* The inline end padding of a scroller isn't always scrollable: this keeps 16px after the last column. */
.board::after {
  content: '';
  flex: none;
  width: 1px;
  height: 1px;
  margin-left: calc(var(--space-4) - var(--space-3) - 1px);
}

/* Phones: 85vw columns that snap, and room for the FAB under the cards (each column scrolls). */
.is-phone .board {
  scroll-snap-type: x mandatory;
}

.is-phone .board > :deep(.board-column),
.is-phone .board > :deep(.collapsed-column),
.is-phone .board > :deep(.add-status) {
  scroll-snap-align: start;
}

.is-phone .board > :deep(.board-column),
.is-phone .board > :deep(.add-status) {
  width: 85vw;
}

.is-phone .board :deep(.board-column .body) {
  padding-bottom: calc(var(--space-2) + var(--fab-room, 0px));
}

.is-phone .board :deep(.board-column .footer) {
  position: static;
  margin: 0;
  padding: 0;
  background: transparent;
}
</style>

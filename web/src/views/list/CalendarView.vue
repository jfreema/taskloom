<script setup lang="ts">
// Calendar view (spec 8.9): one list, month only. It scrolls itself (`data-own-scroll`) and ends
// its scroll content with the phone FAB's room.
// - The header (CalendarHeader), then the month grid (CalendarGrid) with the To schedule tray
//   (CalendarTray) on the right. The tray follows ViewConfig.showToSchedule (default on); below
//   900px of page width it is closed by default and opens over the grid's right edge instead
//   (non-modal, Esc closes it). With no dated task and something to schedule it opens by itself
//   until it is toggled.
// - Below 640px of page width (phones and narrow docked layouts) the agenda replaces the grid.
// - "Show completed tasks" is teleported into the toolbar's end.
// - States: a grid skeleton after 200ms of a first load (a failed first load is ListPage's error
//   state); an empty month is the empty grid.
// A click on a chip opens the task panel (focus stays put for a mouse click while the docked panel
// is open, 8.10); J and K follow the month's order, then the tray's.
import { computed, onBeforeUnmount, onMounted, ref, watchEffect } from 'vue'
import CalendarAgenda from '../../components/calendar/CalendarAgenda.vue'
import CalendarGrid from '../../components/calendar/CalendarGrid.vue'
import CalendarHeader from '../../components/calendar/CalendarHeader.vue'
import CalendarSkeleton from '../../components/calendar/CalendarSkeleton.vue'
import CalendarToolbar from '../../components/calendar/CalendarToolbar.vue'
import CalendarTray from '../../components/calendar/CalendarTray.vue'
import { useCalendarModel } from '../../components/calendar/useCalendarModel'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useAutoScroll } from '../../composables/useDnd'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useListPageContext } from '../../composables/useListPageContext'
import { useTaskPanel } from '../../composables/useTaskPanel'
import type { Uuid } from '../../types'

const AGENDA_BELOW = 640
const OVERLAY_BELOW = 900

const ctx = useListPageContext()
const model = useCalendarModel()
const panel = useTaskPanel()
const { width: viewportWidth } = useBreakpoint()

const root = ref<HTMLElement | null>(null)
// The page's width decides the layout; the viewport is the first guess, before the first measure.
const width = ref<number>(viewportWidth.value)
let observer: ResizeObserver | null = null

onMounted(() => {
  const el = root.value
  if (!el) return
  width.value = el.getBoundingClientRect().width || width.value
  if (typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(([entry]) => {
    if (entry) width.value = entry.borderBoxSize?.[0]?.inlineSize ?? entry.contentRect.width
  })
  observer.observe(el)
})
onBeforeUnmount(() => observer?.disconnect())

useAutoScroll(root)

const agenda = computed(() => width.value < AGENDA_BELOW)
const narrow = computed(() => width.value < OVERLAY_BELOW)

const loading = computed(() => model.loading.value)
const showSkeleton = useDelayedFlag(loading)

// ---- The tray ----

const overlayOpen = ref(false)
const toggled = ref(false)
const autoOpen = computed(() => !toggled.value && !model.hasDated.value && model.trayCount.value > 0)
const trayOpen = computed(() =>
  narrow.value ? overlayOpen.value || autoOpen.value : ctx.config.value.showToSchedule || autoOpen.value,
)

function setTray(open: boolean): void {
  toggled.value = true
  if (narrow.value) overlayOpen.value = open
  else ctx.config.value.showToSchedule = open
}

function toggleTray(): void {
  setTray(!trayOpen.value)
}

function closeTray(): void {
  const inside = !!document.activeElement?.closest('.tray')
  setTray(false)
  if (inside) root.value?.querySelector<HTMLElement>('.tray-toggle')?.focus()
}

// ---- Opening tasks ----

function openTask(id: Uuid, e: MouseEvent | KeyboardEvent): void {
  const keyboard = e instanceof KeyboardEvent || e.detail === 0
  const focus = keyboard || !(panel.mode.value === 'docked' && panel.isOpen.value)
  model.open(id, { focus })
}

// J and K in the panel: the month's tasks day by day, then the tray's.
watchEffect(() => {
  const ids: Uuid[] = []
  for (const day of model.weeks.value.flat()) {
    if (!model.inMonth(day)) continue
    for (const t of model.byDay.value.get(day) ?? []) ids.push(t.id)
  }
  for (const t of model.noDue.value) ids.push(t.id)
  panel.setOrder(ids)
})
</script>

<template>
  <div
    ref="root"
    class="calendar-view"
    :class="{ 'is-agenda': agenda, 'is-narrow': narrow }"
    :aria-busy="loading ? 'true' : undefined"
    data-own-scroll
  >
    <Teleport defer :to="'#' + ctx.toolbarEndId">
      <CalendarToolbar />
    </Teleport>

    <template v-if="loading">
      <CalendarSkeleton v-if="showSkeleton" :agenda="agenda" />
    </template>
    <template v-else>
      <CalendarHeader :model="model" :tray-open="agenda ? null : trayOpen" :compact="narrow" @toggle-tray="toggleTray" />
      <CalendarAgenda v-if="agenda" :model="model" @open="openTask" />
      <div v-else class="body">
        <CalendarGrid class="month" :model="model" @open="openTask" />
        <CalendarTray v-if="trayOpen" class="tray" :model="model" :overlay="narrow" @close="closeTray" @open="openTask" />
      </div>
    </template>

    <div class="end-room" aria-hidden="true" />
  </div>
</template>

<style scoped>
.calendar-view {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  padding: 0 var(--space-4);
  overflow: auto;
  overscroll-behavior: contain;
  scroll-padding-top: 40px;
  scroll-padding-bottom: var(--fab-room, 0px);
}

.body {
  position: relative;
  display: flex;
  align-items: stretch;
  gap: var(--space-3);
  flex: 1 0 auto;
  min-height: 0;
  padding-bottom: var(--space-4);
}

.month {
  flex: 1 1 auto;
}

.tray {
  flex: none;
}

/* Below 900px: the tray floats over the grid's right edge. */
.is-narrow .tray {
  position: absolute;
  top: 0;
  right: 0;
  bottom: var(--space-4);
  z-index: var(--z-sticky);
  max-width: calc(100% - var(--space-8));
}

.end-room {
  flex: none;
  height: var(--fab-room, 0px);
}
</style>

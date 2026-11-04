<script setup lang="ts">
// Home, "My Work" (8.5): the greeting, the scope control, quick add, then my tasks in sections
// computed from the tasks store (components/home). Rows are one keyboard grid with the panel
// following their order; Reschedule opens one shared date picker anchored to the row. States:
// loading skeleton, error, first run, no list, and "You're all caught up."
import { computed, nextTick, ref, watch } from 'vue'
import { CircleCheck, House, Keyboard, List as ListIcon, Plus } from '@lucide/vue'
import DateField from '../components/fields/DateField.vue'
import HomeHeader from '../components/home/HomeHeader.vue'
import HomeQuickAdd from '../components/home/HomeQuickAdd.vue'
import HomeSection from '../components/home/HomeSection.vue'
import HomeSkeleton from '../components/home/HomeSkeleton.vue'
import HomeTaskRow from '../components/home/HomeTaskRow.vue'
import { HOME_SECTIONS, HOME_SECTION_LABEL, type HomeSectionKey } from '../components/home/homeSections'
import { useHomeData } from '../components/home/useHomeData'
import { useHomeKeyboard } from '../components/home/useHomeKeyboard'
import PageHeader from '../components/shell/PageHeader.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import { useBreakpoint } from '../composables/useBreakpoint'
import { useDelayedFlag } from '../composables/useDelayedFlag'
import { useTaskPanel } from '../composables/useTaskPanel'
import { useToday } from '../composables/useToday'
import { formatDateMedium } from '../lib/dates'
import { pluralize } from '../lib/format'
import { useTasks } from '../stores/tasks'
import { useUi } from '../stores/ui'
import { useWorkspaces } from '../stores/workspaces'
import type { IsoDate, Task, TaskPatch, Uuid } from '../types'

/** Rows a section renders before "Show N more" (8.1). */
const PAGE = 100

const tasks = useTasks()
const workspaces = useWorkspaces()
const ui = useUi()
const panel = useTaskPanel()
const today = useToday()
const { isPhone } = useBreakpoint()

const data = useHomeData()
const { sections, collapsed, scope, ready } = data

const canEdit = computed(() => workspaces.canEdit())
const firstList = computed(() => workspaces.allLists[0] ?? null)
const skeleton = useDelayedFlag(data.loading, 200)

const firstRun = computed(() => ready.value && !data.hasAnyTask.value)
const caughtUp = computed(() => ready.value && data.hasAnyTask.value && data.openCount.value === 0 && sections.value.completed.length > 0)

// ---- Sections and rows ----

const limits = ref<Partial<Record<HomeSectionKey, number>>>({})
watch(() => workspaces.currentId, () => {
  limits.value = {}
})

function limitOf(key: HomeSectionKey): number {
  return limits.value[key] ?? PAGE
}

/** Empty sections are hidden, except Today (unless the page is caught up). */
const visibleKeys = computed(() =>
  firstRun.value
    ? []
    : HOME_SECTIONS.filter((key) => sections.value[key].length > 0 || (key === 'today' && !caughtUp.value)),
)

function shown(key: HomeSectionKey): Task[] {
  const rows = sections.value[key]
  return rows.length > limitOf(key) ? rows.slice(0, limitOf(key)) : rows
}

function showMore(key: HomeSectionKey): void {
  limits.value = { ...limits.value, [key]: limitOf(key) + PAGE }
}

/** The rendered rows in order (collapsed sections render none): the grid and the panel follow it. */
const order = computed<Uuid[]>(() => visibleKeys.value.flatMap((key) => (collapsed.value[key] ? [] : shown(key).map((t) => t.id))))

const sectionsEl = ref<HTMLElement | null>(null)

function rowEl(id: Uuid): HTMLElement | null {
  return sectionsEl.value?.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(id)}"]`) ?? null
}

const { tabStopId } = useHomeKeyboard({
  container: sectionsEl,
  order,
  canEdit: () => canEdit.value,
  reschedule: (id, anchor) => openReschedule(id, anchor),
})

/** A click opens the panel and focuses it, except a mouse click while the docked panel is open (8.10). */
function openRow(id: Uuid): void {
  panel.open(id, { focus: !(panel.mode.value === 'docked' && panel.isOpen.value) })
}

// ---- Reschedule: one date picker, anchored to the row ----

const reschedule = ref<{ id: Uuid; anchor: HTMLElement } | null>(null)
const rescheduleOpen = ref(false)
const rescheduleTask = computed(() => (reschedule.value ? (tasks.get(reschedule.value.id) ?? null) : null))
/** The start date the picker moved along with the due date (it emits that first). */
let movedStart: IsoDate | null | undefined

function openReschedule(id: Uuid, anchor: HTMLElement): void {
  if (!canEdit.value || tasks.isPendingDelete(id) || !tasks.get(id)) return
  reschedule.value = { id, anchor }
  movedStart = undefined
  rescheduleOpen.value = true
}

function onOtherDate(v: IsoDate | null): void {
  movedStart = v
}

function applyReschedule(v: IsoDate | null): void {
  const id = reschedule.value?.id
  if (!id) return
  const patch: TaskPatch = { dueDate: v }
  if (movedStart !== undefined) patch.startDate = movedStart
  movedStart = undefined
  void tasks.update(id, patch, { undo: v ? `Rescheduled to ${formatDateMedium(v, today.value)}` : 'Removed the due date' })
}

// ---- Move all to today ----

const overdueOpen = computed(() => sections.value.overdue.filter((t) => !tasks.isDone(t)))

function moveAllToToday(): void {
  const list = overdueOpen.value
  if (!list.length || !canEdit.value) return
  void tasks.bulkUpdate(
    list.map((t) => ({ id: t.id, dueDate: today.value })),
    { undo: `Rescheduled ${pluralize(list.length, 'task')} to today` },
  )
  // The Overdue header (and this button) are gone: focus goes to Today's header.
  void nextTick(() => {
    const a = document.activeElement
    if (!a || a === document.body || !a.isConnected) {
      sectionsEl.value?.querySelector<HTMLElement>('[data-section="today"] [data-section-toggle]')?.focus({ preventScroll: true })
    }
  })
}

// ---- Quick add ----

const flashId = ref<Uuid | null>(null)
let flashTimer: ReturnType<typeof setTimeout> | undefined

/** The new row lands in Today: open it, flash the row and bring it into view without moving focus. */
function onCreated(id: Uuid): void {
  data.expand('today')
  flashId.value = id
  clearTimeout(flashTimer)
  flashTimer = setTimeout(() => {
    if (flashId.value === id) flashId.value = null
  }, 900)
  void nextTick(() => rowEl(id)?.scrollIntoView?.({ block: 'nearest' }))
}
</script>

<template>
  <PageHeader :bare="!isPhone" title="Home" title-tag="p" />
  <div class="home" :aria-busy="data.loading.value ? 'true' : undefined">
    <HomeHeader
      v-model:scope="scope"
      :due-today="data.dueTodayCount.value"
      :overdue="data.overdueCount.value"
      :pending="!ready"
      :failed="!!data.error.value"
      :skeleton="skeleton"
      :show-scope="ready && !firstRun"
    />

    <UiErrorState v-if="data.error.value && !ready" class="state" title="Couldn't load your tasks" :retry="() => data.load()" />

    <HomeSkeleton v-else-if="!ready" :show="skeleton" />

    <template v-else>
      <div v-if="canEdit" class="add">
        <HomeQuickAdd v-if="firstList" :assign-to-me="scope === 'assigned'" :hint="firstRun" @created="onCreated" />
        <UiButton v-else variant="primary" :icon="Plus" @click="ui.openQuickCreate()">Create a list</UiButton>
      </div>

      <UiEmptyState
        v-if="firstRun"
        class="first-run"
        :icon="House"
        title="Your work shows up here"
        description="Tasks assigned to you or created by you, from every list, show up here."
      >
        <template #actions>
          <UiButton v-if="firstList" :icon="ListIcon" :to="`/l/${firstList.list.id}`">Open {{ firstList.list.name }}</UiButton>
          <UiButton :icon="Keyboard" @click="ui.openShortcuts()">Keyboard shortcuts</UiButton>
        </template>
      </UiEmptyState>

      <template v-else>
        <UiEmptyState v-if="caughtUp" class="caught-up" size="sm" :icon="CircleCheck" title="You're all caught up." />
        <div ref="sectionsEl" class="sections">
          <HomeSection
            v-for="key in visibleKeys"
            :key="key"
            :section-key="key"
            :label="HOME_SECTION_LABEL[key]"
            :count="sections[key].length"
            :collapsed="collapsed[key]"
            :hidden-count="sections[key].length - shown(key).length"
            :empty-text="key === 'today' ? 'Nothing due today' : undefined"
            @toggle="data.toggleSection(key)"
            @more="showMore(key)"
          >
            <template v-if="key === 'overdue' && canEdit && overdueOpen.length > 0" #actions>
              <UiButton class="reveal reveal-touch" variant="ghost" size="sm" @click="moveAllToToday">Move all to today</UiButton>
            </template>
            <HomeTaskRow
              v-for="t in shown(key)"
              :key="t.id"
              :task="t"
              :tab-stop="t.id === tabStopId"
              :current="t.id === panel.openId.value"
              :flash="t.id === flashId"
              :can-edit="canEdit"
              @open="openRow(t.id)"
              @reschedule="(anchor) => openReschedule(t.id, anchor)"
            />
          </HomeSection>
        </div>
      </template>
    </template>
  </div>

  <DateField
    v-if="canEdit && rescheduleTask"
    v-model:open="rescheduleOpen"
    :model-value="rescheduleTask.dueDate"
    kind="due"
    :other-date="rescheduleTask.startDate"
    :anchor="reschedule?.anchor ?? null"
    hide-trigger
    @update:other-date="onOtherDate"
    @update:model-value="applyReschedule"
  />
</template>

<style scoped>
.home {
  width: 100%;
  max-width: calc(960px + 2 * var(--space-6));
  margin: 0 auto;
  padding: var(--space-6) var(--space-6) var(--space-12);
}

.add {
  margin-top: var(--space-5);
}

.state {
  margin: var(--space-12) auto 0;
}

.first-run {
  margin-top: var(--space-10);
}

.caught-up {
  margin-top: var(--space-8);
}

.sections {
  margin-top: var(--space-6);
}

@container page (max-width: 639px) {
  .home {
    padding: var(--space-4) var(--space-4) var(--space-8);
  }

  .add {
    margin-top: var(--space-4);
  }

  .sections {
    margin-top: var(--space-5);
  }
}
</style>

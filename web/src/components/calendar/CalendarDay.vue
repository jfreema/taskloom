<script setup lang="ts">
// One day of the month grid (spec 8.9): a focusable gridcell named "Wednesday, November 4,
// 3 tasks". The day number sits top-left (today is a 22px accent disc; the first of a month adds
// the month's short name). Hovering or focusing the day reveals "+" (Member+), which opens quick
// create on that day. Up to 3 chips, then "+N more" (CalendarMore). The whole cell is a drop target
// for task chips of this list from any other day or from the tray.
import { computed, ref } from 'vue'
import { Plus } from '@lucide/vue'
import { useDropTarget } from '../../composables/useDnd'
import { formatDateLong, formatDateShort, fromIso } from '../../lib/dates'
import type { IsoDate, Task, Uuid } from '../../types'
import UiIconButton from '../ui/UiIconButton.vue'
import CalendarChip from './CalendarChip.vue'
import CalendarMore from './CalendarMore.vue'
import { taskCount, type CalendarModel } from './useCalendarModel'

const MAX_CHIPS = 3

const props = defineProps<{
  model: CalendarModel
  day: IsoDate
  tasks: Task[]
  /** The grid's roving tab stop. */
  tabStop: boolean
}>()

const emit = defineEmits<{ open: [id: Uuid, e: MouseEvent | KeyboardEvent] }>()

const root = ref<HTMLElement | null>(null)

const today = computed(() => props.model.today.value)
const isToday = computed(() => props.day === today.value)
const outside = computed(() => !props.model.inMonth(props.day))
const weekend = computed(() => {
  const d = fromIso(props.day).getDay()
  return d === 0 || d === 6
})
const date = computed(() => fromIso(props.day))
const dayNumber = computed(() => date.value.getDate())
/** The first of a month reads "Dec 1" (formatDateShort against its own year: never a year). */
const firstLabel = computed(() => (dayNumber.value === 1 ? formatDateShort(props.day, props.day) : ''))

const shown = computed(() => (props.tasks.length > MAX_CHIPS ? props.tasks.slice(0, MAX_CHIPS) : props.tasks))
const rest = computed(() => props.tasks.length - shown.value.length)

const label = computed(() => `${formatDateLong(props.day)}, ${taskCount(props.tasks.length)}`)
const addLabel = computed(() => `Add task on ${formatDateShort(props.day, today.value)}`)
const canEdit = computed(() => props.model.canEdit.value)

const { over } = useDropTarget(root, {
  data: () => ({ day: props.day }),
  accepts: (d) => d.listId === props.model.listId.value && d.fromDate !== props.day,
  onDrop: (d) => props.model.reschedule(d.id, props.day),
})

function add(): void {
  props.model.createOn(props.day)
}
</script>

<template>
  <div
    ref="root"
    role="gridcell"
    class="day"
    :class="{ 'is-outside': outside, 'is-weekend': weekend, 'is-today': isToday, 'is-over': over }"
    :data-day="day"
    :tabindex="tabStop ? 0 : -1"
    :aria-label="label"
    :aria-current="isToday ? 'date' : undefined"
  >
    <div class="head">
      <span class="date" aria-hidden="true">
        <span class="num tabular" :class="{ 'is-first': firstLabel }">{{ firstLabel || dayNumber }}</span>
      </span>
      <UiIconButton
        v-if="canEdit"
        class="add"
        :icon="Plus"
        :label="addLabel"
        kbd="N"
        size="xs"
        :tabindex="tabStop ? 0 : -1"
        data-no-drag
        @click="add"
      />
    </div>
    <div class="chips">
      <CalendarChip
        v-for="t in shown"
        :key="t.id"
        :task="t"
        :status="model.status(t)"
        :done="model.isDone(t)"
        :parent-title="model.parentTitle(t)"
        :current="model.openId.value === t.id"
        :can-drag="canEdit"
        :data-task-row="t.id"
        @activate="emit('open', t.id, $event)"
      />
      <CalendarMore
        v-if="rest > 0"
        :model="model"
        :day="day"
        :tasks="tasks"
        :count="rest"
        @open="(id, e) => emit('open', id, e)"
      />
    </div>
  </div>
</template>

<style scoped>
.day {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  min-width: 0;
  min-height: 112px;
  padding: var(--space-1) var(--space-1-5) var(--space-1-5);
  background: var(--bg-surface);
  outline: none;
  transition: background-color var(--dur-1) var(--ease-out);
}

.day.is-weekend,
.day.is-outside {
  background: var(--bg-subtle);
}

.day:focus-visible {
  box-shadow: inset 0 0 0 2px var(--focus-ring);
}

.day.is-over {
  background: var(--bg-selected);
  box-shadow: inset 0 0 0 2px var(--accent);
}

.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-1);
  height: 24px;
  flex-shrink: 0;
}

.date {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  padding-left: var(--space-0-5);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.is-outside .date {
  color: var(--text-tertiary);
}

.num {
  display: inline-grid;
  place-items: center;
  min-width: 22px;
  height: 22px;
  margin-left: -5px;
  border-radius: var(--radius-full);
}

.is-today .num {
  background: var(--accent);
  color: var(--accent-fg);
  font-weight: var(--fw-semibold);
}

.num.is-first {
  padding: 0 var(--space-1-5);
  white-space: nowrap;
}

.is-today .num.is-first {
  margin-left: 0;
}

/* "+": revealed on hover and focus. */
.add {
  opacity: 0;
  transition: opacity var(--dur-1) var(--ease-out);
}

.day:hover .add,
.day:focus-within .add,
.add:focus-visible {
  opacity: 1;
}

@media (hover: none) {
  .add {
    visibility: hidden;
  }

  .day:focus-within .add {
    visibility: visible;
  }
}

.chips {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  min-width: 0;
}
</style>


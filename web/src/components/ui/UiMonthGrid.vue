<script setup lang="ts">
// Internal to UiDatePicker: the month header and the day grid (spec 7.10).
// - role="grid" labelled by the month title. Arrow keys move a day or a week, Home and End go to
//   the start and end of the week, PageUp and PageDown change the month (with Shift the year),
//   Enter or Space picks, T goes to today. Moving past the month shows the next one.
// - One day is tabbable (roving tabindex): the selected day, else today. It carries data-autofocus,
//   which UiPopover's default initial focus prefers.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import UiIconButton from './UiIconButton.vue'
import { useToday } from '../../composables/useToday'
import {
  addDaysIso,
  addMonthsIso,
  endOfWeekIso,
  formatDateLong,
  formatMonthTitle,
  monthGrid,
  startOfMonthIso,
  startOfWeekIso,
  weekdayNames,
} from '../../lib/dates'
import type { IsoDate } from '../../types'

const props = defineProps<{
  modelValue: IsoDate | null
  min?: IsoDate
  max?: IsoDate
  highlight?: IsoDate | null
}>()

const emit = defineEmits<{ pick: [value: IsoDate] }>()

const titleId = `${useId()}-title`
const today = useToday()
const grid = ref<HTMLElement | null>(null)

// ISO dates compare correctly as strings.
function inRange(d: IsoDate): boolean {
  return (!props.min || d >= props.min) && (!props.max || d <= props.max)
}

function clamp(d: IsoDate): IsoDate {
  if (props.min && d < props.min) return props.min
  if (props.max && d > props.max) return props.max
  return d
}

const focused = ref<IsoDate>(clamp(props.modelValue ?? today.value))
const month = ref<IsoDate>(startOfMonthIso(focused.value))

watch(
  () => props.modelValue,
  (v) => {
    if (!v) return
    focused.value = clamp(v)
    month.value = startOfMonthIso(focused.value)
  },
)

const weeks = computed(() => monthGrid(month.value))
const weekdays = weekdayNames()
const monthTitle = computed(() => formatMonthTitle(month.value))

function sameMonth(a: IsoDate, b: IsoDate): boolean {
  return a.slice(0, 7) === b.slice(0, 7)
}

/** "Friday, November 6" (11.1), with the year outside the current one. */
function dayLabel(d: IsoDate): string {
  const long = formatDateLong(d)
  return d.slice(0, 4) === today.value.slice(0, 4) ? long : `${long}, ${d.slice(0, 4)}`
}

function moveTo(d: IsoDate): void {
  focused.value = d
  if (!sameMonth(d, month.value)) month.value = startOfMonthIso(d)
  void nextTick(() => grid.value?.querySelector<HTMLElement>(`[data-date="${d}"]`)?.focus({ preventScroll: true }))
}

function showMonth(delta: number): void {
  month.value = addMonthsIso(month.value, delta)
  // The tabbable day follows to the same date in the new month (date-fns clamps it to the month's end).
  focused.value = addMonthsIso(focused.value, delta)
  if (!sameMonth(focused.value, month.value)) focused.value = month.value
}

function pick(d: IsoDate): void {
  if (inRange(d)) emit('pick', d)
}

function onKeydown(e: KeyboardEvent): void {
  if (e.altKey || e.ctrlKey || e.metaKey) return
  const d = focused.value
  const year = e.shiftKey ? 12 : 1
  const targets: Record<string, () => IsoDate> = {
    ArrowLeft: () => addDaysIso(d, -1),
    ArrowRight: () => addDaysIso(d, 1),
    ArrowUp: () => addDaysIso(d, -7),
    ArrowDown: () => addDaysIso(d, 7),
    Home: () => startOfWeekIso(d),
    End: () => endOfWeekIso(d),
    PageUp: () => addMonthsIso(d, -year),
    PageDown: () => addMonthsIso(d, year),
    t: () => today.value,
    T: () => today.value,
  }
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    pick(d)
    return
  }
  const target = targets[e.key]
  if (!target) return
  e.preventDefault()
  moveTo(target())
}
</script>

<template>
  <div class="ui-month-grid">
    <div class="head">
      <h2 :id="titleId" class="title" aria-live="polite">{{ monthTitle }}</h2>
      <UiIconButton :icon="ChevronLeft" label="Previous month" size="sm" @click="showMonth(-1)" />
      <UiIconButton :icon="ChevronRight" label="Next month" size="sm" @click="showMonth(1)" />
    </div>
    <div ref="grid" class="grid" role="grid" :aria-labelledby="titleId" @keydown="onKeydown">
      <div class="row" role="row">
        <div v-for="w in weekdays" :key="w.long" class="weekday" role="columnheader">
          <span class="sr-only">{{ w.long }}</span>
          <span aria-hidden="true">{{ w.narrow }}</span>
        </div>
      </div>
      <div v-for="week in weeks" :key="week[0]" class="row" role="row">
        <div
          v-for="d in week"
          :key="d"
          class="day"
          :class="{
            'is-outside': !sameMonth(d, month),
            'is-today': d === today,
            'is-selected': d === modelValue,
            'is-highlight': !!highlight && d === highlight && d !== modelValue,
            'is-disabled': !inRange(d),
          }"
          role="gridcell"
          :data-date="d"
          :data-autofocus="d === focused ? '' : undefined"
          :tabindex="d === focused ? 0 : -1"
          :aria-label="dayLabel(d)"
          :aria-selected="d === modelValue ? 'true' : 'false'"
          :aria-current="d === today ? 'date' : undefined"
          :aria-disabled="inRange(d) ? undefined : 'true'"
          @click="pick(d)"
          @focus="focused = d"
        >
          <span class="disc" aria-hidden="true">{{ Number(d.slice(8)) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.ui-month-grid {
  --day: 32px;
  padding: var(--space-2) var(--space-3) var(--space-3);
}

@media (pointer: coarse) {
  .ui-month-grid {
    --day: 36px;
  }
}

.head {
  display: flex;
  align-items: center;
  gap: var(--space-0-5);
  height: var(--control-md);
  margin-bottom: var(--space-1);
}

.title {
  flex: 1;
  min-width: 0;
  padding-left: var(--space-1);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.grid {
  display: flex;
  flex-direction: column;
}

.row {
  display: grid;
  grid-template-columns: repeat(7, var(--day));
}

.weekday {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 24px;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.day {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--day);
  height: var(--day);
  border-radius: var(--radius-full);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}

.day:focus-visible {
  outline-offset: -2px;
}

.disc {
  display: flex;
  align-items: center;
  justify-content: center;
  width: calc(var(--day) - 4px);
  height: calc(var(--day) - 4px);
  border-radius: var(--radius-full);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-variant-numeric: tabular-nums;
  transition: background-color var(--dur-1) var(--ease-out);
}

.day:hover:not(.is-disabled):not(.is-selected) .disc {
  background: var(--bg-hover);
}

.day.is-outside .disc {
  color: var(--text-tertiary);
}

.day.is-today .disc {
  color: var(--accent-text);
  font-weight: var(--fw-bold);
  box-shadow: inset 0 0 0 1px var(--accent-text);
}

.day.is-highlight .disc {
  border: 1px dashed var(--border-control);
}

.day.is-selected .disc {
  background: var(--accent);
  color: var(--accent-fg);
  box-shadow: none;
}

.day.is-disabled {
  cursor: default;
}

.day.is-disabled .disc {
  color: var(--text-disabled);
}
</style>

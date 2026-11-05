<script setup lang="ts">
// The month grid (spec 8.9): role="grid" "Calendar", weekday column headers in locale order, then
// 5 or 6 rows of days. One day is the tab stop (the focused one, else today, else the 1st).
// Keys on a day: arrows move by a day or a week, Home and End go to the week's ends, PageUp and
// PageDown change the month, T goes to today, Enter moves into the day's chips, N opens quick
// create on the day. Moving past the shown weeks shows that day's month. Keys on a chip: Up and
// Down (or Tab) move through the day's chips and its "+N more", Enter opens the task, Esc goes back
// to the day. T and N follow the single-key shortcut setting (useShortcut, grid scope).
// From elsewhere on the page (or the body), the arrows, Home, End and T focus the tab-stop day
// (the remembered one, else today) and then do what they do there.
import { computed, nextTick, ref, watch } from 'vue'
import { useShortcut } from '../../composables/useShortcut'
import { addDaysIso, addMonthsIso, endOfWeekIso, startOfWeekIso } from '../../lib/dates'
import type { IsoDate, Uuid } from '../../types'
import { focusOutsideGrids } from '../shell/shellContext'
import CalendarDay from './CalendarDay.vue'
import type { CalendarModel } from './useCalendarModel'

const props = defineProps<{ model: CalendarModel }>()
const emit = defineEmits<{ open: [id: Uuid, e: MouseEvent | KeyboardEvent] }>()

const gridEl = ref<HTMLElement | null>(null)
/** The day that holds the tab stop once focus has been in the grid. */
const focused = ref<IsoDate | null>(null)

const EMPTY: never[] = []

const tabStop = computed<IsoDate>(() => {
  const m = props.model
  if (focused.value && m.inMonth(focused.value)) return focused.value
  if (m.inMonth(m.today.value)) return m.today.value
  return m.month.value
})

// Another month from the header: the tab stop follows it (today or the 1st).
watch(
  () => props.model.month.value,
  () => {
    if (focused.value && !props.model.inMonth(focused.value)) focused.value = null
  },
)

function cellOf(day: IsoDate): HTMLElement | null {
  return gridEl.value?.querySelector<HTMLElement>(`[data-day="${day}"]`) ?? null
}

function focusDay(day: IsoDate): void {
  const m = props.model
  if (!m.inGrid(day)) m.goToMonth(day)
  focused.value = day
  void nextTick(() => {
    const cell = cellOf(day)
    cell?.focus()
    cell?.scrollIntoView?.({ block: 'nearest' })
  })
}

function dayOfTarget(t: EventTarget | null): IsoDate | null {
  const cell = t instanceof Element ? t.closest<HTMLElement>('[data-day]') : null
  return (cell?.dataset.day as IsoDate | undefined) ?? null
}

function onFocusin(e: FocusEvent): void {
  const day = dayOfTarget(e.target)
  if (day) focused.value = day
}

/** The chips and "+N more" of the day that holds `el`. */
function itemsOf(el: HTMLElement): HTMLElement[] {
  const cell = el.closest<HTMLElement>('[data-day]')
  return cell ? Array.from(cell.querySelectorAll<HTMLElement>('.chips [data-chip], .chips .more')) : []
}

function onDayKey(e: KeyboardEvent, cell: HTMLElement): boolean {
  const day = cell.dataset.day as IsoDate
  switch (e.key) {
    case 'ArrowLeft':
      focusDay(addDaysIso(day, -1))
      return true
    case 'ArrowRight':
      focusDay(addDaysIso(day, 1))
      return true
    case 'ArrowUp':
      focusDay(addDaysIso(day, -7))
      return true
    case 'ArrowDown':
      focusDay(addDaysIso(day, 7))
      return true
    case 'Home':
      focusDay(startOfWeekIso(day))
      return true
    case 'End':
      focusDay(endOfWeekIso(day))
      return true
    case 'PageUp':
      focusDay(addMonthsIso(day, -1))
      return true
    case 'PageDown':
      focusDay(addMonthsIso(day, 1))
      return true
    case 'Enter': {
      const first = itemsOf(cell)[0]
      first?.focus()
      return true
    }
    default:
      return false
  }
}

function onItemKey(e: KeyboardEvent, item: HTMLElement): boolean {
  const items = itemsOf(item)
  const i = items.indexOf(item)
  const cell = item.closest<HTMLElement>('[data-day]')
  const back = e.key === 'ArrowUp' || (e.key === 'Tab' && e.shiftKey)
  const forward = (e.key === 'ArrowDown' && !item.classList.contains('more')) || (e.key === 'Tab' && !e.shiftKey)
  if (back) {
    const prev = items[i - 1]
    if (prev) prev.focus()
    else if (e.key === 'Tab') cell?.focus()
    return !!prev || e.key === 'Tab'
  }
  if (forward) {
    const next = items[i + 1]
    if (next) next.focus()
    return !!next
  }
  if (e.key === 'Escape') {
    cell?.focus()
    return true
  }
  return false
}

function onKeydown(e: KeyboardEvent): void {
  if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return
  const target = e.target instanceof HTMLElement ? e.target : null
  if (!target) return
  let handled = false
  if (target.matches('[data-day]')) {
    if (e.shiftKey) return
    handled = onDayKey(e, target)
  } else if (target.matches('.chips [data-chip], .chips .more')) {
    handled = onItemKey(e, target)
  }
  if (handled) {
    e.preventDefault()
    e.stopPropagation()
  }
}

// T and N: single-key shortcuts (9.5), so they follow the setting and never fire while typing.
useShortcut('t', () => focusDay(props.model.today.value), { scope: 'grid', el: gridEl })

// Getting in: the day keys, pressed outside the grid, act from the tab-stop day.
useShortcut(
  ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 't'],
  (e) => {
    if (e.key === 't' || e.key === 'T') return focusDay(props.model.today.value)
    const cell = cellOf(tabStop.value)
    if (!cell) return false
    onDayKey(e, cell)
  },
  { when: () => !!gridEl.value && focusOutsideGrids() },
)
useShortcut(
  'n',
  (e) => {
    const day = dayOfTarget(e.target) ?? dayOfTarget(document.activeElement)
    if (!day || !props.model.canEdit.value) return false
    props.model.createOn(day)
  },
  { scope: 'grid', el: gridEl },
)
</script>

<template>
  <div
    ref="gridEl"
    class="grid"
    role="grid"
    aria-label="Calendar"
    data-shortcut-scope="grid"
    @keydown="onKeydown"
    @focusin="onFocusin"
  >
    <div class="weekdays" role="row">
      <div v-for="w in model.weekdays" :key="w.long" class="weekday" role="columnheader">
        <span aria-hidden="true">{{ w.short }}</span>
        <span class="sr-only">{{ w.long }}</span>
      </div>
    </div>
    <div v-for="week in model.weeks.value" :key="week[0]" class="week" role="row">
      <CalendarDay
        v-for="day in week"
        :key="day"
        :model="model"
        :day="day"
        :tasks="model.byDay.value.get(day) ?? EMPTY"
        :tab-stop="day === tabStop"
        @open="(id, e) => emit('open', id, e)"
      />
    </div>
  </div>
</template>

<style scoped>
.grid {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--border-subtle);
  overflow: hidden;
}

.weekdays,
.week {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  gap: 1px;
}

.weekdays {
  flex: none;
}

.weekday {
  display: flex;
  align-items: center;
  height: 28px;
  padding: 0 var(--space-2);
  background: var(--bg-surface);
  color: var(--text-secondary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.week {
  flex: 1 0 auto;
}
</style>

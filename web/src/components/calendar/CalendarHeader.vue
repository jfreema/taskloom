<script setup lang="ts">
// The calendar header (spec 8.9), 44px inside the view: "Previous month", "Next month", "Today",
// then the month title (18/26 600, a polite live region so a month change is announced). On the
// right, the "To schedule" toggle chip with its count (aria-pressed), unless the agenda shows.
import { computed } from 'vue'
import { CalendarClock, ChevronLeft, ChevronRight } from '@lucide/vue'
import { formatMonthTitle } from '../../lib/dates'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { taskCount, type CalendarModel } from './useCalendarModel'

const props = defineProps<{
  model: CalendarModel
  /** null hides the toggle (the agenda has no tray). */
  trayOpen: boolean | null
  compact: boolean
}>()

const emit = defineEmits<{ toggleTray: [] }>()

const title = computed(() => formatMonthTitle(props.model.month.value))
const count = computed(() => props.model.trayCount.value)
</script>

<template>
  <div class="calendar-header" :class="{ 'is-compact': compact }">
    <div class="nav">
      <UiIconButton :icon="ChevronLeft" label="Previous month" size="sm" @click="model.shiftMonth(-1)" />
      <UiIconButton :icon="ChevronRight" label="Next month" size="sm" @click="model.shiftMonth(1)" />
      <UiButton size="sm" class="today" @click="model.goToToday()">Today</UiButton>
      <h2 class="title" aria-live="polite">{{ title }}</h2>
    </div>
    <button
      v-if="trayOpen !== null"
      type="button"
      class="tray-toggle"
      :class="{ 'is-on': trayOpen }"
      :aria-pressed="trayOpen ? 'true' : 'false'"
      :aria-label="`To schedule, ${taskCount(count)}`"
      @click="emit('toggleTray')"
    >
      <UiIcon :icon="CalendarClock" :size="16" />
      <span class="tray-label">To schedule</span>
      <span class="tray-count tabular" aria-hidden="true">{{ count }}</span>
    </button>
  </div>
</template>

<style scoped>
.calendar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  flex: none;
  height: 44px;
  min-width: 0;
}

.nav {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
}

.today {
  margin-left: var(--space-1);
}

.title {
  min-width: 0;
  margin-left: var(--space-2);
  font-size: var(--fs-xl);
  line-height: var(--lh-xl);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tray-toggle {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-1-5);
  height: var(--control-sm);
  padding: 0 var(--space-2) 0 var(--space-2);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.tray-toggle:hover {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
  color: var(--text-primary);
}

.tray-toggle.is-on {
  border-color: var(--accent);
  background: var(--bg-selected);
  color: var(--accent-text);
}

.tray-count {
  min-width: 18px;
  height: 18px;
  padding: 0 var(--space-1);
  border-radius: var(--radius-full);
  background: var(--bg-subtle);
  color: var(--text-secondary);
  font-size: var(--fs-2xs);
  line-height: 18px;
  text-align: center;
}

.is-on .tray-count {
  background: var(--bg-surface);
  color: var(--accent-text);
}

.is-compact .title {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
}

.is-compact .tray-label {
  display: none;
}
</style>

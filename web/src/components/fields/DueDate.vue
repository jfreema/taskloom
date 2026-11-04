<script setup lang="ts">
// Due date text (7.11): formatDue ("Today", "Tomorrow", "Fri", "Nov 12"). Overdue and not done:
// --danger-text with a CircleAlert, named "Overdue: <date>". Due today: --warning-text. Renders
// nothing without a date. sm is 12/16 (cards, phone rows), md 13/18 (table cells).
import { computed } from 'vue'
import { Calendar, CircleAlert } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { formatDue } from '../../lib/dates'
import type { IsoDate } from '../../types'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    date: IsoDate | null
    done?: boolean
    showIcon?: boolean
    size?: 'sm' | 'md'
  }>(),
  { done: false, showIcon: false, size: 'md' },
)

const today = useToday()

const text = computed(() => (props.date ? formatDue(props.date, today.value) : ''))
const state = computed<'overdue' | 'today' | 'normal'>(() => {
  if (!props.date || props.done) return 'normal'
  if (props.date < today.value) return 'overdue'
  return props.date === today.value ? 'today' : 'normal'
})
const iconSize = computed(() => 14 as const)
</script>

<template>
  <span
    v-if="date"
    class="due-date tabular"
    :class="[`due-date--${size}`, `is-${state}`]"
    :role="state === 'overdue' ? 'img' : undefined"
    :aria-label="state === 'overdue' ? `Overdue: ${text}` : undefined"
  >
    <UiIcon v-if="state === 'overdue'" class="icon" :icon="CircleAlert" :size="iconSize" />
    <UiIcon v-else-if="showIcon" class="icon" :icon="Calendar" :size="iconSize" />
    <span class="text">{{ text }}</span>
  </span>
</template>

<style scoped>
.due-date {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  color: var(--text-secondary);
  white-space: nowrap;
}

.due-date--sm {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.due-date--md {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.icon {
  flex-shrink: 0;
}

.due-date:not(.is-overdue, .is-today) .icon {
  color: var(--text-tertiary);
}

.is-overdue {
  color: var(--danger-text);
}

.is-today {
  color: var(--warning-text);
}

.text {
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>

<script setup lang="ts">
// The agenda (spec 8.9): below 640px of page width the month becomes a list. On the current month
// it starts with "Overdue", then "Today" (always) and every later day of the month with tasks; on
// another month, every day of it with tasks. "No due date" ends the list. Day headings are sticky
// ("Today", "Thu, Nov 5"). Rows: CompleteToggle in the status colour, the title, PriorityFlag and
// avatars; a tap opens the task. Nothing drags here: rescheduling is the task's due date picker.
import { computed, useId } from 'vue'
import { CalendarDays } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { formatDateMedium, monthGrid } from '../../lib/dates'
import { useMembers } from '../../stores/members'
import type { IsoDate, Task, Uuid } from '../../types'
import CompleteToggle from '../fields/CompleteToggle.vue'
import PriorityFlag from '../fields/PriorityFlag.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import type { CalendarModel } from './useCalendarModel'

const props = defineProps<{ model: CalendarModel }>()
const emit = defineEmits<{ open: [id: Uuid, e: MouseEvent] }>()

const actions = useTaskActions()
const members = useMembers()
/** Prefix of the section heading ids, unique to this agenda. */
const uid = useId()

interface Section {
  key: string
  heading: string
  tone?: 'danger'
  tasks: Task[]
  empty?: string
}

const today = computed(() => props.model.today.value)
const currentMonth = computed(() => props.model.inMonth(today.value))

const sections = computed<Section[]>(() => {
  const m = props.model
  const out: Section[] = []
  if (currentMonth.value && m.overdue.value.length) {
    out.push({ key: 'overdue', heading: 'Overdue', tone: 'danger', tasks: m.overdue.value })
  }
  const days: IsoDate[] = monthGrid(m.month.value).flat().filter((d) => m.inMonth(d))
  for (const d of days) {
    if (currentMonth.value && d < today.value) continue
    const list = m.byDay.value.get(d) ?? []
    const isToday = d === today.value
    if (!list.length && !isToday) continue
    out.push({
      key: d,
      heading: isToday ? 'Today' : formatDateMedium(d, today.value),
      tasks: list,
      empty: isToday ? 'Nothing due today.' : undefined,
    })
  }
  if (m.noDue.value.length) out.push({ key: 'none', heading: 'No due date', tasks: m.noDue.value })
  return out
})

function people(t: Task): { id: string; displayName: string }[] {
  return t.assigneeIds.map((id) => ({ id, displayName: members.nameOf(id) }))
}

/** A tap anywhere on the row, outside the complete toggle, opens the task. */
function onRowClick(e: MouseEvent, id: Uuid): void {
  const control = e.target instanceof Element ? e.target.closest('button, a, [role="checkbox"]') : null
  if (control) return
  emit('open', id, e)
}
</script>

<template>
  <div class="agenda">
    <section v-for="s in sections" :key="s.key" class="section" :aria-labelledby="`${uid}-agenda-${s.key}`">
      <h3 :id="`${uid}-agenda-${s.key}`" class="heading" :class="{ 'is-danger': s.tone === 'danger' }">
        {{ s.heading }}<span v-if="s.tasks.length" class="count tabular" aria-hidden="true">{{ s.tasks.length }}</span>
      </h3>
      <ul v-if="s.tasks.length" class="rows" role="list">
        <li
          v-for="t in s.tasks"
          :key="t.id"
          class="row"
          :class="{ 'is-current': model.openId.value === t.id, 'is-done': model.isDone(t) }"
          @click="onRowClick($event, t.id)"
        >
          <CompleteToggle
            class="toggle"
            :done="model.isDone(t)"
            :label="t.title"
            :color="model.status(t)?.color"
            :readonly="!model.canEdit.value"
            @toggle="actions.toggleComplete(t.id)"
          />
          <button type="button" class="open" :data-task-row="t.id" @click="emit('open', t.id, $event)">
            <span class="title truncate">{{ t.title }}</span>
            <span v-if="model.parentTitle(t)" class="parent truncate">in {{ model.parentTitle(t) }}</span>
          </button>
          <PriorityFlag v-if="t.priority !== 'None'" class="flag" :priority="t.priority" :size="14" />
          <UiAvatarStack v-if="t.assigneeIds.length" class="people" :users="people(t)" :max="2" :size="20" />
        </li>
      </ul>
      <p v-else-if="s.empty" class="empty-line">{{ s.empty }}</p>
    </section>
    <div v-if="!sections.length" class="empty">
      <UiEmptyState
        :icon="CalendarDays"
        title="Nothing due this month"
        description="Tasks with a due date in this month show here."
      />
    </div>
  </div>
</template>

<style scoped>
.agenda {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-width: 0;
}

.section {
  display: flex;
  flex-direction: column;
}

.heading {
  position: sticky;
  top: 0;
  z-index: var(--z-sticky);
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  height: 36px;
  padding: 0 var(--space-1);
  background: var(--bg-surface);
  border-bottom: 1px solid var(--border-subtle);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.heading.is-danger {
  color: var(--danger-text);
}

.count {
  color: var(--text-tertiary);
  font-weight: var(--fw-medium);
}

.rows {
  margin: 0;
  padding: var(--space-1) 0;
  list-style: none;
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: 48px;
  padding: 0 var(--space-1);
  border-radius: var(--radius-md);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}

.row:active {
  background: var(--bg-pressed);
}

.row.is-current {
  background: var(--bg-selected);
  box-shadow: inset 2px 0 0 var(--accent);
}

.toggle {
  flex-shrink: 0;
}

.open {
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 44px;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.open:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 2px;
  border-radius: var(--radius-xs);
}

.title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.is-done .title {
  color: var(--text-tertiary);
}

.parent {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.flag,
.people {
  flex-shrink: 0;
}

.empty-line {
  padding: var(--space-3) var(--space-1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.empty {
  display: flex;
  justify-content: center;
  padding: var(--space-12) var(--space-4);
}
</style>

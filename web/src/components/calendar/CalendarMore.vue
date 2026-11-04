<script setup lang="ts">
// A day's "+N more" (spec 8.9): a popover listing every task of the day as buttons, each with its
// status bar and the chip's name. A click opens the task; Up and Down move through the list;
// nothing drags here. The trigger stays out of the tab order: the grid moves focus to it (Up, Down
// and Tab among the day's chips).
import { ref } from 'vue'
import { formatDateLong } from '../../lib/dates'
import type { IsoDate, Task, Uuid } from '../../types'
import UiPopover from '../ui/UiPopover.vue'
import type { CalendarModel } from './useCalendarModel'

const props = defineProps<{ model: CalendarModel; day: IsoDate; tasks: Task[]; count: number }>()
const emit = defineEmits<{ open: [id: Uuid, e: MouseEvent | KeyboardEvent] }>()

const open = ref(false)

function nameOf(t: Task): string {
  const s = props.model.status(t)?.name ?? 'No status'
  return `${t.title}, ${s}, due ${formatDateLong(props.day)}`
}

function choose(id: Uuid, e: MouseEvent | KeyboardEvent, close: () => void): void {
  close()
  emit('open', id, e)
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
  const items = Array.from((e.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('.item'))
  const i = items.indexOf(document.activeElement as HTMLElement)
  const next = items[Math.max(0, Math.min(items.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))]
  if (next) {
    e.preventDefault()
    next.focus()
  }
}
</script>

<template>
  <UiPopover
    v-model:open="open"
    placement="bottom-start"
    :width="260"
    :aria-label="formatDateLong(day)"
    @keydown="onKeydown"
  >
    <template #trigger="{ props: trigger }">
      <button v-bind="trigger" type="button" class="more" tabindex="-1" data-no-drag>+{{ count }} more</button>
    </template>
    <template #default="{ close }">
      <div class="body">
        <p class="title" aria-hidden="true">{{ formatDateLong(day) }}</p>
        <ul class="list" role="list">
          <li v-for="t in tasks" :key="t.id">
            <button
              type="button"
              class="item"
              :class="{ 'is-done': model.isDone(t) }"
              :style="{ '--c': model.status(t)?.color ?? 'var(--border-control)' }"
              :aria-label="nameOf(t)"
              @click="choose(t.id, $event, close)"
            >
              <span class="bar" aria-hidden="true" />
              <span class="item-title truncate">{{ t.title }}</span>
            </button>
          </li>
        </ul>
      </div>
    </template>
  </UiPopover>
</template>

<style scoped>
.more {
  align-self: flex-start;
  height: 18px;
  margin-top: var(--space-0-5);
  padding: 0 var(--space-1);
  border: 0;
  border-radius: var(--radius-xs);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  cursor: pointer;
}

.more:hover,
.more[aria-expanded="true"] {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.more:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 1px;
}

/* The popover's content is teleported; it still carries this component's scope. */
.body {
  padding: var(--space-1-5);
}

.title {
  padding: var(--space-1) var(--space-2) var(--space-1-5);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-secondary);
}

.list {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  margin: 0;
  padding: 0;
  list-style: none;
}

.item {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  height: var(--row-menu);
  padding: 0 var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-align: left;
  cursor: pointer;
}

.item:hover {
  background: var(--bg-hover);
}

.item:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: -2px;
}

.item.is-done .item-title {
  color: var(--text-tertiary);
}

.bar {
  flex-shrink: 0;
  width: 3px;
  height: 16px;
  border-radius: var(--radius-full);
  background: var(--c);
}

.item-title {
  flex: 1 1 auto;
  min-width: 0;
}
</style>

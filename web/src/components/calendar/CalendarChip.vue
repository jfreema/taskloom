<script setup lang="ts">
// One task chip (spec 8.9): a `role="button"` (a div, so that it drags in every browser) on a
// .tint-pill with a 3px bar in the status colour, the title in 12/16 500 with an ellipsis. Done
// chips have a tertiary title on an untinted chip (the bar keeps the status colour), which keeps
// that title above 4.5:1 in both themes whatever the status colour. Subtasks name their parent in the tooltip; other chips show the
// title there only when it is cut off. Members drag it with the mouse or a pen (useDnd); while it
// drags it stays behind as a dashed placeholder.
// Variants: `day` (a grid cell, named "<title>, <status>, due <date>"), `overdue` and `nodue` (the
// To schedule tray: "<title>, overdue since <date>" and "<title>, <status>, no due date"; taller,
// and overdue chips show their date). The parent handles keys; the chip only reports activation.
import { computed, ref } from 'vue'
import { useDraggable } from '../../composables/useDnd'
import { useToday } from '../../composables/useToday'
import { formatDateLong, formatDue } from '../../lib/dates'
import type { Status, Task } from '../../types'

const props = withDefaults(
  defineProps<{
    task: Task
    status: Status | undefined
    done: boolean
    parentTitle: string | null
    variant?: 'day' | 'overdue' | 'nodue'
    /** Open in the task panel. */
    current?: boolean
    canDrag: boolean
    /** Leave tabindex to a roving-focus container (the tray). */
    roving?: boolean
  }>(),
  { variant: 'day', current: false, roving: false },
)

const emit = defineEmits<{ activate: [e: MouseEvent | KeyboardEvent] }>()

const today = useToday()
const el = ref<HTMLElement | null>(null)

const { dragging } = useDraggable(el, {
  data: () => ({ kind: 'task', id: props.task.id, listId: props.task.listId, fromDate: props.task.dueDate }),
  disabled: () => !props.canDrag,
})

const statusName = computed(() => props.status?.name ?? 'No status')

const label = computed(() => {
  const t = props.task
  if (props.variant === 'overdue' && t.dueDate) return `${t.title}, overdue since ${formatDateLong(t.dueDate)}`
  if (props.variant === 'nodue' || !t.dueDate) return `${t.title}, ${statusName.value}, no due date`
  return `${t.title}, ${statusName.value}, due ${formatDateLong(t.dueDate)}`
})

const tooltip = computed(() =>
  props.parentTitle
    ? { text: `${props.task.title} · Subtask of ${props.parentTitle}` }
    : { text: props.task.title, overflowOnly: true },
)

const dueText = computed(() => (props.variant === 'overdue' && props.task.dueDate ? formatDue(props.task.dueDate, today.value) : ''))

function onKeydown(e: KeyboardEvent): void {
  if ((e.key === 'Enter' || e.key === ' ') && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
    e.preventDefault()
    if (!e.repeat) emit('activate', e)
  }
}
</script>

<template>
  <div
    ref="el"
    v-tooltip="tooltip"
    role="button"
    class="chip tint-pill"
    :class="[`chip--${variant}`, { 'is-done': done, 'is-current': current, 'is-dragging': dragging, 'can-drag': canDrag }]"
    :style="{ '--c': status?.color ?? 'var(--border-control)' }"
    :tabindex="roving ? undefined : -1"
    :aria-label="label"
    :aria-current="current ? 'true' : undefined"
    data-chip
    @click="emit('activate', $event)"
    @keydown="onKeydown"
  >
    <span class="title truncate">{{ task.title }}</span>
    <span v-if="dueText" class="due" aria-hidden="true">{{ dueText }}</span>
  </div>
</template>

<style scoped>
.chip {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  height: 22px;
  padding: 0 var(--space-1-5) 0 var(--space-1);
  border-left: 3px solid var(--c);
  border-radius: var(--radius-xs);
  cursor: pointer;
  user-select: none;
  -webkit-user-select: none;
  transition:
    background-color var(--dur-1) var(--ease-out),
    opacity var(--dur-1) var(--ease-out);
}

.chip.can-drag {
  cursor: grab;
}

.chip:hover {
  background: color-mix(in srgb, var(--c) calc(var(--tint-pill) + 8%), var(--bg-surface));
}

.chip:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: 1px;
}

.chip.is-current {
  box-shadow: 0 0 0 2px var(--accent);
}

.title {
  flex: 1 1 auto;
  min-width: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.is-done .title {
  color: var(--text-tertiary);
}

/* Done chips recede: no tint, a quiet edge, the status bar kept. */
.chip.is-done {
  background: var(--bg-surface);
  border-color: var(--border-subtle);
  border-left-color: var(--c);
}

.chip.is-done:hover {
  background: linear-gradient(var(--bg-hover), var(--bg-hover)), var(--bg-surface);
}

.due {
  flex-shrink: 0;
  font-size: var(--fs-2xs);
  line-height: var(--lh-2xs);
  font-weight: var(--fw-medium);
  color: var(--danger-text);
}

/* The tray's chips are list rows: taller, with room for longer titles. */
.chip--overdue,
.chip--nodue {
  height: 28px;
  padding: 0 var(--space-2) 0 var(--space-1-5);
}

.chip--overdue .title,
.chip--nodue .title {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

/* The source of a drag stays in place as a dashed placeholder. */
.chip.is-dragging {
  opacity: 0.4;
  border-style: dashed;
  border-left-style: solid;
}
</style>

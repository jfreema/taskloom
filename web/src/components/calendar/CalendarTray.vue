<script setup lang="ts">
// The To schedule tray (spec 8.9): 260px on --bg-column, radius 10, a region named "To schedule".
// - Header "To schedule N" with a close button.
// - "Overdue N": open tasks due before today, oldest first (they are on their day in the grid too).
// - "No due date N": open tasks without a due date, by position. It is the tray's drop target: a
//   dated chip dropped on it loses its due date. While nothing is in it, it shows only during a
//   drag, as a drop zone. "Overdue" is never a drop target.
// - With nothing to schedule it reads "Nothing to schedule."
// - Its chips form one list: Up and Down move (roving focus), Enter opens.
// - `overlay`: below 900px of page width it floats over the grid's right edge (non-modal); Esc
//   inside it closes it.
import { computed, ref, useId } from 'vue'
import { X } from '@lucide/vue'
import { activeDrag, useDropTarget } from '../../composables/useDnd'
import { useRovingFocus } from '../../composables/useRovingFocus'
import type { Uuid } from '../../types'
import UiIconButton from '../ui/UiIconButton.vue'
import CalendarChip from './CalendarChip.vue'
import type { CalendarModel } from './useCalendarModel'

const props = defineProps<{ model: CalendarModel; overlay: boolean }>()
const emit = defineEmits<{ close: []; open: [id: Uuid, e: MouseEvent | KeyboardEvent] }>()

const uid = useId()
const root = ref<HTMLElement | null>(null)
const noDueEl = ref<HTMLElement | null>(null)

useRovingFocus(root, { itemSelector: '[data-tray-chip]', orientation: 'vertical' })

const overdue = computed(() => props.model.overdue.value)
const noDue = computed(() => props.model.noDue.value)
const count = computed(() => props.model.trayCount.value)
const canEdit = computed(() => props.model.canEdit.value)

/** A dated task of this list is being dragged: "No due date" can take it. */
const datedDrag = computed(() => {
  const d = activeDrag.value
  return !!d && d.listId === props.model.listId.value && d.fromDate != null
})

const { over } = useDropTarget(noDueEl, {
  data: () => ({ day: null }),
  accepts: (d) => d.listId === props.model.listId.value && d.fromDate != null,
  onDrop: (d) => props.model.reschedule(d.id, null),
})

const showNoDue = computed(() => noDue.value.length > 0 || datedDrag.value)

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape' && props.overlay && !e.defaultPrevented) {
    e.preventDefault()
    e.stopPropagation()
    emit('close')
  }
}
</script>

<template>
  <section
    ref="root"
    class="tray"
    :class="{ 'is-overlay': overlay }"
    aria-label="To schedule"
    @keydown="onKeydown"
  >
    <header class="head">
      <h2 class="title">
        To schedule <span class="count tabular">{{ count }}</span>
      </h2>
      <UiIconButton :icon="X" label="Close To schedule" size="sm" @click="emit('close')" />
    </header>

    <div class="body">
      <section v-if="overdue.length" class="group" :aria-labelledby="`${uid}-overdue`">
        <h3 :id="`${uid}-overdue`" class="group-title">
          Overdue <span class="count count--danger tabular">{{ overdue.length }}</span>
        </h3>
        <ul class="chips" role="list">
          <li v-for="t in overdue" :key="t.id">
            <CalendarChip
              :task="t"
              :status="model.status(t)"
              :done="false"
              :parent-title="model.parentTitle(t)"
              variant="overdue"
              :current="model.openId.value === t.id"
              :can-drag="canEdit"
              roving
              data-tray-chip
              @activate="emit('open', t.id, $event)"
            />
          </li>
        </ul>
      </section>

      <section
        v-show="showNoDue"
        ref="noDueEl"
        class="group group--drop"
        :class="{ 'is-over': over, 'is-armed': datedDrag }"
        :aria-labelledby="`${uid}-nodue`"
      >
        <h3 :id="`${uid}-nodue`" class="group-title">
          No due date <span class="count tabular">{{ noDue.length }}</span>
        </h3>
        <ul v-if="noDue.length" class="chips" role="list">
          <li v-for="t in noDue" :key="t.id">
            <CalendarChip
              :task="t"
              :status="model.status(t)"
              :done="false"
              :parent-title="model.parentTitle(t)"
              variant="nodue"
              :current="model.openId.value === t.id"
              :can-drag="canEdit"
              roving
              data-tray-chip
              :data-task-row="t.id"
              @activate="emit('open', t.id, $event)"
            />
          </li>
        </ul>
        <p v-else class="drop-hint">Drop here to remove the due date.</p>
      </section>

      <p v-if="!overdue.length && !showNoDue" class="empty">Nothing to schedule.</p>
    </div>
  </section>
</template>

<style scoped>
.tray {
  display: flex;
  flex-direction: column;
  width: 260px;
  min-height: 0;
  border-radius: var(--radius-lg);
  background: var(--bg-column);
}

.tray.is-overlay {
  border: 1px solid var(--border-floating);
  box-shadow: var(--shadow-3);
}

.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  flex: none;
  height: 44px;
  padding: 0 var(--space-2) 0 var(--space-3);
}

.title {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.count {
  color: var(--text-tertiary);
  font-weight: var(--fw-medium);
}

.count--danger {
  color: var(--danger-text);
}

.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  flex: 1 1 auto;
  min-height: 0;
  padding: 0 var(--space-2) var(--space-3);
  overflow-y: auto;
  overscroll-behavior: contain;
}

.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  padding: var(--space-1);
  border: 1px dashed transparent;
  border-radius: var(--radius-md);
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out);
}

.group-title {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  padding: 0 var(--space-1);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-secondary);
}

.chips {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: 0;
  padding: 0;
  list-style: none;
}

.group--drop.is-armed {
  border-color: var(--border-control);
}

.group--drop.is-over {
  border-color: var(--accent);
  background: var(--bg-selected);
}

.drop-hint,
.empty {
  padding: var(--space-2) var(--space-1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.empty {
  padding: var(--space-4) var(--space-2);
  text-align: center;
}
</style>

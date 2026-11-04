<script setup lang="ts">
// A collapsed column (8.8): a 44px strip with the status icon, the count and the name written
// vertically. A click expands it. It stays a drop target: a card dropped on it goes to the bottom
// of that status.
import { computed, ref } from 'vue'
import { activeDrag, useDropTarget } from '../../composables/useDnd'
import { pluralize } from '../../lib/format'
import StatusIcon from '../fields/StatusIcon.vue'
import { useBoard } from './boardContext'
import type { BoardColumnView } from './useBoardModel'

const props = defineProps<{ column: BoardColumnView }>()

const board = useBoard()
const el = ref<HTMLElement | null>(null)

const statusId = computed(() => props.column.status.id)
const name = computed(() => props.column.status.name)
const count = computed(() => props.column.tasks.length)
const label = computed(() => `${name.value}, ${pluralize(count.value, 'task')}`)

const { over } = useDropTarget(el, {
  data: () => ({ type: 'column', statusId: statusId.value }),
  accepts: (d) => board.canDrag.value && d.listId === board.listId.value,
  onDrop: (d) => board.drop(d, statusId.value, 'end'),
})

const target = computed(() => over.value && activeDrag.value !== null && board.resolveDrop(activeDrag.value, statusId.value, 'end') !== null)

function expand(): void {
  board.model.setCollapsed(statusId.value, false)
}
</script>

<template>
  <section ref="el" class="collapsed-column" :class="{ 'is-over': target }" :aria-label="label" :data-column="column.status.id">
    <button
      v-tooltip="{ text: `Expand ${name}`, placement: 'right' }"
      type="button"
      class="strip focus-inset"
      :aria-label="`Expand ${name}`"
      aria-expanded="false"
      data-strip
      @click="expand"
    >
      <StatusIcon :status="column.status" :size="16" />
      <span class="count tabular" aria-hidden="true">{{ count }}</span>
      <span class="name" aria-hidden="true">{{ name }}</span>
    </button>
  </section>
</template>

<style scoped>
.collapsed-column {
  display: flex;
  flex: none;
  width: 44px;
  max-height: 100%;
  min-height: 0;
  border-radius: var(--radius-lg);
  background: var(--bg-column);
  transition:
    background-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.collapsed-column.is-over {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
  box-shadow: inset 0 0 0 2px var(--accent);
}

.strip {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  align-items: center;
  gap: var(--space-2);
  min-height: 160px;
  padding: var(--space-3) 0 var(--space-4);
  border: 0;
  border-radius: inherit;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  cursor: pointer;
  overflow: hidden;
  transition: background-color var(--dur-1) var(--ease-out);
}

.strip:hover {
  background: var(--bg-hover);
}

.count {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
}

.name {
  min-height: 0;
  overflow: hidden;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  text-overflow: ellipsis;
  white-space: nowrap;
  writing-mode: vertical-rl;
}
</style>

<script setup lang="ts">
// One row of a /_ui state table: the sample's name, then one cell per state of STATES. The slot
// renders the sample for a state; states the sample doesn't have stay empty ("Not applicable").
// The Hover and Focus cells mark their sample's interactive element (forcedStates.ts) and keep it
// marked when the sample re-renders.
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { markTarget } from './forcedStates'
import { STATES, type SampleState } from './states'

const props = defineProps<{
  label: string
  /** The states this sample has. */
  states: readonly SampleState[]
  /** A short second line under the name. */
  note?: string
}>()

defineSlots<{ default(p: { state: SampleState }): unknown }>()

const row = ref<HTMLElement | null>(null)
let observer: MutationObserver | null = null

function mark(): void {
  for (const cell of Array.from(row.value?.querySelectorAll<HTMLElement>('[data-forced]') ?? [])) {
    markTarget(cell, cell.dataset.forced === 'focus' ? 'focus' : 'hover')
  }
}

onMounted(() => {
  mark()
  if (!row.value) return
  observer = new MutationObserver(mark)
  observer.observe(row.value, { childList: true, subtree: true })
})

onBeforeUnmount(() => observer?.disconnect())

const has = (s: SampleState) => props.states.includes(s)
</script>

<template>
  <tr ref="row" class="state-row">
    <th scope="row" class="name">
      <span class="label">{{ label }}</span>
      <span v-if="note" class="note">{{ note }}</span>
    </th>
    <td v-for="s in STATES" :key="s" class="cell" :class="{ 'is-na': !has(s) }">
      <div
        v-if="has(s)"
        class="sample"
        :data-forced="s === 'hover' || s === 'focus' ? s : undefined"
      >
        <slot :state="s" />
      </div>
      <span v-else class="sr-only">Not applicable</span>
    </td>
  </tr>
</template>

<style scoped>
.state-row + .state-row > * {
  border-top: 1px solid var(--border-subtle);
}

.name {
  position: sticky;
  left: 0;
  z-index: 1;
  padding: var(--space-3) var(--space-3) var(--space-3) 0;
  background: var(--bg-surface);
  text-align: left;
  vertical-align: middle;
  font-weight: var(--fw-regular);
}

.label {
  display: block;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.note {
  display: block;
  margin-top: var(--space-0-5);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.cell {
  padding: var(--space-3) var(--space-2);
  vertical-align: middle;
}

.cell.is-na {
  background: var(--bg-subtle);
}

.sample {
  display: flex;
  align-items: center;
  min-width: 0;
}

.sample > :deep(.ui-input),
.sample > :deep(.ui-textarea),
.sample > :deep(.ui-select),
.sample > :deep(.ui-field) {
  flex: 1;
  min-width: 0;
}
</style>

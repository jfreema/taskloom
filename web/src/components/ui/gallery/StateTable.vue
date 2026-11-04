<script setup lang="ts">
// A /_ui state table: a full-width card with one column per state (states.ts) and StateRow
// children. Hover and Focus are forced (forcedStates.ts); empty cells are states the sample
// doesn't have. The table scrolls sideways inside the card on narrow screens, with the names
// column pinned.
import { STATES, STATE_LABEL } from './states'

defineProps<{ title: string; note?: string }>()
</script>

<template>
  <section class="state-table" :aria-label="title">
    <div class="head">
      <h2 class="title">{{ title }}</h2>
      <p v-if="note" class="note">{{ note }}</p>
    </div>
    <div class="scroller">
      <table class="table">
        <colgroup>
          <col class="name-col" />
          <col v-for="s in STATES" :key="s" />
        </colgroup>
        <thead>
          <tr>
            <th scope="col" class="corner"><span class="sr-only">Sample</span></th>
            <th v-for="s in STATES" :key="s" scope="col" class="state">{{ STATE_LABEL[s] }}</th>
          </tr>
        </thead>
        <tbody>
          <slot />
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.state-table {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
  padding: var(--space-4) var(--space-5) var(--space-3);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  box-shadow: var(--shadow-1);
}

.title {
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
}

.note {
  margin-top: var(--space-0-5);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

/* Positioned, so it also clips the visually hidden "Not applicable" texts (absolute). */
.scroller {
  position: relative;
  min-width: 0;
  overflow-x: auto;
  overscroll-behavior-x: contain;
}

/* Fixed columns: the names column, then seven equal ones, never narrower than a control. */
.table {
  width: 100%;
  min-width: 1100px;
  table-layout: fixed;
  border-collapse: collapse;
}

.name-col {
  width: 148px;
}

.corner,
.state {
  padding: var(--space-1) var(--space-2) var(--space-2);
  border-bottom: 1px solid var(--border-default);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  text-align: left;
  white-space: nowrap;
}

.corner {
  position: sticky;
  left: 0;
  z-index: 1;
  padding-left: 0;
  background: var(--bg-surface);
}

@media (max-width: 639px) {
  .state-table {
    padding: var(--space-3) var(--space-4) var(--space-2);
  }
}
</style>

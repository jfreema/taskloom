<script setup lang="ts">
// "Filters are hiding 12 tasks · Clear filters" (7.12): shown at the end of a filtered view while
// filters or the search hide tasks. The view decides what "Clear filters" resets.
import { pluralize } from '../../lib/format'

defineProps<{ hiddenCount: number }>()

const emit = defineEmits<{ clear: [] }>()
</script>

<template>
  <p v-if="hiddenCount > 0" class="filter-hidden-note">
    Filters are hiding <span class="tabular">{{ pluralize(hiddenCount, 'task') }}</span> ·
    <button type="button" class="clear" @click="emit('clear')">Clear filters</button>
  </p>
</template>

<style scoped>
.filter-hidden-note {
  margin: 0;
  padding: var(--space-3) var(--space-4);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.clear {
  display: inline;
  padding: 0;
  border: 0;
  background: transparent;
  color: var(--accent-text);
  font: inherit;
  font-weight: var(--fw-medium);
  cursor: pointer;
}

.clear:hover {
  text-decoration: underline;
}
</style>

<script setup lang="ts">
// The Filter button (7.12): "Filter" with a count badge, named "Filter" or "Filter, 2 active". It
// opens a popover with every facet's checkbox list (FilterSections). "Me" there is the same value
// as the "Assigned to me" chip ('me' in TaskFilters.assignees) and counts once. A round chip on
// phones, where the toolbar is a row of chips.
import { computed } from 'vue'
import { ListFilter } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { activeFilterCount, type TaskFilters } from '../../lib/taskQuery'
import type { Uuid } from '../../types'
import UiBadge from '../ui/UiBadge.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiPopover from '../ui/UiPopover.vue'
import FilterSections from './FilterSections.vue'

const open = defineModel<boolean>('open', { default: false })

const props = defineProps<{ modelValue: TaskFilters; listId: Uuid }>()

const emit = defineEmits<{ 'update:modelValue': [filters: TaskFilters] }>()

const { isPhone } = useBreakpoint()
const count = computed(() => activeFilterCount(props.modelValue))
const label = computed(() => (count.value ? `Filter, ${count.value} active` : 'Filter'))
</script>

<template>
  <UiPopover v-model:open="open" aria-label="Filter" :width="280">
    <template #trigger="{ props: t }">
      <button
        v-bind="t"
        type="button"
        class="filter-button"
        :class="{ 'is-chip': isPhone, 'is-active': count > 0 }"
        :aria-label="label"
      >
        <UiIcon :icon="ListFilter" :size="16" />
        <span class="text">Filter</span>
        <UiBadge v-if="count" class="count tabular" tone="accent" variant="solid" aria-hidden="true">{{ count }}</UiBadge>
      </button>
    </template>
    <template #default>
      <FilterSections :model-value="modelValue" :list-id="listId" @update:model-value="emit('update:modelValue', $event)" />
    </template>
  </UiPopover>
</template>

<style scoped>
.filter-button {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-1-5);
  height: var(--control-sm);
  padding: 0 var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.filter-button:hover,
.filter-button[aria-expanded="true"] {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.filter-button.is-active {
  color: var(--text-primary);
}

.count {
  justify-content: center;
  min-width: 18px;
}

/* Phones: a round chip like the others in the row. */
.filter-button.is-chip {
  padding: 0 var(--space-3);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.filter-button.is-chip.is-active {
  border-color: var(--accent);
  background: var(--bg-selected);
  color: var(--accent-text);
}

/* Touch (7.5): a 44px target around the 28px button (the toolbar clips it to its 43px row). */
@media (pointer: coarse) {
  .filter-button {
    position: relative;
  }

  .filter-button::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: max(100%, var(--touch-min));
    height: max(100%, var(--touch-min));
    transform: translate(-50%, -50%);
  }
}
</style>

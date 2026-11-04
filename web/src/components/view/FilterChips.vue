<script setup lang="ts">
// The filter chips (7.12): the "Assigned to me" toggle (always shown, aria-pressed; it toggles 'me'
// in TaskFilters.assignees, the same value as the Filter popover's "Me"), one chip per other
// active facet ("Status · 2", removable, opening that facet's checkboxes), then "Clear all" while
// any filter is on. Removing a chip moves focus to the next chip, else the one before.
import { computed, nextTick, ref } from 'vue'
import { activeFilterCount, emptyFilters, type TaskFilters } from '../../lib/taskQuery'
import type { Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiChip from '../ui/UiChip.vue'
import FilterFacetChip from './FilterFacetChip.vue'
import { chipFacets, hasValue, toggleValue } from './filterFacets'

const props = defineProps<{ modelValue: TaskFilters; listId: Uuid }>()

const emit = defineEmits<{ 'update:modelValue': [filters: TaskFilters] }>()

const root = ref<HTMLElement | null>(null)

const mine = computed(() => hasValue(props.modelValue, 'assignees', 'me'))
const facets = computed(() => chipFacets(props.modelValue))
const count = computed(() => activeFilterCount(props.modelValue))

function toggleMine(): void {
  emit('update:modelValue', toggleValue(props.modelValue, 'assignees', 'me'))
}

function onRemoved(index: number): void {
  void nextTick(() => {
    const chips = [...(root.value?.querySelectorAll<HTMLElement>('[data-facet-chip]') ?? [])]
    const target = chips[index] ?? chips[index - 1] ?? root.value?.querySelector<HTMLElement>('.mine button')
    target?.focus()
  })
}

function clearAll(): void {
  emit('update:modelValue', emptyFilters())
  void nextTick(() => root.value?.querySelector<HTMLElement>('.mine button')?.focus())
}
</script>

<template>
  <div ref="root" class="filter-chips">
    <UiChip class="mine" size="md" label="Assigned to me" :selected="mine" @click="toggleMine" />
    <FilterFacetChip
      v-for="(facet, i) in facets"
      :key="facet"
      :facet="facet"
      :model-value="modelValue"
      :list-id="listId"
      @update:model-value="emit('update:modelValue', $event)"
      @removed="onRemoved(i)"
    />
    <UiButton v-if="count" class="clear" variant="ghost" size="sm" @click="clearAll">Clear all</UiButton>
  </div>
</template>

<style scoped>
.filter-chips {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-1-5);
}

.clear {
  flex-shrink: 0;
}
</style>

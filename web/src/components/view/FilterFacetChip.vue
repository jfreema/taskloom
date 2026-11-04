<script setup lang="ts">
// One active facet in FilterChips (7.12): "Status · 2", its values in the tooltip. The chip opens a
// popover with that facet's checkboxes; its "Remove Status filter" button clears the facet
// ('me' stays: it belongs to the "Assigned to me" chip).
import { computed } from 'vue'
import { X } from '@lucide/vue'
import type { TaskFilters } from '../../lib/taskQuery'
import { useMembers } from '../../stores/members'
import { useTags } from '../../stores/tags'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import UiPopover from '../ui/UiPopover.vue'
import FilterSections from './FilterSections.vue'
import { FACET_LABEL, chipValues, clearFacet, valueLabel, type FacetKey } from './filterFacets'

const open = defineModel<boolean>('open', { default: false })

const props = defineProps<{ facet: FacetKey; modelValue: TaskFilters; listId: Uuid }>()

const emit = defineEmits<{ 'update:modelValue': [filters: TaskFilters]; removed: [] }>()

const workspaces = useWorkspaces()
const members = useMembers()
const tags = useTags()

const name = computed(() => FACET_LABEL[props.facet])
const values = computed(() => chipValues(props.modelValue, props.facet))
const tip = computed(() =>
  values.value
    .map((v) =>
      valueLabel(props.facet, v, {
        statusName: (id) => workspaces.statusById(id)?.name,
        memberName: (id) => members.nameOf(id),
        tagName: (id) => tags.get(id)?.name,
      }),
    )
    .join(', '),
)
const facets = computed(() => [props.facet])

function remove(): void {
  open.value = false
  emit('update:modelValue', clearFacet(props.modelValue, props.facet))
  emit('removed')
}
</script>

<template>
  <span class="facet-chip">
    <UiPopover v-model:open="open" :aria-label="`${name} filter`" :width="280">
      <template #trigger="{ props: t }">
        <button v-bind="t" v-tooltip="tip" type="button" class="main" data-facet-chip>
          <span class="text">{{ name }} · <span class="tabular">{{ values.length }}</span></span>
        </button>
      </template>
      <template #default>
        <FilterSections
          :model-value="modelValue"
          :list-id="listId"
          :facets="facets"
          @update:model-value="emit('update:modelValue', $event)"
        />
      </template>
    </UiPopover>
    <button v-tooltip="`Remove ${name} filter`" type="button" class="remove" :aria-label="`Remove ${name} filter`" @click="remove">
      <UiIcon :icon="X" :size="12" />
    </button>
  </span>
</template>

<style scoped>
/* The look of UiChip (md, selected): it is the popover's trigger, so it is built here. */
.facet-chip {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  height: var(--control-sm);
  border: 1px solid var(--accent);
  border-radius: var(--radius-full);
  background: var(--bg-selected);
  color: var(--accent-text);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
}

.main {
  display: inline-flex;
  align-items: center;
  height: 100%;
  min-width: 0;
  padding: 0 var(--space-1) 0 var(--space-3);
  border: 0;
  border-radius: var(--radius-full) 0 0 var(--radius-full);
  background: transparent;
  color: inherit;
  font: inherit;
  white-space: nowrap;
  cursor: pointer;
}

.main:hover,
.main[aria-expanded="true"] {
  background: color-mix(in srgb, var(--accent) 8%, transparent);
}

.text {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.remove {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 100%;
  margin-right: var(--space-0-5);
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: transparent;
  color: inherit;
  cursor: pointer;
}

.remove::before {
  content: "";
  position: absolute;
  width: 18px;
  height: 18px;
  border-radius: var(--radius-full);
  transition: background-color var(--dur-1) var(--ease-out);
}

.remove:hover::before {
  background: color-mix(in srgb, var(--accent) 14%, transparent);
}

.remove :deep(.ui-icon) {
  position: relative;
}
</style>

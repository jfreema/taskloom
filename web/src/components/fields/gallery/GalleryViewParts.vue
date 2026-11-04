<script setup lang="ts">
// /_ui: the view components of 7.12 on the sample list. ViewTabs (full, compact, segmented; the
// links go to the real list), ViewToolbar holding ViewSearch, FilterButton and FilterChips (the
// popover's "Me" and the "Assigned to me" chip are one value), and FilterHiddenNote.
import { computed, ref } from 'vue'
import { Eye } from '@lucide/vue'
import { activeFilterCount, emptyFilters, type TaskFilters } from '../../../lib/taskQuery'
import type { Uuid } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import UiIconButton from '../../ui/UiIconButton.vue'
import FilterButton from '../../view/FilterButton.vue'
import FilterChips from '../../view/FilterChips.vue'
import FilterHiddenNote from '../../view/FilterHiddenNote.vue'
import ViewSearch from '../../view/ViewSearch.vue'
import ViewTabs from '../../view/ViewTabs.vue'
import ViewToolbar from '../../view/ViewToolbar.vue'

defineProps<{ listId: Uuid }>()

const search = ref('')
const filters = ref<TaskFilters>(emptyFilters())
const hidden = ref(12)
const pressed = ref(true)
const count = computed(() => activeFilterCount(filters.value))
const summary = computed(() => {
  const f = filters.value
  const parts = [
    f.statusIds.length && `${f.statusIds.length} status`,
    f.assignees.length && `assignees ${f.assignees.join(', ')}`,
    f.priorities.length && `priority ${f.priorities.join(', ')}`,
    f.tagIds.length && `${f.tagIds.length} tag`,
    f.due.length && `due ${f.due.join(', ')}`,
  ].filter(Boolean)
  return parts.join(' · ') || 'none'
})
</script>

<template>
  <GalleryCard title="View tabs, toolbar and filters" wide>
    <div class="row tabs-row">
      <ViewTabs :list-id="listId" current="list" />
      <ViewTabs :list-id="listId" current="board" compact label="Views, compact" />
    </div>
    <div class="segmented">
      <ViewTabs :list-id="listId" current="calendar" segmented label="Views, segmented" />
    </div>
    <div class="toolbar-frame">
      <ViewToolbar>
        <template #start>
          <ViewSearch v-model="search" />
          <FilterButton v-model="filters" :list-id="listId" />
          <FilterChips v-model="filters" :list-id="listId" />
        </template>
        <template #end>
          <UiIconButton :icon="Eye" label="Show completed tasks" :pressed="pressed" @click="pressed = !pressed" />
        </template>
      </ViewToolbar>
    </div>
    <p class="small muted">Search: “{{ search }}” · Filters ({{ count }} active): {{ summary }}</p>
    <FilterHiddenNote :hidden-count="hidden" @clear="hidden = 0" />
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

<style scoped>
.tabs-row {
  gap: var(--space-6);
}

.segmented {
  max-width: 390px;
}

.toolbar-frame {
  min-width: 0;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  overflow: hidden;
}
</style>

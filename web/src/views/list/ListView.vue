<script setup lang="ts">
// List view (8.7). It scrolls itself (`data-own-scroll`), so the column header and the group
// headers stick to its top; the phone FAB's room and the bulk bar's room end the scroll content.
// - Toolbar controls (ListViewMenus) are teleported into the toolbar's end.
// - States: a skeleton after 200ms of a first load; guests' "No tasks yet"; "No tasks match these
//   filters." with "Clear filters" (and "Search all lists for “q”" while searching); otherwise the
//   table, then the "Filters are hiding N tasks" note. An empty list (Member+) shows the groups
//   with the first Not started group's add row open. A failed first load is ListPage's error state.
// - The bulk bar (B2) is rendered as is.
import { computed, ref } from 'vue'
import { List, ListFilter, Search } from '@lucide/vue'
import BulkBar from '../../components/bulk/BulkBar.vue'
import ListSkeleton from '../../components/list/ListSkeleton.vue'
import ListTable from '../../components/list/ListTable.vue'
import ListViewMenus from '../../components/list/ListViewMenus.vue'
import { useListModel } from '../../components/list/useListModel'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import FilterHiddenNote from '../../components/view/FilterHiddenNote.vue'
import { useAutoScroll } from '../../composables/useDnd'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useListPageContext } from '../../composables/useListPageContext'
import { useShortcut } from '../../composables/useShortcut'
import { activeFilterCount, emptyFilters } from '../../lib/taskQuery'
import { useSelection } from '../../stores/selection'
import { useUi } from '../../stores/ui'

const ctx = useListPageContext()
const selection = useSelection()
const ui = useUi()

const model = useListModel(ctx.canEdit)
const scroller = ref<HTMLElement | null>(null)
useAutoScroll(scroller)

const loading = computed(() => ctx.loading.value)
const showSkeleton = useDelayedFlag(loading)
const query = computed(() => ctx.search.value.trim())
const filtering = computed(() => activeFilterCount(ctx.filters.value) > 0 || query.value !== '')
const noTasks = computed(() => ctx.allTasks.value.length === 0)
const guestEmpty = computed(() => noTasks.value && !ctx.canEdit.value)
const filteredEmpty = computed(
  () => !noTasks.value && filtering.value && model.visibleCount.value === 0 && ctx.result.value.keptIds.size === 0,
)

function clearFilters(): void {
  ctx.filters.value = emptyFilters()
  ctx.search.value = ''
}

function searchAll(): void {
  ui.openPalette(query.value)
}

// Esc with focus in the page but outside the grid clears the selection (9.5, view scope).
useShortcut(
  'Escape',
  () => {
    if (selection.count === 0) return false
    selection.clear()
    return true
  },
  { scope: 'view' },
)
</script>

<template>
  <div
    ref="scroller"
    class="list-view"
    :class="{ 'has-bulk-bar': selection.count > 0 }"
    :aria-busy="loading ? 'true' : undefined"
    data-own-scroll
  >
    <Teleport defer :to="'#' + ctx.toolbarEndId">
      <ListViewMenus :model="model" />
    </Teleport>

    <template v-if="loading">
      <ListSkeleton v-if="showSkeleton" />
    </template>
    <div v-else-if="guestEmpty" class="state">
      <UiEmptyState :icon="List" title="No tasks yet" description="Tasks added to this list will show here." />
    </div>
    <div v-else-if="filteredEmpty" class="state">
      <UiEmptyState :icon="ListFilter" title="No tasks match these filters.">
        <template #actions>
          <UiButton variant="secondary" @click="clearFilters">Clear filters</UiButton>
          <UiButton v-if="query" variant="ghost" :icon="Search" @click="searchAll">Search all lists for “{{ query }}”</UiButton>
        </template>
      </UiEmptyState>
    </div>
    <template v-else>
      <ListTable :model="model" :empty-list="noTasks" />
      <FilterHiddenNote class="hidden-note" :hidden-count="ctx.result.value.hiddenCount" @clear="clearFilters" />
    </template>

    <div class="end-room" aria-hidden="true" />
    <BulkBar />
  </div>
</template>

<style scoped>
.list-view {
  position: relative;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: auto;
  overscroll-behavior: contain;
  /* Keyboard focus never lands under the sticky headers or the bulk bar (9.6, WCAG 2.4.11). */
  scroll-padding-top: calc(32px + 40px);
  scroll-padding-bottom: var(--fab-room, 0px);
}

.list-view.has-bulk-bar {
  scroll-padding-bottom: calc(var(--fab-room, 0px) + 48px + var(--space-8));
}

.state {
  display: flex;
  justify-content: center;
  padding: var(--space-12) var(--space-4);
}

.hidden-note {
  padding-left: calc(40px + 20px + var(--space-1-5));
}

/* The end of the scroll content: room for the FAB on phones and for the bulk bar. */
.end-room {
  height: calc(var(--fab-room, 0px) + var(--space-16));
}

.has-bulk-bar .end-room {
  height: calc(var(--fab-room, 0px) + 48px + var(--space-16));
}

@container page (width < 560px) {
  .list-view {
    scroll-padding-top: 40px;
  }

  .hidden-note {
    padding-left: var(--space-4);
  }
}
</style>

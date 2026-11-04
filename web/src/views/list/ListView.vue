<script setup lang="ts">
// List view (spec 8.7). Placeholder until B1 builds it: plain rows with a selection checkbox (the
// selection store) and a title button that opens the task panel. It registers the row order with
// useTaskPanel().setOrder.
import { computed, watch } from 'vue'
import { List } from '@lucide/vue'
import { useListPageContext } from '../../composables/useListPageContext'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useSelection } from '../../stores/selection'
import UiCheckbox from '../../components/ui/UiCheckbox.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'

const ctx = useListPageContext()
const panel = useTaskPanel()
const selection = useSelection()

const rows = computed(() => ctx.allTasks.value.filter((t) => !t.parentTaskId && ctx.result.value.visibleIds.has(t.id)))

watch(rows, (list) => panel.setOrder(list.map((t) => t.id)), { immediate: true })
</script>

<template>
  <div class="list-view">
    <ul v-if="rows.length" class="rows" :aria-label="`${ctx.list.value?.name ?? 'List'} tasks`">
      <li v-for="t in rows" :key="t.id" class="row" data-row :data-task-row="t.id">
        <UiCheckbox
          :model-value="selection.has(t.id)"
          :aria-label="`Select ${t.title}`"
          @update:model-value="selection.toggle(t.id)"
        />
        <button type="button" class="title focus-inset" @click="panel.open(t.id)">
          <span class="truncate">{{ t.title }}</span>
        </button>
      </li>
    </ul>
    <UiEmptyState v-else-if="!ctx.loading.value" :icon="List" title="No tasks here yet" />
  </div>
</template>

<style scoped>
.list-view {
  padding: var(--space-2) var(--space-4);
}

.rows {
  margin: 0;
  padding: 0;
  list-style: none;
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-list);
  padding: 0 var(--space-2);
  border-bottom: 1px solid var(--border-subtle);
}

.title {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  min-height: var(--control-md);
  padding: 0 var(--space-1);
  border-radius: var(--radius-sm);
  text-align: left;
  color: var(--text-primary);
}

.title:hover {
  color: var(--accent-text);
}
</style>

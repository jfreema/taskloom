<script setup lang="ts">
// Where the task lives (8.10): the space tile and name, "/", the list (a link to it) and, for a
// subtask, "/ <parent title>", which opens the parent. Long names truncate with a tooltip.
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task } from '../../types'
import SpaceTile from '../fields/SpaceTile.vue'

const props = defineProps<{ task: Task; parentTitle: string | null }>()
const emit = defineEmits<{ 'open-parent': [] }>()

const workspaces = useWorkspaces()
const loc = computed(() => workspaces.findList(props.task.listId))
</script>

<template>
  <nav class="task-breadcrumb" aria-label="Breadcrumb">
    <template v-if="loc">
      <SpaceTile class="tile" :space="loc.space" :size="18" />
      <span v-tooltip="{ text: loc.space.name, overflowOnly: true }" class="crumb space truncate">{{ loc.space.name }}</span>
      <span class="sep" aria-hidden="true">/</span>
      <RouterLink v-tooltip="{ text: loc.list.name, overflowOnly: true }" class="crumb link truncate" :to="`/l/${loc.list.id}`">
        {{ loc.list.name }}
      </RouterLink>
    </template>
    <template v-if="task.parentTaskId && parentTitle">
      <span class="sep" aria-hidden="true">/</span>
      <button
        v-tooltip="{ text: parentTitle, overflowOnly: true }"
        type="button"
        class="crumb link parent truncate"
        @click="emit('open-parent')"
      >
        {{ parentTitle }}
      </button>
    </template>
  </nav>
</template>

<style scoped>
.task-breadcrumb {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.tile {
  flex-shrink: 0;
}

.crumb {
  flex: 0 1 auto;
  min-width: 0;
}

/* The space name gives way first, the list name last. */
.space {
  flex-shrink: 4;
  min-width: 2ch;
}

.sep {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.link {
  padding: var(--space-0-5) var(--space-1);
  margin: 0 calc(var(--space-1) * -1);
  border: 0;
  border-radius: var(--radius-xs);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  text-decoration: none;
  cursor: pointer;
}

.link:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.parent {
  flex-shrink: 2;
}

/* Touch (7.5): the links are 44px targets. They truncate (overflow: hidden would clip an ::after),
   so their padding grows instead and negative margins keep the row's height. */
@media (pointer: coarse) {
  .link {
    padding-block: calc((var(--touch-min) - var(--lh-sm)) / 2);
    margin-block: calc((var(--touch-min) - var(--lh-sm)) / -2 + var(--space-0-5));
  }
}
</style>

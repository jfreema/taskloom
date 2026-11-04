<script setup lang="ts">
// The panel header (8.10), 48px: the breadcrumb (Back on the phone sheet), then Previous task {K},
// Next task {J}, Copy link, Open full page, More task actions and Close task {Esc}.
import { ArrowLeft, ChevronDown, ChevronUp, Link as LinkIcon, Maximize2, X } from '@lucide/vue'
import { useRouter } from 'vue-router'
import { useTaskActions } from '../../composables/useTaskActions'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { noop } from '../../lib/async'
import type { Task, Uuid } from '../../types'
import UiIconButton from '../ui/UiIconButton.vue'
import TaskActionsMenu from './TaskActionsMenu.vue'
import TaskBreadcrumb from './TaskBreadcrumb.vue'

const props = defineProps<{
  taskId: Uuid
  task: Task | undefined
  parentTitle: string | null
  mode: 'docked' | 'modal' | 'sheet'
  canEdit: boolean
}>()
const emit = defineEmits<{ close: []; 'open-parent': []; delete: [] }>()

const panel = useTaskPanel()
const actions = useTaskActions()
const router = useRouter()

function openFullPage(): void {
  void router.push(`/t/${props.taskId}`).catch(noop)
}
</script>

<template>
  <header class="td-header" :class="`mode-${mode}`">
    <UiIconButton v-if="mode === 'sheet'" :icon="ArrowLeft" label="Back" size="lg" @click="emit('close')" />
    <div v-else class="crumbs">
      <TaskBreadcrumb v-if="task" :task="task" :parent-title="parentTitle" @open-parent="emit('open-parent')" />
    </div>
    <div class="actions">
      <UiIconButton
        :icon="ChevronUp"
        label="Previous task"
        kbd="K"
        :size="mode === 'sheet' ? 'lg' : 'md'"
        :disabled="!panel.hasPrev.value"
        @click="panel.prev()"
      />
      <UiIconButton
        :icon="ChevronDown"
        label="Next task"
        kbd="J"
        :size="mode === 'sheet' ? 'lg' : 'md'"
        :disabled="!panel.hasNext.value"
        @click="panel.next()"
      />
      <template v-if="task">
        <UiIconButton :icon="LinkIcon" label="Copy link" :size="mode === 'sheet' ? 'lg' : 'md'" @click="actions.copyLink(taskId)" />
        <UiIconButton :icon="Maximize2" label="Open full page" :size="mode === 'sheet' ? 'lg' : 'md'" @click="openFullPage" />
      </template>
      <TaskActionsMenu v-if="task && canEdit" :task="task" :size="mode === 'sheet' ? 'lg' : 'md'" @delete="emit('delete')" />
      <UiIconButton v-if="mode !== 'sheet'" :icon="X" label="Close task" kbd="Escape" size="md" @click="emit('close')" />
    </div>
  </header>
</template>

<style scoped>
.td-header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
  height: var(--topbar-h);
  padding: 0 var(--space-2) 0 var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}

.td-header.mode-sheet {
  height: var(--appbar-h);
  padding: 0 var(--space-1-5);
}

.crumbs {
  display: flex;
  flex: 1;
  min-width: 0;
}

.mode-sheet .actions {
  margin-left: auto;
}

.actions {
  display: flex;
  align-items: center;
  gap: var(--space-0-5);
  flex-shrink: 0;
}
</style>

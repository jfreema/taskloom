<script setup lang="ts">
// Task detail (spec 8.10). Placeholder until B4 builds it: the title, the status pill, the close
// button (not in page mode) and the visually hidden heading that names the host (titleId).
import { computed, ref, watch } from 'vue'
import { FileText, X } from '@lucide/vue'
import { ApiError } from '../../api'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'

const props = defineProps<{ taskId: Uuid; mode: 'docked' | 'modal' | 'sheet' | 'page'; titleId: string }>()
const emit = defineEmits<{ close: [] }>()

const tasks = useTasks()
const workspaces = useWorkspaces()
const missing = ref(false)

const task = computed(() => tasks.get(props.taskId))
const status = computed(() => (task.value ? workspaces.statusById(task.value.statusId) : undefined))

watch(
  () => props.taskId,
  async (id) => {
    missing.value = false
    try {
      await tasks.fetchDetail(id)
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) missing.value = true
    }
  },
  { immediate: true },
)
</script>

<template>
  <div class="task-detail" :class="`mode-${mode}`">
    <h2 :id="titleId" class="sr-only">{{ task?.title ?? 'Task' }}</h2>
    <header v-if="mode !== 'page'" class="header">
      <UiIconButton :icon="X" label="Close task" kbd="Escape" size="md" @click="emit('close')" />
    </header>
    <div class="body">
      <UiEmptyState v-if="missing" :icon="FileText" title="This task doesn't exist or was deleted.">
        <template #actions>
          <UiButton v-if="mode === 'page'" to="/">Go to Home</UiButton>
          <UiButton v-else @click="emit('close')">Close</UiButton>
        </template>
      </UiEmptyState>
      <template v-else-if="task">
        <span v-if="status" class="status tint-pill" :style="{ '--c': status.color }">
          <span class="dot" aria-hidden="true" />{{ status.name }}
        </span>
        <component :is="mode === 'page' ? 'h1' : 'p'" class="title">{{ task.title }}</component>
      </template>
      <template v-else>
        <UiSkeleton width="96px" height="24px" variant="rect" />
        <UiSkeleton :lines="2" />
      </template>
    </div>
  </div>
</template>

<style scoped>
.task-detail {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.header {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  height: var(--topbar-h);
  padding: 0 var(--space-3);
  border-bottom: 1px solid var(--border-subtle);
  flex-shrink: 0;
}

.body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-3);
  padding: var(--space-5) var(--space-6);
}

.status {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  height: var(--control-sm);
  padding: 0 var(--space-2);
  border-radius: var(--radius-full);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: var(--radius-full);
  background: var(--c);
}

.title {
  font-size: var(--fs-2xl);
  line-height: var(--lh-2xl);
  font-weight: var(--fw-semibold);
  overflow-wrap: anywhere;
}
</style>

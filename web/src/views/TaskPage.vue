<script setup lang="ts">
// The full-page task /t/:taskId (spec 3.2, 8.10). Placeholder until B4 builds the page: it renders
// TaskDetail in page mode with its own title id. A task of another workspace the user is in
// switches to that workspace first (useWorkspaceSwitch, navigate: false), then renders.
import { ref, watch } from 'vue'
import { api } from '../api'
import { useWorkspaceSwitch } from '../composables/useWorkspaceSwitch'
import { useWorkspaces } from '../stores/workspaces'
import type { TaskDetail as TaskDetailData, Uuid } from '../types'
import TaskDetail from '../components/task/TaskDetail.vue'

const props = defineProps<{ taskId: Uuid }>()

const workspaces = useWorkspaces()
const { switchTo } = useWorkspaceSwitch()
/** The task id whose workspace is settled (switched, or the same); TaskDetail renders for it. */
const readyFor = ref<Uuid | null>(null)

watch(
  () => [props.taskId, workspaces.loaded] as const,
  async ([id, loaded]) => {
    if (!loaded || readyFor.value === id) return
    readyFor.value = null
    try {
      const detail = await api.get<TaskDetailData>(`/api/tasks/${id}`)
      if (props.taskId !== id) return
      const known = workspaces.list.some((w) => w.id === detail.workspaceId)
      if (known && detail.workspaceId !== workspaces.currentId) await switchTo(detail.workspaceId, { navigate: false })
    } catch {
      // Missing or unreachable: TaskDetail shows its own state.
    }
    if (props.taskId === id) readyFor.value = id
  },
  { immediate: true },
)
</script>

<template>
  <div class="task-page">
    <TaskDetail v-if="readyFor === taskId" :task-id="taskId" mode="page" title-id="task-page-title" />
  </div>
</template>

<style scoped>
.task-page {
  width: 100%;
  max-width: var(--reading-w);
  margin: 0 auto;
  padding: var(--space-6);
}
</style>

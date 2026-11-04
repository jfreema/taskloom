<script setup lang="ts">
// The full-page task /t/:taskId (3.2, 8.10): the page header holds the breadcrumb, Copy link and
// More task actions; below it TaskDetail (page mode) is one centred column with the composer sticky
// at the bottom. A task already in the store renders at once. Otherwise the page looks the task up
// first: a task of another workspace the user is in switches to that workspace (8.2), then renders
// from the detail it fetched (no second request); a 404 shows the not-found state.
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { CircleAlert, Link as LinkIcon } from '@lucide/vue'
import { api } from '../api'
import { useTaskActions } from '../composables/useTaskActions'
import { useWorkspaceSwitch } from '../composables/useWorkspaceSwitch'
import { errorStatus, noop } from '../lib/async'
import { useSession } from '../stores/session'
import { useTasks } from '../stores/tasks'
import { useWorkspaces } from '../stores/workspaces'
import type { TaskDetail as TaskDetailData, Uuid } from '../types'
import PageHeader from '../components/shell/PageHeader.vue'
import TaskActionsMenu from '../components/task/TaskActionsMenu.vue'
import TaskBreadcrumb from '../components/task/TaskBreadcrumb.vue'
import TaskDetail from '../components/task/TaskDetail.vue'
import { cacheDetail } from '../components/task/taskDetailState'
import UiButton from '../components/ui/UiButton.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import UiIconButton from '../components/ui/UiIconButton.vue'

const props = defineProps<{ taskId: Uuid }>()

type State = 'loading' | 'ready' | 'missing' | 'error'

const tasks = useTasks()
const workspaces = useWorkspaces()
const session = useSession()
const actions = useTaskActions()
const router = useRouter()
const { switchTo } = useWorkspaceSwitch()

const state = ref<State>('loading')
/** The parent's title from the lookup, for a subtask the store has no parent row for. */
const lookedUpParent = ref<{ id: Uuid; title: string } | null>(null)
let gen = 0

const task = computed(() => tasks.get(props.taskId))
const canEdit = computed(() => (task.value ? workspaces.canEdit(workspaces.findList(task.value.listId)?.workspace.id) : false))
const parentTitle = computed(() => {
  const parentId = task.value?.parentTaskId
  if (!parentId) return null
  return tasks.get(parentId)?.title ?? tasks.refs[parentId]?.title ?? (lookedUpParent.value?.id === parentId ? lookedUpParent.value.title : null)
})

async function resolve(id: Uuid): Promise<void> {
  const mine = ++gen
  if (tasks.get(id) && !tasks.isPendingDelete(id)) {
    state.value = 'ready'
    return
  }
  state.value = 'loading'
  try {
    const d = await api.get<TaskDetailData>(`/api/tasks/${id}`)
    if (mine !== gen) return
    if (d.workspaceId !== workspaces.currentId) {
      if (!workspaces.list.some((w) => w.id === d.workspaceId)) await workspaces.refresh().catch(noop)
      if (mine !== gen) return
      if (!workspaces.list.some((w) => w.id === d.workspaceId)) {
        state.value = 'missing'
        return
      }
      await switchTo(d.workspaceId, { navigate: false })
      if (mine !== gen) return
    }
    if (d.workspaceId === workspaces.currentId) {
      tasks.upsert([d.task, ...d.subtasks])
      cacheDetail(session.user?.id, d)
    }
    lookedUpParent.value = d.parent ? { id: d.parent.id, title: d.parent.title } : null
    state.value = 'ready'
  } catch (err) {
    if (mine !== gen) return
    const status = errorStatus(err)
    state.value = status === 404 || status === 403 ? 'missing' : 'error'
  }
}

watch(
  () => [props.taskId, workspaces.loaded] as const,
  ([id, loaded]) => {
    if (loaded) void resolve(id)
  },
  { immediate: true },
)

function openParent(): void {
  const parentId = task.value?.parentTaskId
  if (parentId) void router.push(`/t/${parentId}`).catch(noop)
}

function deleteTask(): void {
  const t = task.value
  if (!t) return
  tasks.remove([t.id])
  void router.push(`/l/${t.listId}`).catch(noop)
}
</script>

<template>
  <div class="task-page" data-task-drop-zone>
    <PageHeader :title="state === 'missing' ? 'Task not found' : (task?.title ?? 'Task')" title-tag="p" :loading="state === 'loading'">
      <TaskBreadcrumb v-if="task && state === 'ready'" :task="task" :parent-title="parentTitle" @open-parent="openParent" />
      <template v-if="task && state === 'ready'" #actions>
        <UiIconButton :icon="LinkIcon" label="Copy link" @click="actions.copyLink(task.id)" />
        <TaskActionsMenu v-if="canEdit" :task="task" @delete="deleteTask" />
      </template>
      <template v-if="task && state === 'ready'" #below>
        <div class="phone-bar">
          <TaskBreadcrumb class="phone-crumbs" :task="task" :parent-title="parentTitle" @open-parent="openParent" />
          <UiIconButton :icon="LinkIcon" label="Copy link" size="lg" @click="actions.copyLink(task.id)" />
          <TaskActionsMenu v-if="canEdit" :task="task" size="lg" @delete="deleteTask" />
        </div>
      </template>
    </PageHeader>
    <TaskDetail v-if="state === 'ready'" :task-id="taskId" mode="page" title-id="task-page-title" />
    <div v-else-if="state === 'missing'" class="state">
      <h1 class="sr-only">Task not found</h1>
      <UiEmptyState :icon="CircleAlert" title="This task doesn't exist or you don't have access.">
        <template #actions>
          <UiButton to="/">Go to Home</UiButton>
        </template>
      </UiEmptyState>
    </div>
    <div v-else-if="state === 'error'" class="state">
      <h1 class="sr-only">Task</h1>
      <UiErrorState title="Couldn't load this task" :retry="() => resolve(taskId)" />
    </div>
  </div>
</template>

<style scoped>
.task-page {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.state {
  margin: auto;
  padding: var(--space-8) var(--space-4);
}

/* PageHeader's below row has 16px sides already; the icon buttons line up with the app bar's. */
.phone-bar {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  min-height: var(--toolbar-h);
  margin-right: calc(var(--space-1-5) - var(--space-4));
}

.phone-crumbs {
  flex: 1;
}
</style>

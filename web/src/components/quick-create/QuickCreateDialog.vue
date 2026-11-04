<script setup lang="ts">
// Quick create (spec 8.12). Placeholder until B5 builds it: the title field and "Create task",
// which calls tasks.create with the defaults rule of 8.12 (list: defaults, else the current list
// page, else the last list used, else the first list; status: defaults, else the list's default).
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useUi } from '../../stores/ui'
import { useTasks } from '../../stores/tasks'
import { useRecents } from '../../stores/recents'
import { useWorkspaces } from '../../stores/workspaces'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useToast } from '../../composables/useToast'
import type { CreateTaskRequest } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiInput from '../ui/UiInput.vue'

const ui = useUi()
const tasks = useTasks()
const recents = useRecents()
const workspaces = useWorkspaces()
const panel = useTaskPanel()
const toast = useToast()
const route = useRoute()
const router = useRouter()

const title = ref('')
const busy = ref(false)

const open = computed({
  get: () => ui.quickCreate.open,
  set: (v: boolean) => {
    if (!v) ui.closeQuickCreate()
  },
})

const defaults = computed(() => ui.quickCreate.defaults)

const listId = computed(() => {
  const known = (id: string | null | undefined) => (id && workspaces.findList(id) ? id : null)
  return (
    known(defaults.value.listId) ??
    known(ui.createDefaults?.listId) ??
    known(recents.lastListId) ??
    workspaces.allLists[0]?.list.id ??
    null
  )
})

watch(
  () => ui.quickCreate.open,
  (on) => {
    if (on) title.value = defaults.value.title ?? ''
  },
  { immediate: true },
)

async function create(): Promise<void> {
  const name = title.value.trim()
  const target = listId.value
  if (!name || !target || busy.value) return
  const d = defaults.value
  const statusId = d.statusId && workspaces.statusById(d.statusId)?.listId === target ? d.statusId : workspaces.defaultStatusFor(target)?.id
  const request: CreateTaskRequest = {
    title: name,
    statusId,
    parentTaskId: d.parentTaskId,
    assigneeIds: d.assigneeIds,
    tagIds: d.tagIds,
    priority: d.priority,
    startDate: d.startDate,
    dueDate: d.dueDate,
  }
  busy.value = true
  try {
    const task = await tasks.create(target, request)
    recents.setLastList(target)
    ui.closeQuickCreate()
    toast.show({
      message: 'Task created',
      action: {
        label: 'Open',
        run: () => {
          if (route.name === 'home' || route.name === 'list') panel.open(task.id)
          else void router.push(`/t/${task.id}`)
        },
      },
    })
  } catch (err) {
    toast.error(err)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <UiDialog v-model:open="open" title="New task" initial-focus="[data-quick-create-title]">
    <UiInput
      v-model="title"
      data-quick-create-title
      size="lg"
      placeholder="Task title"
      aria-label="Task title"
      :maxlength="500"
      @enter="create"
    />
    <template #footer>
      <UiButton @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" kbd="Enter" :disabled="!title.trim() || !listId" :loading="busy" @click="create">
        Create task
      </UiButton>
    </template>
  </UiDialog>
</template>

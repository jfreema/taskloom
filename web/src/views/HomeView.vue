<script setup lang="ts">
// Home / My Work (spec 8.5). Placeholder until B5 builds the page: the greeting as the h1, and the
// caller's tasks ("mine") as title buttons that open the task panel.
import { computed, ref, watch } from 'vue'
import { CircleCheck } from '@lucide/vue'
import { useSession } from '../stores/session'
import { useWorkspaces } from '../stores/workspaces'
import { useTasks } from '../stores/tasks'
import { useTaskPanel } from '../composables/useTaskPanel'
import type { Uuid } from '../types'
import PageHeader from '../components/shell/PageHeader.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'

const session = useSession()
const workspaces = useWorkspaces()
const tasks = useTasks()
const panel = useTaskPanel()

const ids = ref<Uuid[]>([])
const loading = ref(false)
const error = ref<unknown>(null)

const greeting = computed(() => {
  const h = new Date().getHours()
  const part = h < 12 ? 'morning' : h < 18 ? 'afternoon' : 'evening'
  const first = session.user?.displayName.trim().split(/\s+/)[0]
  return first ? `Good ${part}, ${first}` : `Good ${part}`
})

const rows = computed(() =>
  ids.value.map((id) => tasks.get(id)).filter((t): t is NonNullable<typeof t> => !!t && !tasks.isPendingDelete(t.id)),
)

async function load(wsId: Uuid | null | undefined): Promise<void> {
  if (!wsId) return
  loading.value = true
  error.value = null
  try {
    ids.value = await tasks.loadWorkspaceTasks(wsId, { scope: 'mine' })
  } catch (err) {
    error.value = err
  } finally {
    loading.value = false
  }
}

watch(() => workspaces.current?.id, (id) => void load(id), { immediate: true })
watch(rows, (list) => panel.setOrder(list.map((t) => t.id)), { immediate: true })
</script>

<template>
  <PageHeader bare />
  <div class="home">
    <h1 class="greeting">{{ greeting }}</h1>
    <UiErrorState v-if="error" :retry="() => load(workspaces.current?.id)" />
    <div v-else-if="loading && !rows.length" class="skeletons">
      <UiSkeleton v-for="n in 5" :key="n" width="60%" />
    </div>
    <ul v-else-if="rows.length" class="tasks">
      <li v-for="t in rows" :key="t.id" data-row>
        <button type="button" class="task focus-inset" :data-task-row="t.id" @click="panel.open(t.id)">
          <span class="truncate">{{ t.title }}</span>
        </button>
      </li>
    </ul>
    <UiEmptyState v-else :icon="CircleCheck" title="Nothing on your plate" description="Tasks assigned to you show up here." />
  </div>
</template>

<style scoped>
.home {
  width: 100%;
  max-width: var(--reading-w);
  margin: 0 auto;
  padding: var(--space-8) var(--space-6);
}

.greeting {
  margin-bottom: var(--space-6);
  font-size: var(--fs-2xl);
  line-height: var(--lh-2xl);
  font-weight: var(--fw-semibold);
}

.skeletons {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.tasks {
  margin: 0;
  padding: 0;
  list-style: none;
}

.task {
  display: flex;
  align-items: center;
  width: 100%;
  min-height: var(--row-list);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  text-align: left;
  color: var(--text-primary);
}

.task:hover {
  background: var(--bg-hover);
}
</style>

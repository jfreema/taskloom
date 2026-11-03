<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useList } from '../stores/list'
import { useWorkspaces } from '../stores/workspaces'
import { useSession } from '../stores/session'
import ListView from '../components/ListView.vue'
import BoardView from '../components/BoardView.vue'
import TaskDrawer from '../components/TaskDrawer.vue'
import type { Task } from '../types'

const props = defineProps<{ listId: string; view?: string }>()
const route = useRoute()
const router = useRouter()
const list = useList()
const ws = useWorkspaces()
const session = useSession()

const search = ref('')
const onlyMine = ref(false)
const hideDone = ref(false)

watch(() => props.listId, (id) => list.open(id), { immediate: true })

const current = computed(() => ws.findList(props.listId))
const view = computed(() => (props.view === 'board' ? 'board' : 'list'))
const openTaskId = computed(() => (route.query.task as string) || null)

const filter = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (t: Task) =>
    (!q || t.title.toLowerCase().includes(q)) &&
    (!onlyMine.value || t.assigneeId === session.user?.id) &&
    (!hideDone.value || list.statusById(t.statusId)?.category !== 'Done')
})

const openTask = (id: string) => router.replace({ query: { ...route.query, task: id } })
const closeTask = () => {
  const { task: _drop, ...rest } = route.query
  router.replace({ query: rest })
}

async function quickAdd() {
  const t = await list.createTask('New task')
  if (t) openTask(t.id)
}
</script>

<template>
  <header class="page-head">
    <div class="crumbs row">
      <span class="faint">{{ current?.space.name }}</span>
      <span class="faint">/</span>
      <h1>{{ current?.list.name ?? list.detail?.name }}</h1>
    </div>
    <nav class="tabs row">
      <RouterLink :to="{ name: 'list', params: { listId, view: 'list' }, query: route.query }" :class="{ active: view === 'list' }">☰ List</RouterLink>
      <RouterLink :to="{ name: 'list', params: { listId, view: 'board' }, query: route.query }" :class="{ active: view === 'board' }">▦ Board</RouterLink>
      <span class="spacer" />
      <input v-model="search" class="search" placeholder="Search tasks" />
      <label class="toggle"><input v-model="onlyMine" type="checkbox" /> Me</label>
      <label class="toggle"><input v-model="hideDone" type="checkbox" /> Hide done</label>
      <button class="primary" @click="quickAdd">＋ Task</button>
    </nav>
  </header>

  <div v-if="list.error" class="empty">Couldn't open this list: {{ list.error }}</div>
  <div v-else-if="list.loading && !list.detail" class="empty">Loading…</div>
  <template v-else-if="list.detail">
    <ListView v-if="view === 'list'" :filter="filter" @open="openTask" />
    <BoardView v-else :filter="filter" @open="openTask" />
  </template>

  <TaskDrawer v-if="openTaskId && list.detail" :task-id="openTaskId" @close="closeTask" @open="openTask" />
</template>

<style scoped>
.page-head { padding: 14px 20px 0; border-bottom: 1px solid var(--border); }
.crumbs h1 { font-size: 18px; margin: 0; }
.tabs { margin-top: 10px; gap: 4px; flex-wrap: wrap; padding-bottom: 8px; }
.tabs a { padding: 6px 10px; border-radius: 6px; color: var(--text-muted); text-decoration: none; font-weight: 600; border-bottom: 2px solid transparent; }
.tabs a:hover { background: var(--bg-hover); }
.tabs a.active { color: var(--accent); border-bottom-color: var(--accent); border-radius: 6px 6px 0 0; }
.search { width: 180px; padding: 5px 8px; }
.toggle { display: inline-flex; align-items: center; gap: 4px; color: var(--text-muted); font-size: 13px; padding: 0 6px; cursor: pointer; }
</style>

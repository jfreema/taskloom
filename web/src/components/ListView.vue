<script setup lang="ts">
import { ref } from 'vue'
import { useList } from '../stores/list'
import TaskRow from './TaskRow.vue'
import InlineInput from './InlineInput.vue'
import type { Task } from '../types'

const props = defineProps<{ filter: (t: Task) => boolean }>()
const emit = defineEmits<{ open: [taskId: string] }>()
const list = useList()
const collapsed = ref<Record<string, boolean>>({})
const addingIn = ref<string | null>(null)

async function add(statusId: string, title: string) {
  await list.createTask(title, statusId)
}

const visible = (statusId: string) => list.tasksIn(statusId).filter(props.filter)
</script>

<template>
  <div class="list-view">
    <section v-for="status in list.statuses" :key="status.id" class="group">
      <header class="group-head row">
        <button class="caret" @click="collapsed[status.id] = !collapsed[status.id]">{{ collapsed[status.id] ? '▸' : '▾' }}</button>
        <span class="group-pill" :style="{ background: status.color }">{{ status.name }}</span>
        <span class="faint">{{ visible(status.id).length }}</span>
        <button class="faint add-btn" @click="addingIn = status.id; collapsed[status.id] = false">＋ Add task</button>
      </header>
      <template v-if="!collapsed[status.id]">
        <div class="cols">
          <span>Name</span><span>Assignee</span><span>Due date</span><span>Priority</span>
        </div>
        <TaskRow v-for="t in visible(status.id)" :key="t.id" :task="t" @open="(id) => emit('open', id)" />
        <div v-if="addingIn === status.id" class="adding">
          <InlineInput multi placeholder="Task name, then Enter" @submit="(title) => add(status.id, title)" @cancel="addingIn = null" />
        </div>
        <button v-else class="faint new-row" @click="addingIn = status.id">＋ Add task</button>
      </template>
    </section>
  </div>
</template>

<style scoped>
.list-view { padding: 8px 20px 40px; overflow-y: auto; flex: 1; }
.group { margin-top: 16px; }
.group-head { padding: 4px 0; }
.caret { width: 20px; padding: 0; color: var(--text-faint); font-size: 11px; }
.group-pill { color: #fff; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.03em; padding: 3px 8px; border-radius: 4px; }
.add-btn { font-size: 13px; }
.cols { display: grid; grid-template-columns: minmax(200px, 1fr) 110px 120px 90px; padding: 6px 12px 6px 52px; font-size: 12px; color: var(--text-faint); border-bottom: 1px solid var(--border); }
.adding { padding: 6px 12px 6px 52px; border-bottom: 1px solid var(--border); }
.adding input { width: 100%; max-width: 480px; }
.new-row { margin: 4px 0 0 40px; font-size: 13px; }
@media (max-width: 720px) { .cols { display: none; } .list-view { padding: 8px 8px 40px; } }
</style>

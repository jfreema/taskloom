<script setup lang="ts">
import { ref } from 'vue'
import { useList } from '../stores/list'
import InlineInput from './InlineInput.vue'
import Avatar from './Avatar.vue'
import DueDate from './DueDate.vue'
import PriorityPicker from './PriorityPicker.vue'
import type { Task } from '../types'

const props = defineProps<{ filter: (t: Task) => boolean }>()
const emit = defineEmits<{ open: [taskId: string] }>()
const list = useList()

const addingIn = ref<string | null>(null)
const addingStatus = ref(false)
const dragId = ref<string | null>(null)
const dropTarget = ref<{ statusId: string; index: number } | null>(null)

const visible = (statusId: string) => list.tasksIn(statusId).filter(props.filter)

function onDragStart(e: DragEvent, task: Task) {
  dragId.value = task.id
  e.dataTransfer?.setData('text/plain', task.id)
  if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move'
}

/** Work out where in the column the card would land, from the pointer's height over the cards. */
function onDragOver(e: DragEvent, statusId: string) {
  if (!dragId.value) return
  e.preventDefault()
  const column = e.currentTarget as HTMLElement
  const cards = [...column.querySelectorAll<HTMLElement>('[data-card]')].filter((c) => c.dataset.card !== dragId.value)
  let index = cards.length
  for (let i = 0; i < cards.length; i++) {
    const r = cards[i]!.getBoundingClientRect()
    if (e.clientY < r.top + r.height / 2) {
      index = i
      break
    }
  }
  dropTarget.value = { statusId, index }
}

async function onDrop(statusId: string) {
  const id = dragId.value
  const target = dropTarget.value
  dragId.value = null
  dropTarget.value = null
  if (!id || !target) return
  const others = visible(statusId).filter((t) => t.id !== id)
  const before = others[target.index - 1]?.id ?? null
  const after = others[target.index]?.id ?? null
  await list.moveTask(id, statusId, before, after)
}

function onDragEnd() {
  dragId.value = null
  dropTarget.value = null
}

async function add(statusId: string, title: string) {
  await list.createTask(title, statusId)
}

const palette = ['#4194f6', '#a875ff', '#f5a623', '#e5484d', '#0c9ca6', '#d6409f']
async function addStatus(name: string) {
  addingStatus.value = false
  await list.addStatus(name, palette[list.statuses.length % palette.length]!)
}

const subtaskInfo = (t: Task) => {
  const subs = list.subtasksOf(t.id)
  return subs.length ? `${subs.filter((s) => list.statusById(s.statusId)?.category === 'Done').length}/${subs.length}` : ''
}
</script>

<template>
  <div class="board">
    <section
      v-for="status in list.statuses"
      :key="status.id"
      class="column"
      :style="{ '--col': status.color }"
      @dragover="(e) => onDragOver(e, status.id)"
      @drop.prevent="onDrop(status.id)"
    >
      <header class="col-head row">
        <span class="col-pill">{{ status.name }}</span>
        <span class="faint">{{ visible(status.id).length }}</span>
        <span class="spacer" />
        <button class="faint" title="Add task" @click="addingIn = status.id">＋</button>
      </header>
      <div class="cards">
        <template v-for="(t, i) in visible(status.id)" :key="t.id">
          <div v-if="dropTarget?.statusId === status.id && dropTarget.index === i && dragId !== t.id" class="drop-line" />
          <article
            class="card"
            :class="{ dragging: dragId === t.id }"
            :data-card="t.id"
            draggable="true"
            @dragstart="(e) => onDragStart(e, t)"
            @dragend="onDragEnd"
            @click="emit('open', t.id)"
          >
            <div class="card-title" :class="{ done: status.category === 'Done' }">{{ t.title }}</div>
            <div class="card-meta row" @click.stop>
              <Avatar v-if="list.memberById(t.assigneeId)" :name="list.memberById(t.assigneeId)!.displayName" :size="20" />
              <DueDate v-if="t.dueDate" :date="t.dueDate" :done="status.category === 'Done'" @change="(dueDate) => list.updateTask(t.id, { dueDate })" />
              <PriorityPicker v-if="t.priority !== 'None'" :priority="t.priority" @change="(priority) => list.updateTask(t.id, { priority })" />
              <span v-if="subtaskInfo(t)" class="faint small">⑂ {{ subtaskInfo(t) }}</span>
            </div>
          </article>
        </template>
        <div v-if="dropTarget?.statusId === status.id && dropTarget.index >= visible(status.id).filter((t) => t.id !== dragId).length" class="drop-line" />
        <div v-if="addingIn === status.id" class="card adding">
          <InlineInput multi placeholder="Task name, then Enter" @submit="(title) => add(status.id, title)" @cancel="addingIn = null" />
        </div>
        <button v-else class="faint add-card" @click="addingIn = status.id">＋ Add task</button>
      </div>
    </section>
    <section class="column new-col">
      <InlineInput v-if="addingStatus" placeholder="Status name" @submit="addStatus" @cancel="addingStatus = false" />
      <button v-else class="faint" @click="addingStatus = true">＋ Add status</button>
    </section>
  </div>
</template>

<style scoped>
.board { flex: 1; display: flex; gap: 12px; padding: 16px 20px; overflow-x: auto; align-items: flex-start; }
.column { width: 272px; flex: none; background: var(--bg-soft); border-radius: 10px; border-top: 3px solid var(--col, var(--border)); padding: 8px; max-height: 100%; display: flex; flex-direction: column; }
.col-head { padding: 4px 4px 8px; }
.col-pill { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.03em; color: var(--col); }
.cards { display: flex; flex-direction: column; gap: 8px; overflow-y: auto; min-height: 40px; }
.card { background: var(--bg); border: 1px solid var(--border); border-radius: 8px; padding: 10px; cursor: grab; box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04); }
.card:hover { border-color: var(--text-faint); }
.card.dragging { opacity: 0.4; }
.card.adding { cursor: default; padding: 6px; }
.card.adding input { width: 100%; }
.card-title { font-weight: 500; line-height: 1.35; word-break: break-word; }
.card-title.done { text-decoration: line-through; color: var(--text-muted); }
.card-meta { margin-top: 8px; flex-wrap: wrap; gap: 4px; }
.card-meta:empty { display: none; }
.small { font-size: 12px; }
.drop-line { height: 3px; border-radius: 2px; background: var(--accent); }
.add-card { text-align: left; font-size: 13px; }
.new-col { background: transparent; border-top-color: transparent; }
.new-col input { width: 100%; }
</style>

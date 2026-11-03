<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '../api'
import { useList } from '../stores/list'
import { useSession } from '../stores/session'
import StatusPicker from './StatusPicker.vue'
import PriorityPicker from './PriorityPicker.vue'
import AssigneePicker from './AssigneePicker.vue'
import DueDate from './DueDate.vue'
import Avatar from './Avatar.vue'
import InlineInput from './InlineInput.vue'
import type { Attachment, Comment, Task, TaskDetail } from '../types'

const props = defineProps<{ taskId: string }>()
const emit = defineEmits<{ close: []; open: [taskId: string] }>()
const list = useList()
const session = useSession()

const comments = ref<Comment[]>([])
const attachments = ref<Attachment[]>([])
const newComment = ref('')
const addingSubtask = ref(false)
const uploading = ref(false)
const error = ref('')

const task = computed(() => list.tasks.find((t) => t.id === props.taskId))
const parent = computed(() => (task.value?.parentTaskId ? list.tasks.find((t) => t.id === task.value!.parentTaskId) : undefined))
const subtasks = computed(() => list.subtasksOf(props.taskId))
const title = ref('')
const description = ref('')

async function load() {
  error.value = ''
  try {
    const d = await api.get<TaskDetail>(`/api/tasks/${props.taskId}`)
    list.replace(d.task)
    d.subtasks.forEach((s) => list.replace(s))
    comments.value = d.comments
    attachments.value = d.attachments
  } catch (e) {
    error.value = (e as Error).message
  }
}
watch(() => props.taskId, load, { immediate: true })
watch(task, (t) => {
  if (!t) return
  if (document.activeElement?.id !== 'task-title') title.value = t.title
  if (document.activeElement?.id !== 'task-desc') description.value = t.description
}, { immediate: true })

const update = (patch: Partial<Task>) => list.updateTask(props.taskId, patch).catch((e) => (error.value = e.message))

function saveTitle() {
  const v = title.value.trim()
  if (v && v !== task.value?.title) update({ title: v })
  else if (!v && task.value) title.value = task.value.title
}
function saveDescription() {
  if (description.value !== task.value?.description) update({ description: description.value })
}

async function addSubtask(t: string) {
  await list.createTask(t, undefined, props.taskId)
}

async function postComment() {
  const body = newComment.value.trim()
  if (!body) return
  comments.value.push(await api.post<Comment>(`/api/tasks/${props.taskId}/comments`, { body }))
  newComment.value = ''
}
async function deleteComment(c: Comment) {
  await api.del(`/api/comments/${c.id}`)
  comments.value = comments.value.filter((x) => x.id !== c.id)
}

async function upload(e: Event) {
  const input = e.target as HTMLInputElement
  const files = [...(input.files ?? [])]
  uploading.value = true
  try {
    for (const f of files) {
      const form = new FormData()
      form.append('file', f)
      attachments.value.push(await api.upload<Attachment>(`/api/tasks/${props.taskId}/attachments`, form))
    }
  } catch (err) {
    error.value = (err as Error).message
  } finally {
    uploading.value = false
    input.value = ''
  }
}
async function deleteAttachment(a: Attachment) {
  await api.del(`/api/attachments/${a.id}`)
  attachments.value = attachments.value.filter((x) => x.id !== a.id)
}

async function remove() {
  await list.deleteTask(props.taskId)
  emit('close')
}

const size = (n: number) => (n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(0)} KB` : `${(n / 1048576).toFixed(1)} MB`)
const when = (iso: string) => new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const isDone = (t: Task) => list.statusById(t.statusId)?.category === 'Done'
</script>

<template>
  <div class="backdrop" @click="emit('close')" />
  <aside class="drawer" role="dialog" aria-label="Task details">
    <header class="drawer-head row">
      <button v-if="parent" class="muted crumb" @click="emit('open', parent.id)">↖ {{ parent.title }}</button>
      <span class="spacer" />
      <button class="danger" title="Delete task" @click="remove">Delete</button>
      <button title="Close" @click="emit('close')">✕</button>
    </header>

    <div v-if="task" class="body">
      <input id="task-title" v-model="title" class="bare title" @blur="saveTitle" @keydown.enter.prevent="($event.target as HTMLInputElement).blur()" />

      <div class="fields">
        <span class="label">Status</span>
        <span><StatusPicker :status-id="task.statusId" @change="(statusId) => update({ statusId })" /></span>
        <span class="label">Assignee</span>
        <span><AssigneePicker :assignee-id="task.assigneeId" show-label @change="(assigneeId) => update({ assigneeId })" /></span>
        <span class="label">Start date</span>
        <span><DueDate :date="task.startDate" done @change="(startDate) => update({ startDate })" /></span>
        <span class="label">Due date</span>
        <span><DueDate :date="task.dueDate" :done="isDone(task)" @change="(dueDate) => update({ dueDate })" /></span>
        <span class="label">Priority</span>
        <span><PriorityPicker :priority="task.priority" show-label @change="(priority) => update({ priority })" /></span>
      </div>

      <textarea id="task-desc" v-model="description" class="bare desc" placeholder="Add a description…" rows="4" @blur="saveDescription" />

      <section v-if="!task.parentTaskId">
        <h3>Subtasks <span class="faint">{{ subtasks.filter(isDone).length }}/{{ subtasks.length }}</span></h3>
        <div v-for="s in subtasks" :key="s.id" class="sub row">
          <StatusPicker :status-id="s.statusId" compact @change="(statusId) => list.updateTask(s.id, { statusId })" />
          <button class="sub-title" :class="{ done: isDone(s) }" @click="emit('open', s.id)">{{ s.title }}</button>
          <AssigneePicker :assignee-id="s.assigneeId" @change="(assigneeId) => list.updateTask(s.id, { assigneeId })" />
        </div>
        <InlineInput v-if="addingSubtask" multi class="sub-input" placeholder="Subtask name, then Enter" @submit="addSubtask" @cancel="addingSubtask = false" />
        <button v-else class="faint" @click="addingSubtask = true">＋ Add subtask</button>
      </section>

      <section>
        <h3>Attachments</h3>
        <div v-for="a in attachments" :key="a.id" class="attachment row">
          <a :href="`/api/attachments/${a.id}`" target="_blank" rel="noopener">📎 {{ a.fileName }}</a>
          <span class="faint">{{ size(a.sizeBytes) }}</span>
          <span class="spacer" />
          <button class="faint" title="Remove" @click="deleteAttachment(a)">✕</button>
        </div>
        <label class="upload faint">
          {{ uploading ? 'Uploading…' : '＋ Attach files' }}
          <input type="file" multiple hidden @change="upload" />
        </label>
      </section>

      <section>
        <h3>Comments</h3>
        <div v-for="c in comments" :key="c.id" class="comment">
          <div class="row">
            <Avatar :name="c.author.displayName" />
            <strong>{{ c.author.displayName }}</strong>
            <span class="faint small">{{ when(c.createdAt) }}</span>
            <span class="spacer" />
            <button v-if="c.author.id === session.user?.id" class="faint small" @click="deleteComment(c)">Delete</button>
          </div>
          <p>{{ c.body }}</p>
        </div>
        <form class="comment-form" @submit.prevent="postComment">
          <textarea v-model="newComment" rows="2" placeholder="Write a comment…" @keydown.meta.enter="postComment" @keydown.ctrl.enter="postComment" />
          <button class="primary" :disabled="!newComment.trim()">Comment</button>
        </form>
      </section>
      <p v-if="error" class="error">{{ error }}</p>
    </div>
    <div v-else class="empty">{{ error || 'Loading…' }}</div>
  </aside>
</template>

<style scoped>
.backdrop { position: fixed; inset: 0; background: rgba(0, 0, 0, 0.2); z-index: 40; }
.drawer { position: fixed; top: 0; right: 0; bottom: 0; width: min(560px, 100vw); background: var(--bg); border-left: 1px solid var(--border); box-shadow: var(--shadow); z-index: 41; display: flex; flex-direction: column; }
.drawer-head { padding: 10px 14px; border-bottom: 1px solid var(--border); }
.crumb { max-width: 60%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.body { padding: 16px 24px 40px; overflow-y: auto; display: flex; flex-direction: column; gap: 18px; }
.title { font-size: 22px; font-weight: 700; width: 100%; }
.fields { display: grid; grid-template-columns: 110px 1fr; row-gap: 6px; align-items: center; }
.label { color: var(--text-muted); font-size: 13px; }
.desc { width: 100%; resize: vertical; line-height: 1.5; }
h3 { font-size: 13px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); margin: 0 0 6px; }
.sub { padding: 4px 0; border-bottom: 1px solid var(--border); }
.sub-title { flex: 1; text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sub-title.done { text-decoration: line-through; color: var(--text-muted); }
.sub-input { width: 100%; margin-top: 6px; }
.attachment { padding: 4px 0; }
.attachment a { color: var(--text); }
.upload { cursor: pointer; padding: 4px 8px; display: inline-block; border-radius: 6px; }
.upload:hover { background: var(--bg-hover); }
.comment { padding: 8px 0; border-bottom: 1px solid var(--border); }
.comment p { margin: 6px 0 0 30px; white-space: pre-wrap; line-height: 1.45; }
.comment-form { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; align-items: flex-end; }
.comment-form textarea { width: 100%; resize: vertical; }
.small { font-size: 12px; }
.error { color: var(--danger); }
</style>

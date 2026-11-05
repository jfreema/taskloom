<script setup lang="ts">
// Subtasks (8.10), for top-level tasks: "Subtasks" with the progress count and thread, the rows
// in position order (no dragging here), and "+ Add subtask", which becomes a field: Enter creates
// the subtask with the list's default status and keeps the field open for the next one.
import { computed, nextTick, ref, useId } from 'vue'
import { Plus } from '@lucide/vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import type { Task } from '../../types'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import UiButton from '../ui/UiButton.vue'
import UiInput from '../ui/UiInput.vue'
import UiProgress from '../ui/UiProgress.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import SubtaskRow from './SubtaskRow.vue'
import { useTaskDetailContext } from './taskDetailContext'

const MAX_TITLE = 500

const props = defineProps<{ task: Task; loaded: boolean }>()

const ctx = useTaskDetailContext()
const tasks = useTasks()
const headingId = `${useId()}-subtasks`

const subtasks = computed(() =>
  [...tasks.subtasksOf(props.task.id)].sort((a, b) => a.position - b.position || (a.id < b.id ? -1 : 1)),
)
/** The store may hold every subtask already (the list was loaded); otherwise wait for the detail. */
const known = computed(() => props.loaded || subtasks.value.length >= props.task.subtaskCount)
const showSkeleton = useDelayedFlag(computed(() => !known.value), 200)
const total = computed(() => (known.value ? subtasks.value.length : props.task.subtaskCount))
const done = computed(() => (known.value ? subtasks.value.filter((t) => tasks.isDone(t)).length : props.task.subtaskDoneCount))

const adding = ref(false)
const draft = ref('')
const input = ref<InstanceType<typeof UiInput> | null>(null)
const addButton = ref<InstanceType<typeof UiButton> | null>(null)

async function startAdding(): Promise<void> {
  adding.value = true
  await nextTick()
  input.value?.focus()
}

function create(): void {
  const title = draft.value.trim()
  if (!title) return
  draft.value = ''
  void tasks.create(props.task.listId, { title, parentTaskId: props.task.id }).catch(noop)
}

async function stopAdding(): Promise<void> {
  adding.value = false
  draft.value = ''
  await nextTick()
  const el = (addButton.value as { $el?: unknown } | null)?.$el
  if (el instanceof HTMLElement) el.focus()
}

function onEscape(e: KeyboardEvent): void {
  // Esc only closes the field; the panel stays open.
  e.preventDefault()
  void stopAdding()
}

function onBlur(): void {
  if (!draft.value.trim()) adding.value = false
}
</script>

<template>
  <section class="subtasks" :aria-labelledby="headingId">
    <div class="header">
      <component :is="ctx.headingTag.value" :id="headingId" class="section-title">Subtasks</component>
      <SubtaskProgress :done="done" :total="total" />
      <UiProgress v-if="total > 0" class="thread" :value="done" :max="total" label="Subtasks done" variant="thread" decorative />
    </div>
    <div v-if="!known && showSkeleton" class="skeleton" aria-hidden="true">
      <UiSkeleton v-for="i in Math.min(task.subtaskCount, 4)" :key="i" width="60%" />
    </div>
    <ul v-else-if="known && subtasks.length" class="rows">
      <SubtaskRow v-for="s in subtasks" :key="s.id" :task="s" />
    </ul>
    <p v-else-if="known && !ctx.canEdit.value" class="none">No subtasks</p>
    <div v-if="ctx.canEdit.value" class="add">
      <UiInput
        v-if="adding"
        ref="input"
        v-model="draft"
        class="add-field"
        size="sm"
        placeholder="Subtask name"
        aria-label="Subtask name"
        :maxlength="MAX_TITLE"
        @enter.prevent="create"
        @escape="onEscape"
        @blur="onBlur"
      />
      <UiButton v-else ref="addButton" variant="ghost" size="sm" :icon="Plus" @click="startAdding">Add subtask</UiButton>
    </div>
  </section>
</template>

<style scoped>
.subtasks {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--control-sm);
}

.section-title {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.thread {
  flex: 1;
  max-width: 160px;
}

/* One grid shared by every row (each SubtaskRow is a subgrid), so the avatars, dates and menus line up
   from row to row whatever each row shows: toggle, title, assignees, due date, menu. */
.rows {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto auto auto;
  column-gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

@media (pointer: coarse) {
  .rows {
    column-gap: var(--space-3);
  }
}

.skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-2) 0;
}

.none {
  margin: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-tertiary);
}

.add {
  display: flex;
  margin-left: calc(var(--space-2) * -1);
}

.add-field {
  flex: 1;
  margin-left: var(--space-2);
}

/* Touch (7.5): a 44px row, so the button's 44px target stays clear of the last subtask's. */
@media (pointer: coarse) {
  .add {
    align-items: center;
    min-height: var(--touch-min);
  }
}
</style>

<script setup lang="ts">
// The task detail (8.10), in the panel (docked, modal, sheet: TaskPanelHost owns the container
// and its focus root) or on the full page (TaskPage). It reads like a document: the status and
// title, the properties, the description, subtasks, attachments and the activity, with the comment
// composer at the bottom.
// - Instant render: the title and properties come from the task store at once; skeletons (after
//   200ms) cover only what the detail brings. A 404 shows the missing state.
// - The visually hidden heading (h2, or the page's h1) carries `titleId`, which names the host.
// - Keys (panel scope: focus inside the panel root, never while typing): J and K move through the
//   view's order, S A D P open the pickers, Mod+Enter completes or reopens, Mod+Backspace deletes.
// - Files dropped anywhere on the panel (or page) or pasted into it become attachments.
import { computed, nextTick, provide, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useShortcut } from '../../composables/useShortcut'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { noop } from '../../lib/async'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import GuestNotice from '../shell/GuestNotice.vue'
import UiErrorState from '../ui/UiErrorState.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import ActivitySection from './ActivitySection.vue'
import AttachmentsSection from './AttachmentsSection.vue'
import CommentComposer from './CommentComposer.vue'
import DescriptionSection from './DescriptionSection.vue'
import SubtasksSection from './SubtasksSection.vue'
import TaskDetailHeader from './TaskDetailHeader.vue'
import TaskDropOverlay from './TaskDropOverlay.vue'
import TaskMissingState from './TaskMissingState.vue'
import TaskProperties, { type PickerField } from './TaskProperties.vue'
import { taskDetailKey, type TaskDetailMode } from './taskDetailContext'
import { mergeAttachments } from './taskDetailState'
import { useAttachmentUploads } from './useAttachmentUploads'
import { useTaskDetailData } from './useTaskDetailData'
import { useTaskDetailScope } from './useTaskDetailScope'

const props = defineProps<{ taskId: Uuid; mode: TaskDetailMode; titleId: string }>()
const emit = defineEmits<{ close: [] }>()

const tasks = useTasks()
const workspaces = useWorkspaces()
const panel = useTaskPanel()
const router = useRouter()
const uploads = useAttachmentUploads()

const taskId = computed(() => props.taskId)
const mode = computed(() => props.mode)
const task = computed(() => tasks.get(props.taskId))
const data = useTaskDetailData(taskId)
const detail = computed(() => (data.detail.value?.task.id === props.taskId ? data.detail.value : null))
const missing = computed(() => data.missing.value)
const loadError = computed(() => (detail.value || missing.value ? null : data.error.value))

const workspaceId = computed(() => detail.value?.workspaceId ?? (task.value ? workspaces.findList(task.value.listId)?.workspace.id : undefined))
const canEdit = computed(() => workspaces.canEdit(workspaceId.value))
const guest = computed(() => workspaces.role(workspaceId.value) === 'Guest')
const headingTag = computed<'h2' | 'h3'>(() => (props.mode === 'page' ? 'h2' : 'h3'))
const title = computed(() => task.value?.title ?? detail.value?.task.title ?? (missing.value ? 'Task not found' : 'Task'))
const parentTitle = computed(() => {
  const parentId = task.value?.parentTaskId
  if (!parentId) return null
  return detail.value?.parent?.title ?? tasks.get(parentId)?.title ?? tasks.refs[parentId]?.title ?? null
})
const attachments = computed(() => (detail.value ? mergeAttachments(props.taskId, detail.value.attachments) : null))
const live = computed(() => !!task.value && !missing.value && !tasks.isPendingDelete(props.taskId))
const showTopSkeleton = useDelayedFlag(
  computed(() => !task.value && !missing.value && !loadError.value),
  200,
)

function openTask(id: Uuid): void {
  if (props.mode === 'page') void router.push(`/t/${id}`).catch(noop)
  else panel.open(id)
}

function addFiles(files: File[], o: { announce?: boolean } = {}): void {
  if (!canEdit.value || !live.value || !files.length) return
  uploads.add(props.taskId, files, o)
}

provide(taskDetailKey, { taskId, mode, headingTag, canEdit, openTask, addFiles })

function openParent(): void {
  const parentId = task.value?.parentTaskId
  if (parentId) openTask(parentId)
}

/** Delete with Undo. The panel closes by itself once the task is pending delete; the page goes to the list. */
function deleteTask(): void {
  const t = task.value
  if (!t || !canEdit.value) return
  tasks.remove([t.id])
  if (props.mode === 'page') void router.push(`/l/${t.listId}`).catch(noop)
}

// ---- Root, drop zone, paste ----

const root = ref<HTMLElement | null>(null)
const scroller = ref<HTMLElement | null>(null)
const { scopeEl, dropActive } = useTaskDetailScope(root, {
  titleId: () => props.titleId,
  enabled: () => canEdit.value && live.value,
  onFiles: (files) => addFiles(files, { announce: true }),
})

// J and K show the next task from the top.
watch(taskId, () => {
  void nextTick(() => {
    if (scroller.value) scroller.value.scrollTop = 0
  })
})

// ---- Keys ----

const properties = ref<InstanceType<typeof TaskProperties> | null>(null)

function pick(field: PickerField): false | void {
  if (!canEdit.value || !properties.value?.openPicker(field)) return false
}

const inPanel = () => props.mode !== 'page'
useShortcut('j', () => panel.next(), { scope: 'panel', el: scopeEl, when: inPanel })
useShortcut('k', () => panel.prev(), { scope: 'panel', el: scopeEl, when: inPanel })
useShortcut('s', () => pick('status'), { scope: 'panel', el: scopeEl, when: () => live.value })
useShortcut('a', () => pick('assignees'), { scope: 'panel', el: scopeEl, when: () => live.value })
useShortcut('d', () => pick('dueDate'), { scope: 'panel', el: scopeEl, when: () => live.value })
useShortcut('p', () => pick('priority'), { scope: 'panel', el: scopeEl, when: () => live.value })
useShortcut(
  'Mod+Enter',
  () => {
    if (!canEdit.value) return false
    void tasks.toggleComplete(props.taskId)
  },
  { scope: 'panel', el: scopeEl, when: () => live.value },
)
useShortcut(
  'Mod+Backspace',
  () => {
    if (!canEdit.value) return false
    deleteTask()
  },
  { scope: 'panel', el: scopeEl, when: () => live.value },
)
</script>

<template>
  <div
    ref="root"
    class="task-detail"
    :class="`mode-${mode}`"
    :data-shortcut-scope="mode === 'page' ? 'panel' : undefined"
    :tabindex="mode === 'page' ? -1 : undefined"
  >
    <component :is="mode === 'page' ? 'h1' : 'h2'" :id="titleId" class="sr-only">{{ title }}</component>
    <TaskDetailHeader
      v-if="mode !== 'page'"
      :task-id="taskId"
      :task="missing ? undefined : task"
      :parent-title="parentTitle"
      :mode="mode"
      :can-edit="canEdit && live"
      @close="emit('close')"
      @open-parent="openParent"
      @delete="deleteTask"
    />
    <div ref="scroller" class="scroll">
      <div :key="taskId" class="body">
        <GuestNotice v-if="guest && !missing" />
        <div v-if="missing" class="state">
          <TaskMissingState :page="mode === 'page'" @close="emit('close')" />
        </div>
        <template v-else-if="task">
          <TaskProperties ref="properties" :task="task" :created-by="detail?.createdBy ?? null" :can-edit="canEdit" :size="mode === 'page' ? 'page' : 'panel'" />
          <UiErrorState v-if="loadError" class="detail-error" title="Couldn't load this task's details" :retry="data.reload" />
          <template v-else>
            <DescriptionSection
              :task-id="taskId"
              :html="detail ? detail.description : null"
              :requested-at="data.requestedAt.value"
              :can-edit="canEdit"
              :heading-tag="headingTag"
              @files="(files: File[]) => addFiles(files, { announce: true })"
            />
            <SubtasksSection v-if="!task.parentTaskId" :task="task" :loaded="!!detail" />
            <AttachmentsSection :task-id="taskId" :attachments="attachments" />
            <ActivitySection class="activity" :task-id="taskId" :detail="detail" />
          </template>
        </template>
        <div v-else-if="loadError" class="state">
          <UiErrorState title="Couldn't load this task" :retry="data.reload" />
        </div>
        <div v-else-if="showTopSkeleton" class="skeleton" aria-hidden="true">
          <UiSkeleton variant="rect" width="112px" height="28px" />
          <UiSkeleton width="70%" height="24px" />
          <UiSkeleton :lines="4" />
        </div>
      </div>
    </div>
    <CommentComposer v-if="task && !missing" class="composer" :task-id="taskId" :layout="mode === 'page' ? 'page' : 'panel'" />
    <TaskDropOverlay v-if="dropActive" :fixed="mode === 'page'" />
  </div>
</template>

<style scoped>
.task-detail {
  position: relative;
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  outline: none;
}

.task-detail.mode-docked,
.task-detail.mode-modal,
.task-detail.mode-sheet {
  height: 100%;
  overflow: hidden;
}

.scroll {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}

.task-detail.mode-docked .scroll,
.task-detail.mode-modal .scroll,
.task-detail.mode-sheet .scroll {
  overflow-y: auto;
  overscroll-behavior: contain;
}

.body {
  display: flex;
  flex-direction: column;
  flex: 1 0 auto;
  gap: var(--space-6);
  width: 100%;
  padding: var(--space-5) var(--space-6) var(--space-8);
}

.task-detail.mode-sheet .body {
  padding: var(--space-4) var(--space-4) var(--space-6);
}

.task-detail.mode-page .body {
  max-width: var(--reading-w);
  margin: 0 auto;
  padding: var(--space-8) var(--space-6) var(--space-10);
  flex: 1;
}

@media (max-width: 639px) {
  .task-detail.mode-page .body {
    padding: var(--space-4) var(--space-4) var(--space-6);
  }
}

.activity {
  padding-top: var(--space-5);
  border-top: 1px solid var(--border-subtle);
}

.state {
  margin: auto 0;
  padding: var(--space-10) 0;
}

.detail-error {
  padding: var(--space-6) 0;
}

.skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.task-detail.mode-page .composer {
  position: sticky;
  bottom: 0;
  z-index: var(--z-sticky);
  background: var(--bg-surface);
}

.task-detail.mode-docked .composer {
  background: var(--bg-surface);
}

.task-detail.mode-modal .composer,
.task-detail.mode-sheet .composer {
  background: var(--bg-raised);
}
</style>

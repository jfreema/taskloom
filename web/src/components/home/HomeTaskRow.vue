<script setup lang="ts">
// One Home row (8.5): 40px, `data-row`, `data-task-row`. The complete circle in the status colour,
// the title (subtasks add "in <parent>"), subtask progress, the location link, the due date, the
// flag and the people, or "Unassigned" when it shows only because I created it. Revealed actions:
// Reschedule (the parent opens the shared date picker on this row) and the shared row menu.
// The row is one roving tab stop; its controls are tabbable only while it is the tab stop.
// Below 640px of page width (container `page`) it takes two lines (56px).
import { computed, ref } from 'vue'
import { CalendarClock, Ellipsis } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { useToday } from '../../composables/useToday'
import { formatDue } from '../../lib/dates'
import { useMembers } from '../../stores/members'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task } from '../../types'
import CompleteToggle from '../fields/CompleteToggle.vue'
import DueDate from '../fields/DueDate.vue'
import PriorityFlag from '../fields/PriorityFlag.vue'
import SpaceTile from '../fields/SpaceTile.vue'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'

const props = defineProps<{
  task: Task
  /** The roving tab stop of the Home rows. */
  tabStop: boolean
  /** Open in the task panel. */
  current: boolean
  /** Just created by quick add: the background flashes. */
  flash: boolean
  canEdit: boolean
}>()

const emit = defineEmits<{
  open: [e: MouseEvent]
  reschedule: [anchor: HTMLElement]
}>()

const tasks = useTasks()
const workspaces = useWorkspaces()
const members = useMembers()
const actions = useTaskActions()
const today = useToday()

const root = ref<HTMLElement | null>(null)
const menuOpen = ref(false)

const done = computed(() => tasks.isDone(props.task))
const statusColor = computed(() => workspaces.statusById(props.task.statusId)?.color)
const parentTitle = computed(() => {
  const id = props.task.parentTaskId
  return id ? (tasks.get(id)?.title ?? tasks.refs[id]?.title ?? null) : null
})
const location = computed(() => workspaces.findList(props.task.listId))
const people = computed(() => props.task.assigneeIds.map((id) => ({ id, displayName: members.nameOf(id) })))
const readonly = computed(() => !props.canEdit || tasks.isPendingDelete(props.task.id))
/** Controls inside the row join the tab order only while the row is the tab stop. */
const inner = computed(() => (props.tabStop ? undefined : -1))

const label = computed(() => {
  const parts = [props.task.title]
  if (parentTitle.value) parts.push(`in ${parentTitle.value}`)
  if (props.task.dueDate) parts.push(`due ${formatDue(props.task.dueDate, today.value)}`)
  if (done.value) parts.push('completed')
  return parts.join(', ')
})

function menuItems() {
  return actions.rowMenuItems(props.task, {
    variant: 'home',
    onPick: (field) => {
      if (field === 'dueDate' && root.value) emit('reschedule', root.value)
    },
  })
}

const CONTROLS = 'button, a, input, textarea, [role="checkbox"], [role="button"]'

/** A click on the row itself, outside its controls, opens the task. */
function onClick(e: MouseEvent): void {
  const hit = e.target instanceof Element ? e.target.closest(CONTROLS) : null
  if (hit && root.value?.contains(hit)) return
  emit('open', e)
}

function reschedule(): void {
  if (root.value) emit('reschedule', root.value)
}

defineExpose({ el: root })
</script>

<template>
  <li
    ref="root"
    class="home-row focus-inset"
    :class="{ 'is-current': current, 'is-done': done, 'flash-new': flash }"
    data-row
    :data-task-row="task.id"
    :tabindex="tabStop ? 0 : -1"
    :aria-label="label"
    :aria-current="current ? 'true' : undefined"
    @click="onClick"
  >
    <CompleteToggle
      class="toggle"
      :done="done"
      :label="task.title"
      :color="statusColor"
      :readonly="readonly"
      :tabindex="inner"
      @toggle="actions.toggleComplete(task.id)"
    />
    <span class="main">
      <span v-tooltip="{ text: task.title, overflowOnly: true }" class="title truncate">{{ task.title }}</span>
      <span v-if="parentTitle" class="parent truncate">in {{ parentTitle }}</span>
      <SubtaskProgress :done="task.subtaskDoneCount" :total="task.subtaskCount" />
    </span>
    <span class="meta">
      <RouterLink
        v-if="location"
        v-tooltip="{ text: `${location.space.name} / ${location.list.name}`, overflowOnly: true }"
        class="location"
        :to="`/l/${task.listId}`"
        :tabindex="inner"
      >
        <SpaceTile :space="location.space" :size="16" />
        <span class="truncate">{{ location.space.name }} / {{ location.list.name }}</span>
      </RouterLink>
      <span v-else class="location" />
      <span class="due">
        <span v-if="task.dueDate" class="sep" aria-hidden="true">·</span>
        <DueDate :date="task.dueDate" :done="done" size="sm" />
      </span>
      <span class="flag"><PriorityFlag v-if="task.priority !== 'None'" :priority="task.priority" :size="14" /></span>
      <span class="people">
        <UiAvatarStack v-if="people.length" :users="people" :max="3" :size="20" />
        <UiBadge v-else>Unassigned</UiBadge>
      </span>
    </span>
    <span class="actions">
      <UiIconButton
        v-if="!readonly"
        class="reveal"
        :icon="CalendarClock"
        :label="`Reschedule ${task.title}`"
        kbd="D"
        :tabindex="inner"
        @click="reschedule"
      />
      <UiMenu v-model:open="menuOpen" :items="menuItems" :aria-label="`Actions for ${task.title}`">
        <template #trigger="{ props: trigger }">
          <UiIconButton
            v-bind="trigger"
            class="reveal reveal-touch"
            :icon="Ellipsis"
            :label="`More actions for ${task.title}`"
            :tabindex="inner"
            data-row-menu
          />
        </template>
      </UiMenu>
    </span>
  </li>
</template>

<style scoped>
.home-row {
  position: relative;
  display: grid;
  grid-template-columns: var(--control-xs) minmax(120px, 1fr) auto auto;
  grid-template-areas: "toggle main meta actions";
  align-items: center;
  column-gap: var(--space-3);
  min-height: 40px;
  padding: 0 var(--space-1) 0 var(--space-2);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  cursor: pointer;
  transition: background-color var(--dur-1) var(--ease-out);
}

.home-row:hover {
  background: var(--bg-hover);
}

.home-row.is-current {
  background: var(--bg-selected);
  box-shadow: inset 2px 0 0 var(--accent);
}

.toggle {
  grid-area: toggle;
}

.main {
  grid-area: main;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}

.title {
  flex: 0 1 auto;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.is-done .title {
  color: var(--text-tertiary);
}

.parent {
  flex: 0 10000 auto;
  min-width: 48px;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.meta {
  grid-area: meta;
  display: grid;
  grid-template-columns: minmax(0, 200px) 88px 16px 76px;
  align-items: center;
  column-gap: var(--space-3);
  min-width: 0;
}

.location {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  max-width: 200px;
  min-height: var(--control-xs);
  border-radius: var(--radius-xs);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  text-decoration: none;
}

a.location:hover {
  color: var(--text-secondary);
  text-decoration: underline;
}

.due,
.flag,
.people {
  display: inline-flex;
  align-items: center;
  min-width: 0;
}

.sep {
  display: none;
  color: var(--text-tertiary);
}

.actions {
  grid-area: actions;
  display: inline-flex;
  align-items: center;
  gap: var(--space-0-5);
}

/* Narrow pages (the phone, or a docked panel): two lines, 56px. */
@container page (max-width: 639px) {
  .home-row {
    grid-template-columns: var(--control-xs) minmax(0, 1fr) auto;
    grid-template-areas:
      "toggle main actions"
      ". meta actions";
    row-gap: 0;
    min-height: 56px;
    padding-top: var(--space-1);
    padding-bottom: var(--space-1);
  }

  .meta {
    display: flex;
    align-items: center;
    gap: var(--space-1-5);
    min-width: 0;
    overflow: hidden;
  }

  .location {
    flex: 0 1 auto;
    max-width: 60%;
  }

  .due,
  .flag,
  .people {
    flex: none;
    gap: var(--space-1-5);
  }

  .flag:empty {
    display: none;
  }

  .sep {
    display: inline;
  }
}
</style>

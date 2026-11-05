<script setup lang="ts">
// One Home row (8.5): 40px, `data-row`, `data-task-row`. The complete circle in the status colour,
// the title (subtasks add "in <parent>"), subtask progress, then the details (HomeRowMeta: location,
// due date, flag, people). Revealed actions: Reschedule (the parent opens the shared date picker on
// this row) and the shared row menu; with a mouse they overlay the row's trailing edge on a fade
// rather than taking a column. The row is one roving tab stop; its controls are tabbable only
// while it is the tab stop. Below 640px of page width (container `page`) it takes two lines (56px).
import { computed, ref } from 'vue'
import { CalendarClock, Ellipsis } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import { useToday } from '../../composables/useToday'
import { formatDue } from '../../lib/dates'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task } from '../../types'
import CompleteToggle from '../fields/CompleteToggle.vue'
import SubtaskProgress from '../fields/SubtaskProgress.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'
import HomeRowMeta from './HomeRowMeta.vue'

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
      @toggle="void actions.toggleComplete(task.id)"
    />
    <span class="main">
      <span v-tooltip="{ text: task.title, overflowOnly: true }" class="title truncate">{{ task.title }}</span>
      <span v-if="parentTitle" class="parent truncate">in {{ parentTitle }}</span>
      <SubtaskProgress :done="task.subtaskDoneCount" :total="task.subtaskCount" />
    </span>
    <HomeRowMeta class="meta" :task="task" :done="done" :tabindex="inner" />
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
}

.actions {
  grid-area: actions;
  display: inline-flex;
  align-items: center;
  gap: var(--space-0-5);
}

/* One-line rows under a mouse (V12): the actions take no column of their own. They overlay the
   row's trailing edge on a fade of the row's own colour while the row is hovered, focused from
   the keyboard or has its menu open, so the details end at the row's padding. */
@media (hover: hover) {
  @container page (min-width: 640px) {
    .home-row {
      --row-base: var(--bg-surface);
      --row-tint: transparent;
      grid-template-columns: var(--control-xs) minmax(120px, 1fr) auto;
      grid-template-areas: "toggle main meta";
      padding-right: var(--space-2);
    }

    .home-row:hover {
      --row-tint: var(--bg-hover);
    }

    .home-row.is-current {
      --row-base: var(--bg-selected);
      --row-tint: transparent;
    }

    .actions {
      position: absolute;
      top: 0;
      right: 0;
      bottom: 0;
      padding: 0 var(--space-1) 0 var(--space-6);
      border-radius: 0 var(--radius-md) var(--radius-md) 0;
      background:
        linear-gradient(to right, transparent, var(--row-tint) var(--space-6)),
        linear-gradient(to right, transparent, var(--row-base) var(--space-6));
      opacity: 0;
      transition: opacity var(--dur-1) var(--ease-out);
    }

    .home-row:hover .actions,
    .home-row:focus-visible .actions,
    .home-row:has(:focus-visible) .actions,
    .actions:has([aria-expanded="true"]) {
      opacity: 1;
    }
  }
}

/* Touch (7.5): one-line rows are 44px. */
@media (pointer: coarse) {
  .home-row {
    min-height: var(--touch-min);
  }
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
}
</style>

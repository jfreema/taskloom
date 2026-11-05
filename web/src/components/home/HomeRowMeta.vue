<script setup lang="ts">
// A Home row's details (8.5): the location link (SpaceTile 16 and "Space / List", max 200px), the
// due date, the flag (only if set) and the people, or "Unassigned" when the row shows only because
// I created it. On one-line rows they sit in fixed columns, the people right-aligned in theirs, so
// every row ends at the same edge; below 640px of page width (container
// `page`) they form the row's second line, "location · due · flag · people", and the location shows
// only the list name (the tile stands for the space).
import { computed } from 'vue'
import { useMembers } from '../../stores/members'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task } from '../../types'
import DueDate from '../fields/DueDate.vue'
import PriorityFlag from '../fields/PriorityFlag.vue'
import SpaceTile from '../fields/SpaceTile.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'
import UiBadge from '../ui/UiBadge.vue'

const props = defineProps<{
  task: Task
  done: boolean
  /** The row's controls are tabbable only while the row is the tab stop. */
  tabindex?: number
}>()

const workspaces = useWorkspaces()
const members = useMembers()

const location = computed(() => workspaces.findList(props.task.listId))
const path = computed(() => (location.value ? `${location.value.space.name} / ${location.value.list.name}` : ''))
const people = computed(() => props.task.assigneeIds.map((id) => ({ id, displayName: members.nameOf(id) })))
</script>

<template>
  <span class="row-meta">
    <RouterLink
      v-if="location"
      v-tooltip="{ text: path, overflowOnly: true }"
      class="location"
      :to="`/l/${task.listId}`"
      :tabindex="tabindex"
      :aria-label="path"
    >
      <SpaceTile :space="location.space" :size="16" />
      <!-- The space name gives way first (and goes on narrow pages): the tile already shows its initial. -->
      <span class="location-text">
        <span class="location-space truncate">{{ location.space.name }}</span>
        <span class="location-sep" aria-hidden="true">/</span>
        <span class="location-list truncate">{{ location.list.name }}</span>
      </span>
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
</template>

<style scoped>
.row-meta {
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

.location-text {
  display: flex;
  min-width: 0;
}

.location-space {
  flex: 0 1000 auto;
  min-width: 2.5ch;
}

.location-sep {
  flex: none;
  padding: 0 var(--space-1);
}

.location-list {
  flex: 0 1 auto;
}

.due,
.flag,
.people {
  display: inline-flex;
  align-items: center;
  min-width: 0;
}

/* The people (or "Unassigned") end at the row's right edge. */
.people {
  justify-self: end;
}

.sep {
  display: none;
  color: var(--text-tertiary);
}

/* Touch (7.5): on a one-line row (44px) the link's hit area is the row's full height. */
@media (pointer: coarse) {
  a.location {
    position: relative;
  }

  a.location::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: var(--touch-min);
    transform: translateY(-50%);
  }
}

/* Narrow pages: the row's second line. */
@container page (max-width: 639px) {
  .row-meta {
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

  .location-space,
  .location-sep {
    display: none;
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

  /* Touch, two-line rows: the location is a 16px line under the title, too close to it for a 44px
     target of its own, so a tap on it opens the task like the rest of the row. The link stays for
     the keyboard and screen readers, and the list is a tap away in the panel and the drawer. */
  @media (pointer: coarse) {
    a.location {
      pointer-events: none;
    }
  }
}
</style>

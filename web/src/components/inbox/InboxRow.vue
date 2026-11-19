<script setup lang="ts">
// One Inbox row (Features 2): an unread dot, who did it (their avatar; a clock for reminders), the
// task's title, what happened with the comment it quotes, where the task lives and when. The whole
// row opens the task; "Mark as read" or "Mark as unread" appears on hover and focus.
import { computed } from 'vue'
import { AlarmClock, CalendarClock, Mail, MailOpen } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { useWorkspaces } from '../../stores/workspaces'
import type { InboxItem } from '../../types'
import RelativeTime from '../task/RelativeTime.vue'
import UiAvatar from '../ui/UiAvatar.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { inboxLabel, inboxQuote, inboxSegments } from './inboxText'

const props = defineProps<{ item: InboxItem; current: boolean }>()

const emit = defineEmits<{
  open: []
  toggleRead: []
}>()

const today = useToday()
const workspaces = useWorkspaces()

const unread = computed(() => !props.item.readAt)
const segments = computed(() => inboxSegments(props.item, today.value))
const quote = computed(() => inboxQuote(props.item))
const label = computed(() => inboxLabel(props.item, today.value))
const listName = computed(() => workspaces.findList(props.item.listId)?.list.name ?? null)
const reminderIcon = computed(() => (props.item.kind === 'Overdue' ? AlarmClock : CalendarClock))
</script>

<template>
  <li class="inbox-row" :class="{ 'is-unread': unread, 'is-current': current }" data-row :data-task-row="item.taskId" :data-inbox-row="item.id">
    <button type="button" class="open focus-inset" :aria-label="label" :aria-current="current ? 'true' : undefined" @click="emit('open')">
      <span class="dot" aria-hidden="true" />
      <UiAvatar v-if="item.actor" class="who" :user="item.actor" :size="28" decorative />
      <span v-else class="who reminder" :class="{ 'is-overdue': item.kind === 'Overdue' }" aria-hidden="true">
        <UiIcon :icon="reminderIcon" :size="16" />
      </span>
      <span class="text">
        <span class="title truncate">{{ item.taskTitle }}</span>
        <span class="what truncate">
          <span v-if="item.actor" class="actor">{{ item.actor.displayName }}</span>
          <template v-for="(s, i) in segments" :key="i"
            ><strong v-if="s.strong" class="strong">{{ s.text }}</strong
            ><template v-else>{{ s.text }}</template></template
          ><template v-if="quote">: <span class="quote">{{ quote }}</span></template>
        </span>
      </span>
      <span class="meta">
        <span v-if="listName" class="list truncate">{{ listName }}</span>
        <RelativeTime class="time" :at="item.createdAt" />
      </span>
    </button>
    <span class="actions">
      <UiIconButton
        class="reveal reveal-touch"
        :icon="unread ? MailOpen : Mail"
        :label="unread ? 'Mark as read' : 'Mark as unread'"
        size="sm"
        @click="emit('toggleRead')"
      />
    </span>
  </li>
</template>

<style scoped>
.inbox-row {
  position: relative;
  display: flex;
  align-items: center;
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  transition: background-color var(--dur-1) var(--ease-out);
}

.inbox-row:hover {
  background: var(--bg-hover);
}

.inbox-row.is-current {
  background: var(--bg-selected);
  box-shadow: inset 2px 0 0 var(--accent);
}

.open {
  display: grid;
  grid-template-columns: 8px 28px minmax(0, 1fr) auto;
  align-items: center;
  column-gap: var(--space-3);
  flex: 1;
  min-width: 0;
  padding: var(--space-2) var(--space-10) var(--space-2) var(--space-2);
  border: 0;
  border-radius: inherit;
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: transparent;
}

.is-unread .dot {
  background: var(--accent);
}

.who {
  flex: none;
}

.reminder {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--accent-subtle);
  color: var(--accent-text);
}

.reminder.is-overdue {
  background: var(--danger-subtle);
  color: var(--danger-text);
}

.text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-secondary);
}

.is-unread .title {
  color: var(--text-primary);
  font-weight: var(--fw-semibold);
}

.what {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.is-unread .what {
  color: var(--text-secondary);
}

.actor {
  margin-right: 0.3em;
  font-weight: var(--fw-medium);
  color: var(--text-secondary);
}

.is-unread .actor {
  color: var(--text-primary);
}

.strong {
  font-weight: var(--fw-medium);
  color: var(--text-secondary);
}

.is-unread .strong {
  color: var(--text-primary);
}

.is-overdue + .text .strong:first-child {
  color: var(--danger-text);
}

.meta {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.list {
  max-width: 160px;
}

.actions {
  position: absolute;
  top: 50%;
  right: var(--space-1-5);
  display: inline-flex;
  transform: translateY(-50%);
}

/* Narrow pages (the phone, or beside a docked panel): the list's name gives way. */
@container page (max-width: 639px) {
  .list {
    display: none;
  }

  .open {
    column-gap: var(--space-2);
    padding-left: var(--space-1);
  }
}

/* Touch (7.5): rows are at least 56px, and the action stays visible. */
@media (pointer: coarse) {
  .open {
    min-height: 56px;
  }
}
</style>

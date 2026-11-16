<script setup lang="ts">
// One account on Settings › People (spec E3): avatar, name ("(you)" for me), a status badge
// (Active, Invited or Turned off) and "Site admin" for site admins, the email, when the person was
// last active ("Never signed in" for invited people) and how many workspaces they are in, then the
// "..." menu: "Turn off account" or "Turn on account", and "Sign out everywhere". Turning off your
// own account or another site admin's is disabled with the server's reason as the item's
// description. While an action is on its way the menu's items wait. The page applies the actions.
// Wide rows put the activity on the right; below 560px of list width it becomes a third line.
import { computed } from 'vue'
import { Ellipsis, LogOut, UserRoundCheck, UserRoundX } from '@lucide/vue'
import { formatTimeAgo, formatTimestamp } from '../../lib/dates'
import { pluralize } from '../../lib/format'
import type { AdminUser } from '../../types'
import { useNow } from '../task/useNow'
import UiAvatar from '../ui/UiAvatar.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { MenuItem } from '../ui/menu'

const props = defineProps<{
  user: AdminUser
  isMe: boolean
  /** An action for this account is on its way. */
  busy: boolean
}>()

const emit = defineEmits<{ turnOff: []; turnOn: []; signOut: [] }>()

const now = useNow()

const status = computed<{ label: string; tone: 'success' | 'info' | 'danger' }>(() => {
  if (props.user.disabled) return { label: 'Turned off', tone: 'danger' }
  if (props.user.pending) return { label: 'Invited', tone: 'info' }
  return { label: 'Active', tone: 'success' }
})

const lastSeen = computed(() => (props.user.lastSeenAt ? formatTimeAgo(props.user.lastSeenAt, now.value) : ''))
const lastSeenFull = computed(() => (props.user.lastSeenAt ? formatTimestamp(props.user.lastSeenAt) : ''))

// Someone who signed in before sessions were recorded has no last activity yet, but has signed in.
const noActivity = computed(() => (props.user.pending ? 'Never signed in' : 'No recent sign-in'))

const workspacesText = computed(() =>
  props.user.workspaces === 0 ? 'No workspaces' : pluralize(props.user.workspaces, 'workspace'),
)

/** The server's refusals, shown before anyone tries (spec D). */
const turnOffBlocked = computed<string | null>(() => {
  if (props.isMe) return "You can't turn off your own account."
  if (props.user.isSiteAdmin) return "Site admins can't be turned off here. Remove them from Auth:SiteAdmins first."
  return null
})

function menuItems(): MenuItem[] {
  const toggle: MenuItem = props.user.disabled
    ? { id: 'turn-on', label: 'Turn on account', icon: UserRoundCheck, disabled: props.busy, onSelect: () => emit('turnOn') }
    : {
        id: 'turn-off',
        label: 'Turn off account',
        icon: UserRoundX,
        danger: true,
        disabled: props.busy || !!turnOffBlocked.value,
        hint: turnOffBlocked.value ?? undefined,
        onSelect: () => emit('turnOff'),
      }
  return [
    toggle,
    { id: 'sign-out', label: 'Sign out everywhere', icon: LogOut, disabled: props.busy, onSelect: () => emit('signOut') },
  ]
}
</script>

<template>
  <li class="person" :data-user-id="user.id" :aria-busy="busy ? 'true' : undefined">
    <span class="avatar"><UiAvatar :user="user" :size="32" :pending="user.pending" decorative /></span>
    <div class="name-line">
      <span v-tooltip="{ text: user.displayName, overflowOnly: true }" class="name truncate">{{ user.displayName }}</span>
      <span v-if="isMe" class="you">(you)</span>
      <UiBadge :tone="status.tone">{{ status.label }}</UiBadge>
      <UiBadge v-if="user.isSiteAdmin" tone="accent">Site admin</UiBadge>
    </div>
    <span v-tooltip="{ text: user.email, overflowOnly: true }" class="email truncate">{{ user.email }}</span>
    <p class="meta">
      <span v-if="user.lastSeenAt">Last active <time v-tooltip="lastSeenFull" class="tabular" :datetime="user.lastSeenAt">{{ lastSeen }}</time></span>
      <span v-else>{{ noActivity }}</span>
      <span class="sep" aria-hidden="true">·</span>
      <span>{{ workspacesText }}</span>
    </p>
    <span class="actions">
      <UiMenu :items="menuItems" :aria-label="`Actions for ${user.displayName}`" placement="bottom-end">
        <template #trigger="{ props: trigger }">
          <UiIconButton v-bind="trigger" :icon="Ellipsis" :label="`More actions for ${user.displayName}`" size="sm" />
        </template>
      </UiMenu>
    </span>
  </li>
</template>

<style scoped>
.person {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto auto;
  grid-template-areas:
    "avatar name meta actions"
    "avatar email meta actions";
  align-items: center;
  column-gap: var(--space-3);
  min-height: 60px;
  padding: var(--space-2) var(--space-3) var(--space-2) var(--space-4);
}

.person + .person {
  border-top: 1px solid var(--border-subtle);
}

.avatar {
  grid-area: avatar;
  display: flex;
}

/* The name gives way first; the badges wrap under it rather than being cut. */
.name-line {
  grid-area: name;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-1) var(--space-2);
  min-width: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.name {
  flex: 0 1 auto;
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.you {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.email {
  grid-area: email;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.meta {
  grid-area: meta;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
  text-align: right;
  white-space: nowrap;
}

.sep {
  display: none;
}

.actions {
  grid-area: actions;
  display: flex;
}

/* Narrow lists and phones: the activity becomes a third line under the email. */
@container people (width < 560px) {
  .person {
    grid-template-columns: auto minmax(0, 1fr) auto;
    grid-template-areas:
      "avatar name actions"
      "avatar email actions"
      "avatar meta actions";
    padding: var(--space-3) var(--space-3) var(--space-3) var(--space-4);
  }

  .avatar {
    align-self: start;
  }

  .meta {
    flex-direction: row;
    flex-wrap: wrap;
    align-items: baseline;
    column-gap: var(--space-1-5);
    margin-top: var(--space-0-5);
    text-align: left;
    white-space: normal;
  }

  .sep {
    display: inline;
  }
}
</style>

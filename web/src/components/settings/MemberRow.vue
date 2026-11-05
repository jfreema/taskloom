<script setup lang="ts">
// One row of the Members table (8.13): avatar and name ("(you)" for me), email, an "Invited" badge
// with a "Copy sign-in link for <name>" button for pending people, the role and Remove. The role
// is a select and Remove shows only for Admin+, and never on the Owner's row or your own (5.5),
// which show the role as text. In a narrow table (under 680px, a 1100px window) the email goes under
// the name and the badge follows the name, so short names and emails are never cut. Below 640px the
// row stacks: name and email, then role and Remove.
import { computed } from 'vue'
import { Link, Trash2 } from '@lucide/vue'
import { ROLE_DESCRIPTION, ROLE_LABEL, type AssignableRole, type Member } from '../../types'
import UiAvatar from '../ui/UiAvatar.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiSelect from '../ui/UiSelect.vue'
import type { ListboxOption } from '../ui/listbox'

const props = defineProps<{
  member: Member
  isMe: boolean
  /** Admin+: may change roles and remove people. */
  canManage: boolean
  /** May copy the sign-in link (everyone but guests). */
  canCopy: boolean
}>()

const emit = defineEmits<{ role: [role: AssignableRole]; remove: []; copyLink: [] }>()

const ROLES: AssignableRole[] = ['Admin', 'Member', 'Guest']
const roleOptions: ListboxOption[] = ROLES.map((r) => ({ value: r, label: ROLE_LABEL[r], description: ROLE_DESCRIPTION[r] }))

const user = computed(() => ({ id: props.member.userId, displayName: props.member.displayName }))
const editable = computed(() => props.canManage && !props.isMe && props.member.role !== 'Owner')

function onRole(value: string | null): void {
  if (value && value !== props.member.role && (ROLES as string[]).includes(value)) emit('role', value as AssignableRole)
}
</script>

<template>
  <tr class="member" role="row">
    <td class="cell name-cell" role="cell">
      <div class="identity">
        <span class="avatar"><UiAvatar :user="user" :size="28" :pending="member.pending" decorative /></span>
        <span v-tooltip="{ text: member.displayName, overflowOnly: true }" class="name truncate">{{ member.displayName }}</span>
        <span v-if="isMe" class="you">(you)</span>
      </div>
    </td>
    <!-- Without an invitation the email also takes the Invitation column, so it isn't cut beside an empty cell. -->
    <td class="cell email-cell" role="cell" :colspan="member.pending ? undefined : 2">
      <span v-tooltip="{ text: member.email, overflowOnly: true }" class="email truncate">{{ member.email }}</span>
    </td>
    <td v-if="member.pending" class="cell status-cell" role="cell">
      <UiBadge tone="info">Invited</UiBadge>
      <UiIconButton v-if="canCopy" :icon="Link" :label="`Copy sign-in link for ${member.displayName}`" size="xs" @click="emit('copyLink')" />
    </td>
    <td class="cell role-cell" role="cell">
      <UiSelect
        v-if="editable"
        :model-value="member.role"
        :options="roleOptions"
        :aria-label="`Role for ${member.displayName}`"
        size="sm"
        @update:model-value="onRole"
      />
      <span v-else class="role-text">{{ ROLE_LABEL[member.role] }}</span>
    </td>
    <td class="cell actions-cell" role="cell">
      <UiIconButton v-if="editable" class="remove" :icon="Trash2" :label="`Remove ${member.displayName}`" size="sm" @click="emit('remove')" />
    </td>
  </tr>
</template>

<style scoped>
.member + .member .cell {
  border-top: 1px solid var(--border-subtle);
}

.cell {
  height: 52px;
  padding: var(--space-2) var(--space-2);
  vertical-align: middle;
}

.cell:first-child {
  padding-left: var(--space-4);
}

.cell:last-child {
  padding-right: var(--space-3);
}

/* Names are usually shorter than emails, so the email gets the larger share. */
.name-cell {
  max-width: 0;
  width: 34%;
}

.identity {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}

.avatar {
  display: flex;
  flex: none;
}

.name {
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.you {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.email-cell {
  max-width: 0;
  width: 40%;
}

.email {
  display: block;
  color: var(--text-secondary);
}

.status-cell {
  width: 1%;
  white-space: nowrap;
}

.status-cell :deep(.ui-icon-button) {
  margin-left: var(--space-1);
  vertical-align: middle;
}

.role-cell {
  width: 1%;
  white-space: nowrap;
}

.role-cell :deep(.ui-select) {
  width: 128px;
}

.role-text {
  display: inline-block;
  min-width: 128px;
  padding: 0 var(--space-2);
  color: var(--text-secondary);
}

.actions-cell {
  width: 1%;
  text-align: right;
}

/* Quiet until pointed at: a row of red bins would shout. */
.actions-cell .remove:not(.is-disabled):hover {
  background-color: var(--danger-subtle);
  color: var(--danger-text);
}

/* Narrow tables: two lines per person. The avatar is centred on both; the name, the "(you)" and the
   Invited badge share the first line, the email is the second, and the role and Remove sit on the
   right across both. */
@container members (width < 680px) {
  .member {
    position: relative;
    display: grid;
    grid-template-columns: minmax(0, max-content) auto minmax(0, 1fr) auto auto;
    grid-template-areas:
      "name status . role actions"
      "email email email role actions";
    align-items: center;
    column-gap: var(--space-2);
    padding: var(--space-2) var(--space-3) var(--space-2) calc(var(--space-4) + 28px + var(--space-3));
  }

  .member + .member {
    border-top: 1px solid var(--border-subtle);
  }

  .member + .member .cell {
    border-top: 0;
  }

  .cell,
  .cell:first-child,
  .cell:last-child {
    height: auto;
    padding: 0;
  }

  .avatar {
    position: absolute;
    top: 50%;
    left: var(--space-4);
    transform: translateY(-50%);
  }

  .name-cell {
    grid-area: name;
    width: auto;
    max-width: none;
    min-width: 0;
  }

  .email-cell {
    grid-area: email;
    width: auto;
    max-width: none;
    min-width: 0;
  }

  .email {
    font-size: var(--fs-sm);
    line-height: var(--lh-sm);
  }

  .status-cell {
    grid-area: status;
    display: flex;
    align-items: center;
    width: auto;
  }

  .role-cell {
    grid-area: role;
    width: auto;
    margin-left: var(--space-2);
  }

  .role-text {
    min-width: 0;
  }

  .actions-cell {
    grid-area: actions;
    width: auto;
  }
}

/* Phones: name and email, then role and Remove. */
@media (max-width: 639px) {
  .member {
    display: grid;
    grid-template-columns: minmax(0, max-content) minmax(0, 1fr) auto;
    grid-template-areas:
      "name status status"
      "email email email"
      "role role actions";
    align-items: center;
    padding: var(--space-3) var(--space-4);
    row-gap: var(--space-1);
    column-gap: var(--space-2);
  }

  .avatar {
    position: static;
    transform: none;
  }

  .email {
    font-size: var(--fs-md);
    line-height: var(--lh-md);
  }

  .member + .member {
    border-top: 1px solid var(--border-subtle);
  }

  .member + .member .cell {
    border-top: 0;
  }

  .cell,
  .cell:first-child,
  .cell:last-child {
    height: auto;
    padding: 0;
  }

  .name-cell {
    grid-area: name;
    width: auto;
    max-width: none;
    min-width: 0;
  }

  .email-cell {
    grid-area: email;
    width: auto;
    max-width: none;
    min-width: 0;
    padding-left: calc(28px + var(--space-2));
  }

  .status-cell {
    grid-area: status;
    width: auto;
  }

  .role-cell {
    grid-area: role;
    width: auto;
    margin-left: 0;
    padding-left: calc(28px + var(--space-2));
  }

  .role-text {
    padding: 0;
  }

  .actions-cell {
    grid-area: actions;
    width: auto;
  }

  /* The role line sits a little apart from name and email; both cells get the same top padding
     (beating .cell:last-child above) so the select and Remove stay centred on one line. */
  .cell.role-cell,
  .cell.actions-cell {
    padding-top: var(--space-2);
  }
}
</style>

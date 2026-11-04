<script setup lang="ts">
// One row of the Members table (8.13): avatar and name ("(you)" for me), email, an "Invited" badge
// with a "Copy sign-in link for <name>" button for pending people, the role and Remove. The role
// is a select and Remove shows only for Admin+, and never on the Owner's row or your own (5.5),
// which show the role as text. Below 640px the row stacks: name and email, then role and Remove.
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
        <UiAvatar :user="user" :size="28" :pending="member.pending" decorative />
        <span v-tooltip="{ text: member.displayName, overflowOnly: true }" class="name truncate">{{ member.displayName }}</span>
        <span v-if="isMe" class="you">(you)</span>
      </div>
    </td>
    <td class="cell email-cell" role="cell">
      <span v-tooltip="{ text: member.email, overflowOnly: true }" class="email truncate">{{ member.email }}</span>
    </td>
    <td class="cell status-cell" role="cell">
      <template v-if="member.pending">
        <UiBadge tone="info">Invited</UiBadge>
        <UiIconButton v-if="canCopy" :icon="Link" :label="`Copy sign-in link for ${member.displayName}`" size="xs" @click="emit('copyLink')" />
      </template>
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

.name-cell {
  max-width: 0;
  width: 40%;
}

.identity {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
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
  width: 34%;
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

/* Phones: name and email, then role and Remove. */
@media (max-width: 639px) {
  .member {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      "name status"
      "email email"
      "role actions";
    align-items: center;
    padding: var(--space-3) var(--space-4);
    row-gap: var(--space-1);
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

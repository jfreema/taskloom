<script setup lang="ts">
// Assignee picker (7.11): a multiple-select, always-searchable listbox. The first row is "Assign
// to me" (or "Unassign me"); then the members, me first, then by name. Descriptions: the email,
// "Invited" for pending members, "Guest · view and comment only" for guests. Assigned people who
// left the workspace show as "Former member" so they can be removed. Admins get an "Invite people"
// footer (it passes the query when it looks like an email, and `taskIds` to assign); others see
// "Not in this workspace. Ask an admin to invite them." when nothing matches.
// Triggers, named "Assignees: <names>, change" or "Assignees: none, add": 'stack' (an avatar
// stack), 'field' (avatars and names), 'chip' (quick create).
import { computed, nextTick } from 'vue'
import { UserPlus, UserRoundPlus, X } from '@lucide/vue'
import { isValidEmail } from '../../lib/format'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import type { ListboxOption } from '../ui/listbox'
import UiAvatar from '../ui/UiAvatar.vue'
import UiAvatarStack from '../ui/UiAvatarStack.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiListbox from '../ui/UiListbox.vue'
import UiPopover from '../ui/UiPopover.vue'
import FieldTrigger from './FieldTrigger.vue'
import { joinNames, triggerLabel } from './fieldLabel'
import { queryNear } from './pickerQuery'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: Uuid[]
    /** The tasks being edited: "Invite people" assigns the new person to them. */
    taskIds?: Uuid[]
    variant?: 'stack' | 'field' | 'chip'
    max?: number
    size?: 16 | 20 | 24 | 28
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
  }>(),
  { taskIds: () => [], variant: 'stack', max: 3, size: 24, readonly: false, anchor: null, hideTrigger: false },
)

const emit = defineEmits<{ 'update:modelValue': [ids: Uuid[]] }>()

const ME = '@me'

const members = useMembers()
const session = useSession()
const workspaces = useWorkspaces()
const ui = useUi()

const meId = computed(() => session.user?.id ?? null)
const canInvite = computed(() => workspaces.isAdmin())

const people = computed(() => props.modelValue.map((id) => ({ id, displayName: members.nameOf(id) })))
const label = computed(() => triggerLabel('Assignees', joinNames(people.value.map((p) => p.displayName))))
const chipText = computed(() => {
  const n = people.value.length
  if (n === 0) return 'Assignees'
  return n === 1 ? people.value[0].displayName : `${n} people`
})

function describe(userId: Uuid): string | undefined {
  const m = members.get(userId)
  if (!m) return undefined
  if (m.role === 'Guest') return 'Guest · view and comment only'
  if (m.pending) return 'Invited'
  return m.email
}

function options(): ListboxOption[] {
  const me = meId.value
  const chosen = new Set(props.modelValue)
  const out: ListboxOption[] = []
  if (me) out.push({ value: ME, label: chosen.has(me) ? 'Unassign me' : 'Assign to me', keywords: ['me'] })
  const list = members.current
  const mine = list.find((m) => m.userId === me)
  for (const m of mine ? [mine, ...list.filter((x) => x !== mine)] : list) {
    out.push({ value: m.userId, label: m.displayName, description: describe(m.userId), keywords: [m.email] })
  }
  for (const id of props.modelValue) {
    if (!members.get(id) && id !== me) out.push({ value: id, label: 'Former member' })
  }
  return out
}

function isPending(id: string): boolean {
  return members.get(id)?.pending ?? false
}

function update(next: string | string[] | null): void {
  const values = Array.isArray(next) ? next : []
  let ids = values.filter((v) => v !== ME)
  if (values.includes(ME) && meId.value) {
    const me = meId.value
    ids = props.modelValue.includes(me) ? props.modelValue.filter((id) => id !== me) : [...props.modelValue, me]
  }
  emit('update:modelValue', ids)
}

async function invite(e: MouseEvent): Promise<void> {
  const q = queryNear(e.currentTarget)
  open.value = false
  await nextTick()
  ui.openInvite({ ...(isValidEmail(q) ? { email: q } : {}), assignTo: [...props.taskIds] })
}
</script>

<template>
  <template v-if="readonly">
    <UiAvatarStack v-if="variant === 'stack'" v-bind="$attrs" :users="people" :max="max" :size="size" />
    <span v-else v-bind="$attrs" class="static" :class="`static--${variant}`">
      <template v-if="people.length">
        <span v-for="p in people" :key="p.id" class="person">
          <UiAvatar :user="p" :size="variant === 'chip' ? 16 : 20" decorative />
          <span class="person-name">{{ p.displayName }}</span>
        </span>
      </template>
      <span v-else class="muted">Unassigned</span>
    </span>
  </template>
  <UiPopover v-else v-model:open="open" role="listbox" :anchor="anchor" :width="300">
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="variant === 'stack' ? 'round' : variant === 'chip' ? 'chip' : 'ghost'"
        :label="label"
        :empty="people.length === 0"
      >
        <UiAvatarStack v-if="variant === 'stack'" :users="people" :max="max" :size="size" />
        <template v-else-if="variant === 'chip'">
          <UiAvatarStack v-if="people.length" :users="people" :max="3" :size="16" />
          <UiIcon v-else :icon="UserRoundPlus" :size="16" />
          <span class="chip-text">{{ chipText }}</span>
        </template>
        <template v-else>
          <template v-if="people.length">
            <span v-for="p in people" :key="p.id" class="person">
              <UiAvatar :user="p" :size="20" :pending="isPending(p.id)" decorative />
              <span class="person-name">{{ p.displayName }}</span>
            </span>
          </template>
          <template v-else>
            <UiIcon :icon="UserRoundPlus" :size="16" />
            <span>Add</span>
          </template>
        </template>
      </FieldTrigger>
    </template>
    <template #default>
      <UiListbox
        :options="options"
        :model-value="modelValue"
        multiple
        :searchable="true"
        search-placeholder="Search people"
        aria-label="Assignees"
        @update:model-value="update"
      >
        <template #option="{ option }">
          <span v-if="option.value === ME" class="me-icon" aria-hidden="true">
            <UiIcon :icon="modelValue.includes(meId ?? '') ? X : UserRoundPlus" :size="16" />
          </span>
          <UiAvatar
            v-else
            :user="{ id: option.value, displayName: option.label }"
            :size="20"
            :pending="isPending(option.value)"
            decorative
          />
          <span class="option-text">
            <span class="option-label">{{ option.label }}</span>
            <span v-if="option.description" class="option-desc">{{ option.description }}</span>
          </span>
        </template>
        <template #empty="{ query }">
          <span v-if="query && !canInvite">Not in this workspace. Ask an admin to invite them.</span>
          <span v-else-if="query">No one matches “{{ query }}”.</span>
          <span v-else>No members yet.</span>
        </template>
        <template v-if="canInvite" #footer>
          <button type="button" class="footer-action focus-inset" @click="invite">
            <UiIcon :icon="UserPlus" :size="16" />
            <span>Invite people</span>
          </button>
        </template>
      </UiListbox>
    </template>
  </UiPopover>
</template>

<style scoped>
.static {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1) var(--space-2);
  min-width: 0;
  min-height: var(--control-md);
  padding: var(--space-1) var(--space-2);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.static--chip {
  min-height: var(--control-sm);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.person {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
}

.person-name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.muted {
  color: var(--text-tertiary);
}

.chip-text {
  min-width: 0;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.me-icon {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: var(--radius-full);
  background: var(--accent-subtle);
  color: var(--accent-text);
}

.option-text {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}

.option-label,
.option-desc {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.option-desc {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.footer-action {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-height: var(--row-menu);
  padding: var(--space-1-5) var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--accent-text);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  text-align: left;
  cursor: pointer;
}

.footer-action:hover {
  background: var(--bg-hover);
}
</style>

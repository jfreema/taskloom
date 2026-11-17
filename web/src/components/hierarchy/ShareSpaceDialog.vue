<script setup lang="ts">
// Share space (Security 3), Member+, opened by ui.openShareSpace(spaceId) from the space menu.
// - "Who can see it": Everyone in the workspace, or Private. Making it private keeps it to its
//   people, starting with you; making it public again asks first, then forgets its people.
// - For a private space, a searchable checklist of the workspace's members: ticking adds a person,
//   unticking takes them out, at once. Unticking yourself asks first and closes the dialog, since
//   the space then disappears for you. The last person can't be taken out.
// Changes apply immediately (the store is optimistic and toasts failures); Done just closes.
import { computed, ref, useId, watch } from 'vue'
import { useConfirm } from '../../composables/useConfirm'
import { pluralize } from '../../lib/format'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import { ROLE_LABEL, type Uuid } from '../../types'
import type { ListboxOption } from '../ui/listbox'
import UiAvatar from '../ui/UiAvatar.vue'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiListbox from '../ui/UiListbox.vue'
import UiRadioCards from '../ui/UiRadioCards.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const members = useMembers()
const session = useSession()
const confirm = useConfirm()
const uid = useId()
const whoLabelId = `${uid}-who`
const peopleLabelId = `${uid}-people`

const spaceId = ref<Uuid | null>(null)
const notice = ref('')

const open = useRequestedDialog(
  () => ui.shareSpace,
  () => {
    ui.shareSpace = null
  },
  (r) => {
    spaceId.value = r.spaceId
    notice.value = ''
    void members.ensure().catch(() => undefined)
  },
)

const found = computed(() => (spaceId.value ? workspaces.findSpace(spaceId.value) : null))
const space = computed(() => found.value?.space ?? null)
// The name stays while the dialog fades out after the space went away.
const title = ref('')
watch(
  () => space.value?.name,
  (name) => {
    if (name) title.value = `Share “${name}”`
  },
  { immediate: true },
)

// Losing the role, the space or the workspace closes it.
watch(
  () => [found.value ? workspaces.canEdit(found.value.workspace.id) : false, workspaces.currentId] as const,
  ([canEdit, id], [, wasId]) => {
    if (open.value && (!canEdit || id !== wasId)) open.value = false
  },
)

const meId = computed(() => session.user?.id ?? null)
const workspaceName = computed(() => found.value?.workspace.name ?? 'the workspace')

const visibility = computed<string>({
  get: () => (space.value?.isPrivate ? 'private' : 'everyone'),
  set: (value) => void setVisibility(value === 'private'),
})

const visibilityOptions = computed(() => [
  { value: 'everyone', label: 'Everyone', description: `Everyone in ${workspaceName.value} can see it.` },
  { value: 'private', label: 'Private', description: 'Only the people you choose can see it.' },
])

async function setVisibility(isPrivate: boolean): Promise<void> {
  const s = space.value
  if (!s || s.isPrivate === isPrivate) return
  notice.value = ''
  if (!isPrivate) {
    const lists = s.lists.length
    const ok = await confirm({
      title: `Show “${s.name}” to everyone?`,
      message: `Everyone in ${workspaceName.value} will see it${lists ? `, its ${pluralize(lists, 'list')} and their tasks` : ''}. Its people list is cleared.`,
      confirmLabel: 'Show to everyone',
    })
    if (!ok) return
  }
  try {
    await workspaces.setSpacePrivate(s.id, isPrivate)
  } catch {
    // Reported by the store.
  }
}

function describe(userId: Uuid): string {
  const m = members.get(userId)
  if (!m) return ''
  const parts: string[] = []
  if (userId === meId.value) parts.push('You')
  if (m.pending) parts.push('Invited')
  if (m.role === 'Guest') parts.push('Guest · view and comment only')
  else if (m.role === 'Owner' || m.role === 'Admin') parts.push(ROLE_LABEL[m.role])
  parts.push(m.email)
  return parts.join(' · ')
}

/** The workspace's members: the space's people first (in the order they were added), then everyone else by name. */
function options(): ListboxOption[] {
  const s = space.value
  if (!s) return []
  const inSpace = new Set(s.memberIds)
  const people = s.memberIds.map((id) => members.get(id)).filter((m) => !!m)
  const others = members.current.filter((m) => !inSpace.has(m.userId))
  return [...people, ...others].map((m) => ({
    value: m.userId,
    label: m.displayName,
    description: describe(m.userId),
    keywords: [m.email],
  }))
}

const summary = computed(() => {
  const n = space.value?.memberIds.length ?? 0
  return `${pluralize(n, 'person', 'people')} can see this space. Their role in ${workspaceName.value} still decides what they can change; owners and admins who aren't listed can't see it either.`
})

async function setPeople(next: string | string[] | null): Promise<void> {
  const s = space.value
  if (!s) return
  const wanted = Array.isArray(next) ? next : []
  const added = wanted.filter((id) => !s.memberIds.includes(id))
  const removed = s.memberIds.filter((id) => !wanted.includes(id))
  notice.value = ''
  if (added.length) void workspaces.addSpaceMembers(s.id, added).catch(() => undefined)
  for (const id of removed) {
    if (s.memberIds.length <= 1) {
      notice.value = 'A private space needs at least one person. Show it to everyone, or delete it, instead.'
      return
    }
    if (id === meId.value) {
      const ok = await confirm({
        title: `Leave “${s.name}”?`,
        message: "You won't see this space or anything in it until someone adds you back.",
        confirmLabel: 'Leave space',
        danger: true,
      })
      if (!ok) continue
      open.value = false
    }
    void workspaces.removeSpaceMember(s.id, id).catch(() => undefined)
  }
}
</script>

<template>
  <UiDialog v-model:open="open" :title="title" description="Choose who can see this space and everything in it." size="md">
    <div v-if="space" class="body">
      <div class="group">
        <span :id="whoLabelId" class="label">Who can see it</span>
        <UiRadioCards v-model="visibility" :options="visibilityOptions" :name="`${uid}-who`" :columns="2" :aria-labelledby="whoLabelId" />
      </div>
      <div v-if="space.isPrivate" class="group">
        <span :id="peopleLabelId" class="label">People</span>
        <p class="hint">{{ summary }}</p>
        <UiInlineAlert v-if="notice" tone="warning">{{ notice }}</UiInlineAlert>
        <div class="people">
          <UiListbox
            :options="options"
            :model-value="space.memberIds"
            multiple
            :searchable="true"
            search-placeholder="Search people"
            :max-height="280"
            aria-label="People who can see this space"
            @update:model-value="setPeople"
          >
            <template #option="{ option }">
              <UiAvatar :user="{ id: option.value, displayName: option.label }" :size="20" :pending="members.get(option.value)?.pending" decorative />
              <span class="option-text">
                <span class="option-label">{{ option.label }}</span>
                <span v-if="option.description" class="option-desc">{{ option.description }}</span>
              </span>
            </template>
            <template #empty="{ query }">
              <span v-if="query">No one in this workspace matches “{{ query }}”.</span>
              <span v-else>No members yet.</span>
            </template>
          </UiListbox>
        </div>
      </div>
    </div>
    <template #footer>
      <UiButton variant="primary" @click="open = false">Done</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.hint {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.people {
  border: 1px solid var(--border-default);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.option-text {
  display: flex;
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
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}
</style>

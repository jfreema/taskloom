<script setup lang="ts">
// The bulk bar's Assignees button (8.7): "Add assignee ›" (the workspace's members, you first),
// "Remove assignee ›" (the people on any selected task) and "Unassign all".
import { computed, h, onMounted, ref } from 'vue'
import { Users } from '@lucide/vue'
import { pluralize } from '../../lib/format'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import type { Uuid } from '../../types'
import type { MenuItem } from '../ui/menu'
import UiAvatar from '../ui/UiAvatar.vue'
import UiMenu from '../ui/UiMenu.vue'
import BulkButton from './BulkButton.vue'
import { menuGlyphs } from './menuGlyph'
import type { BulkActions } from './useBulkActions'

const props = defineProps<{ actions: BulkActions; iconOnly: boolean }>()

const members = useMembers()
const session = useSession()
const open = ref(false)

onMounted(() => void members.ensure().catch(() => undefined))

/** Each person's avatar as their item's icon (dashed while invited). */
const avatarOf = menuGlyphs<Uuid>((id, cls) =>
  h(UiAvatar, {
    user: { id, displayName: members.nameOf(id) },
    size: 16,
    pending: members.get(id)?.pending === true,
    decorative: true,
    class: cls,
  }),
)

/** How many selected tasks each person is on. */
const counts = computed(() => {
  const out = new Map<Uuid, number>()
  for (const t of props.actions.selected.value) for (const id of t.assigneeIds) out.set(id, (out.get(id) ?? 0) + 1)
  return out
})

function addItems(): MenuItem[] {
  const total = props.actions.selected.value.length
  const me = session.user?.id
  const list = members.current
  const mine = list.find((m) => m.userId === me)
  const ordered = mine ? [mine, ...list.filter((m) => m !== mine)] : list
  return ordered.map((m) => {
    const onAll = (counts.value.get(m.userId) ?? 0) >= total
    const note = m.role === 'Guest' ? 'Guest' : m.pending ? 'Invited' : undefined
    return {
      id: `add-${m.userId}`,
      label: m.displayName,
      icon: avatarOf(m.userId),
      hint: onAll ? 'Already on every task' : note,
      disabled: onAll,
      onSelect: () => props.actions.addAssignee(m.userId),
    }
  })
}

function removeItems(): MenuItem[] {
  return [...counts.value.entries()]
    .map(([id, n]) => ({ id, n, name: members.nameOf(id) }))
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(({ id, n, name }) => ({
      id: `remove-${id}`,
      label: name,
      icon: avatarOf(id),
      hint: `On ${pluralize(n, 'task')}`,
      onSelect: () => props.actions.removeAssignee(id),
    }))
}

function items(): MenuItem[] {
  const anyone = counts.value.size > 0
  return [
    { kind: 'submenu', id: 'add-assignee', label: 'Add assignee', items: addItems() },
    { kind: 'submenu', id: 'remove-assignee', label: 'Remove assignee', disabled: !anyone, items: removeItems() },
    { kind: 'separator', id: 'sep-unassign' },
    { id: 'unassign-all', label: 'Unassign all', disabled: !anyone, onSelect: () => props.actions.unassignAll() },
  ]
}
</script>

<template>
  <UiMenu v-model:open="open" :items="items" aria-label="Assignees" placement="top-start" :min-width="220">
    <template #trigger="{ props: t }">
      <BulkButton v-bind="t" :icon="Users" label="Assignees" :icon-only="iconOnly" />
    </template>
  </UiMenu>
</template>

<script setup lang="ts">
// The bulk bar's Status button (8.7): a picker over the list's statuses, grouped by category, with
// the status checked when every selected task has it. 1–9 pick a status while it is open.
import { computed, h, ref } from 'vue'
import { CircleDot } from '@lucide/vue'
import { digitOf, useKeysWhileOpen } from '../fields/pickerKeys'
import StatusIcon from '../fields/StatusIcon.vue'
import { useWorkspaces } from '../../stores/workspaces'
import { STATUS_CATEGORY_LABEL, type StatusCategory, type Uuid } from '../../types'
import type { MenuItem } from '../ui/menu'
import UiMenu from '../ui/UiMenu.vue'
import BulkButton from './BulkButton.vue'
import { menuGlyphs } from './menuGlyph'
import type { BulkActions } from './useBulkActions'

const props = defineProps<{ actions: BulkActions; listId: Uuid; iconOnly: boolean }>()

const workspaces = useWorkspaces()
const open = ref(false)

const statuses = computed(() => workspaces.statusesFor(props.listId))
const shared = computed<Uuid | null>(() => {
  const ids = new Set(props.actions.selected.value.map((t) => t.statusId))
  return ids.size === 1 ? [...ids][0] : null
})

/** Each status's own icon as its item's icon. */
const glyphOf = menuGlyphs<Uuid>((id, cls) => {
  const status = workspaces.statusById(id)
  return status ? h(StatusIcon, { status, size: 16, class: cls }) : null
})

function pick(statusId: Uuid): void {
  open.value = false
  props.actions.setStatus(statusId)
}

function items(): MenuItem[] {
  const out: MenuItem[] = []
  let category: StatusCategory | null = null
  for (const s of statuses.value) {
    if (s.category !== category) {
      category = s.category
      out.push({ kind: 'label', id: `label-${s.category}`, label: STATUS_CATEGORY_LABEL[s.category] })
    }
    out.push({
      kind: 'radio',
      id: `status-${s.id}`,
      label: s.name,
      icon: glyphOf(s.id),
      checked: shared.value === s.id,
      onSelect: () => pick(s.id),
    })
  }
  return out
}

useKeysWhileOpen(open, ['1', '2', '3', '4', '5', '6', '7', '8', '9'], (e) => {
  const n = digitOf(e)
  const s = n ? statuses.value[n - 1] : undefined
  if (!s) return false
  pick(s.id)
})
</script>

<template>
  <UiMenu v-model:open="open" :items="items" aria-label="Status" placement="top-start" :min-width="220">
    <template #trigger="{ props: t }">
      <BulkButton v-bind="t" :icon="CircleDot" label="Status" :icon-only="iconOnly" />
    </template>
  </UiMenu>
</template>

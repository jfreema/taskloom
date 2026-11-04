<script setup lang="ts">
// The bulk bar's Priority button (8.7): Urgent, High, Normal, Low and "Clear priority", as in the
// priority picker (1–4 and 0 while it is open), with the shared priority checked.
import { computed, ref } from 'vue'
import { Flag } from '@lucide/vue'
import { digitOf, useKeysWhileOpen } from '../fields/pickerKeys'
import { PRIORITY_GLYPHS } from '../fields/priorityGlyphs'
import { PRIORITY_LABEL, type Priority } from '../../types'
import type { MenuItem } from '../ui/menu'
import UiMenu from '../ui/UiMenu.vue'
import BulkButton from './BulkButton.vue'
import type { BulkActions } from './useBulkActions'

const props = defineProps<{ actions: BulkActions; iconOnly: boolean }>()

const CHOICES: Priority[] = ['Urgent', 'High', 'Normal', 'Low']

const open = ref(false)
const shared = computed<Priority | null>(() => {
  const values = new Set(props.actions.selected.value.map((t) => t.priority))
  return values.size === 1 ? [...values][0] : null
})

function pick(p: Priority): void {
  open.value = false
  props.actions.setPriority(p)
}

function items(): MenuItem[] {
  return [
    ...CHOICES.map(
      (p): MenuItem => ({
        kind: 'radio',
        id: `priority-${p}`,
        label: PRIORITY_LABEL[p],
        icon: PRIORITY_GLYPHS[p],
        checked: shared.value === p,
        onSelect: () => pick(p),
      }),
    ),
    { kind: 'separator', id: 'priority-sep' },
    { id: 'priority-None', label: 'Clear priority', onSelect: () => pick('None') },
  ]
}

useKeysWhileOpen(open, ['1', '2', '3', '4', '0'], (e) => {
  const n = digitOf(e)
  if (n === null) return false
  const p = n === 0 ? 'None' : CHOICES[n - 1]
  if (!p) return false
  pick(p)
})
</script>

<template>
  <UiMenu v-model:open="open" :items="items" aria-label="Priority" placement="top-start" :min-width="200">
    <template #trigger="{ props: t }">
      <BulkButton v-bind="t" :icon="Flag" label="Priority" :icon-only="iconOnly" />
    </template>
  </UiMenu>
</template>

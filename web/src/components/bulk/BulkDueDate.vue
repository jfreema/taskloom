<script setup lang="ts">
// The bulk bar's Due date button (8.7): a date picker (quick picks, the month, "No due date"). The
// shared due date is selected when every selected task has it. A task whose start date would fall
// after the new due date gets its start date moved to it too.
import { computed, ref } from 'vue'
import { Calendar } from '@lucide/vue'
import type { IsoDate } from '../../types'
import UiDatePicker from '../ui/UiDatePicker.vue'
import UiPopover from '../ui/UiPopover.vue'
import BulkButton from './BulkButton.vue'
import type { BulkActions } from './useBulkActions'

const props = defineProps<{ actions: BulkActions; iconOnly: boolean }>()

const open = ref(false)

const shared = computed<IsoDate | null>(() => {
  const dates = new Set(props.actions.selected.value.map((t) => t.dueDate))
  return dates.size === 1 ? ([...dates][0] ?? null) : null
})

function pick(date: IsoDate | null): void {
  open.value = false
  props.actions.setDueDate(date)
}
</script>

<template>
  <UiPopover v-model:open="open" placement="top-start" aria-label="Due date" width="auto">
    <template #trigger="{ props: t }">
      <BulkButton v-bind="t" :icon="Calendar" label="Due date" :icon-only="iconOnly" />
    </template>
    <template #default>
      <UiDatePicker :model-value="shared" clear-label="No due date" @update:model-value="pick" @cancel="open = false" />
    </template>
  </UiPopover>
</template>

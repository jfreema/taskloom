<script setup lang="ts">
// "More task actions" (8.10): Move to list… (top-level tasks), then Delete task {Mod+Backspace}.
// Shown to Members and above only; the caller decides what happens after a delete.
import { computed } from 'vue'
import { Ellipsis, FolderInput, Trash2 } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import type { Task } from '../../types'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { MenuItem } from '../ui/menu'

const props = withDefaults(defineProps<{ task: Task; size?: 'sm' | 'md' | 'lg' }>(), { size: 'md' })
const emit = defineEmits<{ delete: [] }>()

const actions = useTaskActions()

const items = computed<MenuItem[]>(() => {
  const out: MenuItem[] = []
  if (props.task.parentTaskId === null) {
    out.push({ id: 'move', label: 'Move to list…', icon: FolderInput, onSelect: () => actions.openMoveDialog([props.task.id]) })
    out.push({ kind: 'separator', id: 'sep' })
  }
  out.push({ id: 'delete', label: 'Delete task', icon: Trash2, kbd: 'Mod+Backspace', danger: true, onSelect: () => emit('delete') })
  return out
})
</script>

<template>
  <UiMenu :items="items" placement="bottom-end" aria-label="Task actions">
    <template #trigger="{ props: t }">
      <UiIconButton v-bind="t" :icon="Ellipsis" label="More task actions" :size="size" />
    </template>
  </UiMenu>
</template>

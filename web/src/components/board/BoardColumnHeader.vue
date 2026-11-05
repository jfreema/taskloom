<script setup lang="ts">
// A column's 40px header (8.8): the status icon, the name (14/600), the count, then for Member+
// the "+" that opens the add card at the top and the column menu: Rename (inline), Colour,
// Collapse column, Edit statuses…, and Delete status… (the delete dialog).
import { computed, ref } from 'vue'
import { ChevronsRightLeft, Ellipsis, ListChecks, Pencil, Plus, Trash2 } from '@lucide/vue'
import { noop } from '../../lib/async'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status } from '../../types'
import StatusIcon from '../fields/StatusIcon.vue'
import type { MenuItem } from '../ui/menu'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import UiMenu from '../ui/UiMenu.vue'
import { useBoard } from './boardContext'

const props = defineProps<{
  status: Status
  count: number
}>()

const emit = defineEmits<{ add: []; collapse: [] }>()

const board = useBoard()
const ui = useUi()
const workspaces = useWorkspaces()

const renaming = ref(false)
const menuOpen = ref(false)
const name = computed(() => props.status.name)

function items(): MenuItem[] {
  const s = props.status
  return [
    { id: 'rename', label: 'Rename', icon: Pencil, onSelect: () => (renaming.value = true) },
    {
      kind: 'swatches',
      id: 'colour',
      label: 'Colour',
      value: s.color,
      onSelect: (hex: string) => void workspaces.updateStatus(s.id, { color: hex }).catch(noop),
    },
    { id: 'collapse', label: 'Collapse column', icon: ChevronsRightLeft, onSelect: () => emit('collapse') },
    { id: 'edit', label: 'Edit statuses…', icon: ListChecks, onSelect: () => ui.openStatusEditor(board.listId.value) },
    { kind: 'separator', id: 'sep-delete' },
    { id: 'delete', label: 'Delete status…', icon: Trash2, danger: true, onSelect: () => ui.openDeleteStatus(s.id) },
  ]
}

function save(value: string): void {
  void workspaces.updateStatus(props.status.id, { name: value }).catch(noop)
}
</script>

<template>
  <header class="column-header">
    <StatusIcon :status="status" :size="16" />
    <UiInlineEdit
      v-if="renaming"
      v-model:editing="renaming"
      class="rename"
      :model-value="name"
      aria-label="Status name"
      activate="none"
      :maxlength="100"
      @save="save"
    />
    <template v-else>
      <h2 v-tooltip="{ text: name, overflowOnly: true }" class="name truncate">{{ name }}</h2>
      <span class="count tabular" aria-hidden="true">{{ count }}</span>
    </template>
    <span class="actions">
      <template v-if="board.canEdit.value">
        <UiIconButton :icon="Plus" :label="`Add task to ${name}`" size="xs" data-add-top @click="emit('add')" />
        <UiMenu v-model:open="menuOpen" :items="items" :aria-label="`Actions for ${name}`" placement="bottom-end">
          <template #trigger="{ props: t }">
            <UiIconButton v-bind="t" :icon="Ellipsis" :label="`Column actions for ${name}`" size="xs" />
          </template>
        </UiMenu>
      </template>
    </span>
  </header>
</template>

<style scoped>
.column-header {
  display: flex;
  flex: none;
  align-items: center;
  gap: var(--space-2);
  height: 40px;
  padding: 0 var(--space-1-5) 0 var(--space-3);
}

.name {
  margin: 0;
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.count {
  flex: none;
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
}

.rename {
  flex: 1 1 auto;
  min-width: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.actions {
  display: inline-flex;
  flex: none;
  align-items: center;
  gap: var(--space-0-5);
  margin-left: auto;
}

/* Touch (7.5): a 44px header, and the two 24px buttons far enough apart that their 44px targets
   don't overlap. */
@media (pointer: coarse) {
  .column-header {
    height: var(--touch-min);
  }

  .actions {
    gap: var(--space-5);
  }
}
</style>

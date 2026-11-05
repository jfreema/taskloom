<script setup lang="ts">
// One status in the status editor (8.13). The row is focusable and named by the status. It holds
// the colour button, the name (UiInlineEdit), the number of tasks (only when the list's tasks are
// loaded) and the "..." menu: Move up, Move down, Move to <other sections>, Delete…, each disabled
// with the reason as its hint when a guard forbids it. Keys: Alt+Up and Alt+Down move the status
// (from the row or its buttons), F2 renames, Shift+F10 or the ContextMenu key opens the menu.
// The row only asks; the dialog applies the change, keeps focus with the row and announces moves.
import { computed, ref } from 'vue'
import { ArrowDown, ArrowUp, Ellipsis, Trash2 } from '@lucide/vue'
import { pluralize } from '../../lib/format'
import type { Status, StatusCategory } from '../../types'
import StatusIcon from '../fields/StatusIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { MenuItem } from '../ui/menu'
import SwatchPopover from './SwatchPopover.vue'
import { CATEGORIES, deleteBlocked, moveBlocked, sectionLabel, stepMove } from './statusRules'

const props = defineProps<{
  status: Status
  /** Every status of the list in display order, for the guards and the moves. */
  statuses: readonly Status[]
  /** Tasks in this status; null while the list's tasks aren't loaded (the count is hidden). */
  count: number | null
}>()

const emit = defineEmits<{
  step: [dir: 'up' | 'down']
  moveTo: [to: StatusCategory]
  rename: [name: string]
  recolour: [hex: string]
  remove: []
}>()

const renaming = ref(false)
const menuOpen = ref(false)

const countText = computed(() => {
  if (props.count === null) return ''
  return props.count === 0 ? 'No tasks' : pluralize(props.count, 'task')
})

function menuItems(): MenuItem[] {
  const s = props.status
  const all = props.statuses
  const items: MenuItem[] = [
    { id: 'up', label: 'Move up', icon: ArrowUp, kbd: 'Alt+ArrowUp', disabled: !stepMove(s, 'up', all), onSelect: () => emit('step', 'up') },
    {
      id: 'down',
      label: 'Move down',
      icon: ArrowDown,
      kbd: 'Alt+ArrowDown',
      disabled: !stepMove(s, 'down', all),
      onSelect: () => emit('step', 'down'),
    },
  ]
  for (const c of CATEGORIES) {
    if (c === s.category) continue
    const why = moveBlocked(s, c, all)
    items.push({ id: `to-${c}`, label: `Move to ${sectionLabel(c)}`, disabled: !!why, hint: why ?? undefined, onSelect: () => emit('moveTo', c) })
  }
  const deleteWhy = deleteBlocked(s, all)
  items.push(
    { kind: 'separator', id: 'sep-delete' },
    { id: 'delete', label: 'Delete…', icon: Trash2, danger: true, disabled: !!deleteWhy, hint: deleteWhy ?? undefined, onSelect: () => emit('remove') },
  )
  return items
}

function plain(e: KeyboardEvent): boolean {
  return !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey
}

function onKeydown(e: KeyboardEvent): void {
  if (e.target instanceof HTMLElement && e.target.closest('input, textarea')) return
  if ((e.key === 'ArrowUp' || e.key === 'ArrowDown') && e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
    e.preventDefault()
    e.stopPropagation()
    emit('step', e.key === 'ArrowUp' ? 'up' : 'down')
    return
  }
  // The rest only on the row itself, so the name and the buttons keep their own keys.
  if (e.target !== e.currentTarget) return
  if (e.key === 'F2' && plain(e)) {
    e.preventDefault()
    renaming.value = true
  } else if ((e.key === 'F10' && e.shiftKey && !e.altKey && !e.ctrlKey && !e.metaKey) || e.key === 'ContextMenu') {
    e.preventDefault()
    menuOpen.value = true
  }
}
</script>

<template>
  <li class="row focus-inset" tabindex="0" :aria-label="status.name" :data-status-id="status.id" data-status-row @keydown="onKeydown">
    <SwatchPopover :color="status.color" :label="`Colour for ${status.name}`" @pick="emit('recolour', $event)">
      <StatusIcon :status="status" :size="16" />
    </SwatchPopover>
    <span class="name">
      <UiInlineEdit
        v-model:editing="renaming"
        :model-value="status.name"
        aria-label="Status name"
        :maxlength="100"
        @save="emit('rename', $event)"
      />
    </span>
    <span v-if="count !== null" class="count tabular">{{ countText }}</span>
    <UiMenu v-model:open="menuOpen" :items="menuItems" :aria-label="`Actions for ${status.name}`" placement="bottom-end">
      <template #trigger="{ props: trigger }">
        <UiIconButton v-bind="trigger" :icon="Ellipsis" :label="`More actions for ${status.name}`" size="sm" />
      </template>
    </UiMenu>
  </li>
</template>

<style scoped>
.row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: 44px;
  padding: var(--space-1) var(--space-1-5) var(--space-1) var(--space-2);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  transition: background-color var(--dur-1) var(--ease-out);
}

.row + .row {
  border-top: 1px solid var(--border-subtle);
}

.row:hover {
  background: var(--bg-hover);
}

.name {
  display: flex;
  flex: 1;
  min-width: 0;
  font-weight: var(--fw-medium);
}

.count {
  flex-shrink: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
  white-space: nowrap;
}

/* Touch (7.5): the name takes at least a 44px-wide tap that starts clear of the colour button's hit
   area, and the menu button's hit area keeps its full width at the end of the row. */
@media (pointer: coarse) {
  .row {
    gap: var(--space-3);
    padding-right: var(--space-2);
  }

  .name :deep(.view) {
    min-width: var(--touch-min);
  }
}
</style>

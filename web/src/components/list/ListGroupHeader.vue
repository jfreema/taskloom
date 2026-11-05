<script setup lang="ts">
// A group header row of the List (8.7): one spanning gridcell, sticky under the column header.
// It holds the group checkbox (every visible top-level task of the group; indeterminate when only
// some are selected), the disclosure button named by the group label, the count, and, revealed on
// the right, "+" (an add row under the header that creates at the top) and, for status groups
// (Member+), a menu: Rename status (inline), Colour, Edit statuses…
import { computed, ref, watchEffect } from 'vue'
import { ChevronRight, Ellipsis, ListChecks, Pencil, Plus } from '@lucide/vue'
import type { MenuItem } from '../ui/menu'
import { noop } from '../../lib/async'
import { useSelection } from '../../stores/selection'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import PriorityFlag from '../fields/PriorityFlag.vue'
import StatusIcon from '../fields/StatusIcon.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiIcon from '../ui/UiIcon.vue'
import LazyMenu from '../fields/LazyMenu.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import { useListTable } from './listTable'
import { headerKey, type GroupView } from './useListModel'

const props = defineProps<{
  view: GroupView
  tabStop: boolean
  drop: 'top' | 'bottom' | null
}>()

const table = useListTable()
const selection = useSelection()
const workspaces = useWorkspaces()
const ui = useUi()

const rowEl = ref<HTMLElement | null>(null)
const checkbox = ref<InstanceType<typeof UiCheckbox> | null>(null)
const renaming = ref(false)
const menuOpen = ref(false)

const g = computed(() => props.view.group)
const status = computed(() => g.value.status ?? null)
const label = computed(() => g.value.label)
const inner = computed(() => (props.tabStop ? undefined : -1))
const count = computed(() => props.view.topIds.length)

const selectedCount = computed(() => {
  const ids = selection.ids
  let n = 0
  for (const id of props.view.topIds) if (ids.has(id)) n++
  return n
})
const allSelected = computed(() => count.value > 0 && selectedCount.value === count.value)
const someSelected = computed(() => selectedCount.value > 0 && !allSelected.value)

watchEffect(
  () => {
    const input = checkbox.value?.el
    if (input) input.tabIndex = props.tabStop ? 0 : -1
  },
  { flush: 'post' },
)

table.dnd.target(
  rowEl,
  () => ({ kind: 'header', key: headerKey(props.view.key), groupKey: props.view.key }),
  () => !props.view.collapsed,
)

function toggleAll(): void {
  if (allSelected.value) selection.remove(props.view.topIds)
  else selection.add(props.view.topIds)
}

function toggle(): void {
  table.model.toggleGroup(props.view.key)
}

function addTop(): void {
  if (props.view.collapsed) table.model.toggleGroup(props.view.key, false)
  table.model.openAdd.value = `at:${props.view.key}`
}

function statusMenu(): MenuItem[] {
  const s = status.value
  if (!s) return []
  return [
    { id: 'rename', label: 'Rename status', icon: Pencil, onSelect: () => (renaming.value = true) },
    {
      kind: 'swatches',
      id: 'colour',
      label: 'Colour',
      value: s.color,
      onSelect: (hex: string) => void workspaces.updateStatus(s.id, { color: hex }).catch(noop),
    },
    { kind: 'separator', id: 'sep' },
    { id: 'edit', label: 'Edit statuses…', icon: ListChecks, onSelect: () => ui.openStatusEditor(table.listId.value) },
  ]
}

function saveName(name: string): void {
  const s = status.value
  if (s) void workspaces.updateStatus(s.id, { name }).catch(noop)
}
</script>

<template>
  <div
    ref="rowEl"
    role="row"
    class="group-header"
    :class="{ 'is-collapsed': view.collapsed, 'drop-bottom': drop !== null }"
    data-row
    :data-row-key="`h:${view.key}`"
  >
    <div role="gridcell" class="header-cell" :aria-colspan="table.colCount.value">
      <span class="gutter">
        <UiCheckbox
          v-if="table.selectable.value && count > 0"
          ref="checkbox"
          class="check reveal"
          :class="{ 'is-on': selectedCount > 0 }"
          :model-value="allSelected"
          :indeterminate="someSelected"
          :aria-label="`Select all in ${label}`"
          @change="toggleAll"
        />
      </span>
      <span v-if="renaming && status" class="rename">
        <UiIcon class="chev chev--open" :icon="ChevronRight" :size="14" />
        <span class="glyph"><StatusIcon :status="status" :size="16" /></span>
        <UiInlineEdit
          v-model:editing="renaming"
          :model-value="status.name"
          aria-label="Status name"
          activate="none"
          :maxlength="40"
          @save="saveName"
        />
      </span>
      <button
        v-else
        type="button"
        class="disclosure focus-inset"
        :class="{ 'is-overdue': g.kind === 'due' && g.bucket === 'overdue' }"
        data-nav
        :aria-expanded="view.collapsed ? 'false' : 'true'"
        :tabindex="tabStop ? 0 : -1"
        @click="toggle"
      >
        <UiIcon class="chev" :class="{ 'chev--open': !view.collapsed }" :icon="ChevronRight" :size="14" />
        <span v-if="status" class="glyph"><StatusIcon :status="status" :size="16" /></span>
        <span v-else-if="g.priority" class="glyph">
          <PriorityFlag :priority="g.priority" :size="16" show-empty decorative />
        </span>
        <span class="label truncate">{{ label }}</span>
      </button>
      <span class="count tabular" aria-hidden="true">{{ count }}</span>
      <span class="sr-only">{{ count === 1 ? '1 task' : `${count} tasks` }}</span>
      <span class="actions">
        <UiIconButton
          v-if="view.addDefaults"
          class="reveal"
          data-add-top
          :icon="Plus"
          :label="`Add task to ${label}`"
          size="xs"
          :tabindex="inner"
          @click="addTop"
        />
        <LazyMenu
          v-if="status && table.canEdit.value"
          v-model:open="menuOpen"
          lazy
          :items="statusMenu"
          :aria-label="`Actions for ${label}`"
          placement="bottom-end"
        >
          <template #trigger="{ props: trigger }">
            <UiIconButton
              v-bind="trigger"
              class="reveal reveal-touch"
              :icon="Ellipsis"
              :label="`Status actions for ${label}`"
              size="xs"
              :tabindex="inner"
            />
          </template>
        </LazyMenu>
      </span>
    </div>
  </div>
</template>

<style scoped>
.group-header {
  position: sticky;
  top: var(--colhead-h);
  z-index: var(--z-sticky);
  display: flex;
  height: 40px;
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-surface);
}

.header-cell {
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  padding-right: var(--space-2);
}

.gutter {
  display: flex;
  flex: none;
  justify-content: flex-end;
  width: var(--gutter-w);
  padding-right: var(--space-1-5);
}

/* The ancestor needs no scope attribute: only the last part of a scoped selector gets one. */
.check.is-on,
.has-selection .check {
  opacity: 1;
  visibility: visible;
}

/* The chevron, the icon and the label line up with the rows' chevron, status icon and title. */
.disclosure,
.rename {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  height: var(--control-sm);
  padding: 0 var(--space-1-5) 0 var(--space-0-5);
  margin-left: calc(-1 * var(--space-1-5));
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  text-align: left;
  cursor: pointer;
}

.rename {
  flex: 0 1 280px;
  cursor: default;
}

.disclosure:hover {
  background: var(--bg-hover);
}

.disclosure.is-overdue .label {
  color: var(--danger-text);
}

.chev {
  flex: none;
  width: 20px;
  color: var(--text-tertiary);
  transition: transform var(--dur-2) var(--ease-out);
}

.chev--open {
  transform: rotate(90deg);
}

.glyph {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: var(--control-xs);
}

.count {
  flex: none;
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.actions {
  display: inline-flex;
  flex: none;
  align-items: center;
  gap: var(--space-0-5);
  margin-left: auto;
  padding-right: var(--space-1);
}

/* The header is a drop target for the top of its group. */
.drop-bottom::after {
  content: '';
  position: absolute;
  z-index: 1;
  left: var(--gutter-w);
  right: 0;
  bottom: -4px;
  height: 6px;
  background:
    radial-gradient(circle at 3px 3px, var(--accent) 3px, transparent 3.5px),
    linear-gradient(var(--accent), var(--accent)) 0 2px / 100% 2px no-repeat;
  pointer-events: none;
}

/* Touch (7.5): a 44px header whose disclosure is a 44px target; the icon and label move with the
   rows' status icons and titles (--touch-shift, set by ListTable). */
@media (pointer: coarse) {
  .group-header {
    height: var(--touch-min);
  }

  .disclosure {
    position: relative;
  }

  .disclosure::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: var(--touch-min);
    transform: translateY(-50%);
  }

  .chev {
    margin-right: var(--touch-shift, 0px);
  }
}

/* Phones: no gutter (no selection); the chevron, icon and label still line up with the rows. */
@container page (width < 560px) {
  .group-header {
    top: 0;
  }

  .header-cell {
    padding-left: var(--space-3);
  }

  .gutter {
    display: none;
  }

  .disclosure,
  .rename {
    margin-left: calc(-1 * var(--space-0-5));
  }
}
</style>

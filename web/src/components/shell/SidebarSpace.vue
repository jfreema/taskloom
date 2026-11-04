<script setup lang="ts">
// One space of the sidebar tree (3.4, 8.3): a disclosure button named by the space (chevron,
// SpaceTile, name; aria-expanded), its "..." menu (Rename, Colour, New list, Delete space) and "+"
// ("New list in <space>"), then its lists. Collapsed spaces are remembered per user
// (sidebarState). F2 renames in place; Shift+F10 opens the menu. Guests get neither control.
import { computed, nextTick, ref, watch } from 'vue'
import { ChevronRight, Ellipsis, Plus } from '@lucide/vue'
import { shortcutsEnabled } from '../../composables/useShortcut'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Space, Uuid } from '../../types'
import SpaceTile from '../fields/SpaceTile.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import UiMenu from '../ui/UiMenu.vue'
import { useHierarchyMenus } from './hierarchyMenus'
import { useSidebarFocusRescue } from './sidebarFocus'
import SidebarListRow from './SidebarListRow.vue'
import { useSidebarState } from './sidebarState'

const props = defineProps<{ space: Space; activeListId: Uuid | null }>()

const workspaces = useWorkspaces()
const ui = useUi()
const menus = useHierarchyMenus()
const sidebar = useSidebarState()

const itemEl = ref<HTMLElement | null>(null)
const rowEl = ref<HTMLElement | null>(null)
const renaming = ref(false)
const menuOpen = ref(false)
useSidebarFocusRescue(itemEl)

// As in SidebarListRow: Enter or Esc in the field returns focus to the space row.
watch(renaming, (on) => {
  if (on || !rowEl.value?.contains(document.activeElement)) return
  void nextTick(() => {
    const a = document.activeElement
    if (a && a !== document.body && a.isConnected) return
    rowEl.value?.querySelector<HTMLElement>('[data-sidebar-item]')?.focus({ preventScroll: true })
  })
})

const expanded = computed(() => !sidebar.isCollapsed(props.space.id))
const lists = computed(() => [...props.space.lists].sort((a, b) => a.position - b.position))
const canEdit = computed(() => workspaces.canEdit(workspaces.findSpace(props.space.id)?.workspace.id))

function toggle(): void {
  sidebar.toggle(props.space.id)
}

function startRename(): void {
  if (canEdit.value) renaming.value = true
}

function menuItems() {
  return menus.spaceMenuItems(props.space.id, { onRename: startRename, renameKbd: 'F2' })
}

function onKeydown(e: KeyboardEvent): void {
  if (!canEdit.value) return
  if (e.key === 'F2' && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
    if (!shortcutsEnabled.value) return
    e.preventDefault()
    startRename()
  } else if ((e.key === 'F10' && e.shiftKey && !e.altKey && !e.ctrlKey && !e.metaKey) || e.key === 'ContextMenu') {
    e.preventDefault()
    menuOpen.value = true
  }
}

function save(name: string): void {
  // Optimistic; the store reverts the name and toasts when the server refuses.
  void workspaces.updateSpace(props.space.id, { name }).catch(() => undefined)
}
</script>

<template>
  <li ref="itemEl" class="space">
    <div ref="rowEl" class="row" :class="{ 'is-editing': renaming }" data-row>
      <button
        v-show="!renaming"
        v-tooltip="{ text: space.name, overflowOnly: true, placement: 'right' }"
        type="button"
        class="row-main"
        data-sidebar-item="space"
        :data-space-id="space.id"
        :aria-expanded="expanded ? 'true' : 'false'"
        @click="toggle"
        @keydown="onKeydown"
      >
        <UiIcon :icon="ChevronRight" :size="16" class="chevron" :class="{ 'is-open': expanded }" />
        <SpaceTile :space="space" :size="18" />
        <span class="name space-name truncate">{{ space.name }}</span>
      </button>
      <div v-if="renaming" class="row-main is-editing">
        <UiIcon :icon="ChevronRight" :size="16" class="chevron" :class="{ 'is-open': expanded }" />
        <SpaceTile :space="space" :size="18" />
        <UiInlineEdit
          v-model:editing="renaming"
          class="edit space-name"
          :model-value="space.name"
          aria-label="Space name"
          activate="none"
          :maxlength="200"
          @save="save"
        />
      </div>
      <div v-if="canEdit && !renaming" class="row-actions">
        <UiMenu v-model:open="menuOpen" :items="menuItems" :aria-label="`Actions for ${space.name}`">
          <template #trigger="{ props: trigger }">
            <UiIconButton
              v-bind="trigger"
              class="reveal reveal-touch"
              :icon="Ellipsis"
              :label="`More actions for ${space.name}`"
              size="xs"
              tabindex="-1"
            />
          </template>
        </UiMenu>
        <UiIconButton
          class="reveal"
          :icon="Plus"
          :label="`New list in ${space.name}`"
          size="xs"
          tabindex="-1"
          @click="ui.openNewList(space.id)"
        />
      </div>
    </div>
    <ul v-if="expanded" class="lists">
      <SidebarListRow v-for="l in lists" :key="l.id" :list="l" :active="l.id === activeListId" />
      <li v-if="!lists.length" class="no-lists">
        <span>No lists yet</span>
        <UiButton
          v-if="canEdit"
          variant="link"
          size="sm"
          data-sidebar-item="empty"
          :data-space-id="space.id"
          @click="ui.openNewList(space.id)"
        >
          New list
        </UiButton>
      </li>
    </ul>
  </li>
</template>

<style scoped src="./sidebarRow.css"></style>

<style scoped>
.space + .space {
  margin-top: var(--space-0-5);
}

.space-name {
  color: var(--text-primary);
  font-weight: var(--fw-medium);
}

/* Chevron at 6px, then the tile at 28px: the list rows' icons line up under it. */
.row-main {
  gap: var(--space-1-5);
  padding-left: var(--space-1-5);
}

.chevron {
  color: var(--text-tertiary);
}

@media (prefers-reduced-motion: no-preference) {
  .chevron {
    transition: transform var(--dur-2) var(--ease-out);
  }
}

.chevron.is-open {
  transform: rotate(90deg);
}

.lists {
  margin: var(--space-0-5) 0 0;
  padding: 0;
  list-style: none;
}

.lists > li + li {
  margin-top: var(--space-0-5);
}

.no-lists {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-sidebar);
  padding-left: 28px;
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}
</style>

<script setup lang="ts">
// One list row of the sidebar tree (3.4, 8.3): a RouterLink to /l/:id (aria-current="page" while
// the list is open), indented 28px, and its "..." menu (Rename, Edit statuses, Move to space…, Copy
// link, Delete list; guests get only Copy link). F2 renames in place; Shift+F10 opens the menu.
import { computed, nextTick, ref, watch } from 'vue'
import { Ellipsis, List as ListIcon } from '@lucide/vue'
import { shortcutsEnabled } from '../../composables/useShortcut'
import { useWorkspaces } from '../../stores/workspaces'
import type { ListSummary } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import UiMenu from '../ui/UiMenu.vue'
import { useHierarchyMenus } from './hierarchyMenus'
import { useSidebarFocusRescue } from './sidebarFocus'

const props = defineProps<{ list: ListSummary; active: boolean }>()

const workspaces = useWorkspaces()
const menus = useHierarchyMenus()

const itemEl = ref<HTMLElement | null>(null)
const rowEl = ref<HTMLElement | null>(null)
const renaming = ref(false)
const menuOpen = ref(false)
useSidebarFocusRescue(itemEl)
const canEdit = computed(() => workspaces.canEdit(workspaces.findList(props.list.id)?.workspace.id))

// Enter or Esc in the field returns focus to the row (9.6). UiInlineEdit returns it to what had
// focus when the edit began; after the menu's Rename that was the "..." button, which is gone.
watch(renaming, (on) => {
  if (on || !rowEl.value?.contains(document.activeElement)) return
  void nextTick(() => {
    const a = document.activeElement
    if (a && a !== document.body && a.isConnected) return
    rowEl.value?.querySelector<HTMLElement>('[data-sidebar-item]')?.focus({ preventScroll: true })
  })
})

function startRename(): void {
  if (canEdit.value) renaming.value = true
}

function menuItems() {
  return menus.listMenuItems(props.list.id, { onRename: startRename, renameKbd: 'F2' })
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'F2' && !e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
    if (!shortcutsEnabled.value || !canEdit.value) return
    e.preventDefault()
    startRename()
  } else if ((e.key === 'F10' && e.shiftKey && !e.altKey && !e.ctrlKey && !e.metaKey) || e.key === 'ContextMenu') {
    e.preventDefault()
    menuOpen.value = true
  }
}

function save(name: string): void {
  // Optimistic; the store reverts the name and toasts when the server refuses.
  void workspaces.updateList(props.list.id, { name }).catch(() => undefined)
}
</script>

<template>
  <li ref="itemEl" class="list-item">
    <div ref="rowEl" class="row" :class="{ 'is-active': active, 'is-editing': renaming }" data-row>
      <RouterLink
        v-show="!renaming"
        v-tooltip="{ text: list.name, overflowOnly: true, placement: 'right' }"
        :to="`/l/${list.id}`"
        class="row-main"
        data-sidebar-item="list"
        :data-list-id="list.id"
        :data-space-id="list.spaceId"
        :aria-current="active ? 'page' : undefined"
        @keydown="onKeydown"
      >
        <UiIcon :icon="ListIcon" :size="16" class="list-icon" />
        <span class="name truncate">{{ list.name }}</span>
      </RouterLink>
      <div v-if="renaming" class="row-main is-editing">
        <UiIcon :icon="ListIcon" :size="16" class="list-icon" />
        <UiInlineEdit
          v-model:editing="renaming"
          class="edit"
          :model-value="list.name"
          aria-label="List name"
          activate="none"
          :maxlength="200"
          @save="save"
        />
      </div>
      <div v-if="!renaming" class="row-actions">
        <UiMenu v-model:open="menuOpen" :items="menuItems" :aria-label="`Actions for ${list.name}`">
          <template #trigger="{ props: trigger }">
            <UiIconButton
              v-bind="trigger"
              class="reveal reveal-touch"
              :icon="Ellipsis"
              :label="`More actions for ${list.name}`"
              size="xs"
              tabindex="-1"
            />
          </template>
        </UiMenu>
      </div>
    </div>
  </li>
</template>

<style scoped src="./sidebarRow.css"></style>

<style scoped>
.row-main {
  padding-left: 28px;
}

.list-icon {
  color: var(--text-tertiary);
}

.is-active .list-icon {
  color: var(--accent-text);
}
</style>

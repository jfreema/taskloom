<script setup lang="ts">
// The sidebar (3.4, 8.3): <nav aria-label="Sidebar"> with the workspace menu and "Hide sidebar",
// the search field (opens the palette) and the "New task" pen, Home, the Spaces tree and the
// account menu. AppShell docks it at 1024px and wider and puts it in a left drawer below that
// (`inDrawer`: no "Hide sidebar"; the drawer closes on navigation, Esc or the scrim).
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { House, PanelLeft, Search, SquarePen } from '@lucide/vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { isApple } from '../../lib/platform'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiKbd from '../ui/UiKbd.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import SidebarTree from './SidebarTree.vue'
import UserMenu from './UserMenu.vue'
import WorkspaceMenu from './WorkspaceMenu.vue'
import { skeletonDelay } from './skeletonHandover'

withDefaults(defineProps<{ inDrawer?: boolean }>(), { inDrawer: false })

const ui = useUi()
const workspaces = useWorkspaces()
const route = useRoute()

const hasWorkspace = computed(() => !!workspaces.current)
// The workspace trigger's placeholder keeps the row's height; its skeleton shows only while the
// tree loads (after 200ms), never in the error state (the tree shows "Couldn't load spaces").
const loadingTree = computed(() => !workspaces.loaded && !workspaces.error)
const showWorkspaceSkeleton = useDelayedFlag(loadingTree, skeletonDelay())
const canEdit = computed(() => workspaces.canEdit())
const onHome = computed(() => route.name === 'home')
const keyShortcut = isApple ? 'Meta+K' : 'Control+K'

function newTask(): void {
  ui.openQuickCreate(ui.createDefaults ?? undefined)
}
</script>

<template>
  <nav class="app-sidebar" :class="{ 'in-drawer': inDrawer }" aria-label="Sidebar">
    <div class="head">
      <div class="workspace">
        <WorkspaceMenu v-if="workspaces.loaded" />
        <div v-else class="workspace-placeholder" aria-hidden="true">
          <template v-if="showWorkspaceSkeleton">
            <UiSkeleton variant="rect" width="24px" height="24px" />
            <UiSkeleton width="60%" />
          </template>
        </div>
      </div>
      <UiIconButton
        v-if="!inDrawer"
        class="hide-sidebar"
        data-sidebar-toggle
        :icon="PanelLeft"
        label="Hide sidebar"
        kbd="["
        size="sm"
        @click="ui.toggleSidebar()"
      />
    </div>

    <div v-if="hasWorkspace" class="tools">
      <button type="button" class="search focus-inset" aria-label="Search" :aria-keyshortcuts="keyShortcut" @click="ui.openPalette()">
        <UiIcon :icon="Search" :size="16" class="search-icon" />
        <span class="search-text">Search…</span>
        <span class="search-kbd" aria-hidden="true"><UiKbd keys="Mod+K" /></span>
      </button>
      <UiIconButton v-if="canEdit" :icon="SquarePen" label="New task" kbd="N" size="md" variant="secondary" @click="newTask" />
    </div>

    <div class="body">
      <RouterLink to="/" class="nav-item focus-inset" :class="{ 'is-active': onHome }" :aria-current="onHome ? 'page' : undefined">
        <UiIcon :icon="House" :size="16" class="nav-icon" />
        <span class="truncate">Home</span>
      </RouterLink>
      <SidebarTree v-if="hasWorkspace || !workspaces.loaded" />
    </div>

    <div class="foot">
      <UserMenu />
    </div>
  </nav>
</template>

<style scoped>
.app-sidebar {
  display: flex;
  flex-direction: column;
  width: var(--sidebar-w);
  height: 100%;
  min-height: 0;
  background: var(--bg-canvas);
  color: var(--text-primary);
}

.app-sidebar.in-drawer {
  width: 100%;
  background: var(--bg-raised);
}

.head {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  padding: var(--space-2) var(--space-2) 0;
}

.workspace {
  flex: 1;
  min-width: 0;
}

.workspace-placeholder {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--control-lg);
  padding: 0 var(--space-2);
}

.hide-sidebar {
  flex: none;
  color: var(--text-tertiary);
  opacity: 0;
  transition: opacity var(--dur-1) var(--ease-out);
}

.app-sidebar:hover .hide-sidebar,
.app-sidebar:focus-within .hide-sidebar {
  opacity: 1;
}

@media (hover: none) {
  .hide-sidebar {
    opacity: 1;
  }
}

.tools {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  padding: var(--space-2) var(--space-2) 0;
}

/* A button styled as an input (3.4). */
.search {
  position: relative;
  display: flex;
  flex: 1;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  height: var(--control-md);
  padding: 0 var(--space-1-5) 0 var(--space-2);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-sm);
  background: var(--bg-surface);
  color: var(--text-tertiary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-align: left;
  cursor: text;
  transition: border-color var(--dur-1) var(--ease-out);
}

.search:hover {
  border-color: var(--border-control);
}

.search-icon {
  flex: none;
}

.search-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.search-kbd {
  display: flex;
  flex: none;
}

.body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: var(--space-3) var(--space-2) var(--space-4);
  scroll-padding: var(--space-2);
}

.nav-item {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-sidebar);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-decoration: none;
  transition: background-color var(--dur-1) var(--ease-out);
}

.nav-item:hover {
  background: var(--bg-hover);
}

.nav-item.is-active {
  background: var(--bg-selected);
  color: var(--accent-text);
  font-weight: var(--fw-medium);
}

.nav-icon {
  color: var(--text-tertiary);
}

.nav-item.is-active .nav-icon {
  color: var(--accent-text);
}

.foot {
  flex: none;
  padding: var(--space-1) var(--space-2) var(--space-2);
  padding-bottom: max(var(--space-2), env(safe-area-inset-bottom, 0px));
  border-top: 1px solid var(--border-subtle);
}

@media (max-width: 639px) {
  .search-kbd {
    display: none;
  }
}

/* Touch (7.5): the 32px search field keeps its look beside the pen button and grows its hit area. */
@media (pointer: coarse) {
  .search::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: var(--touch-min);
    transform: translateY(-50%);
  }
}
</style>

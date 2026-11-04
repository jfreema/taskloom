<script setup lang="ts">
// The signed-in frame (3.3, 3.6, 8.2).
// - Layout: a grid of the sidebar (248px, docked at 1024px and wider; animates to 0 when collapsed)
//   and the inset content panel (8px margin, radius 10, --bg-surface, --shadow-1; full-bleed below
//   640px). Inside the panel, a second grid of the page area and the docked task panel. The page
//   area is the `page` size container (7.5); <main id="main"> inside it scrolls.
// - Below 1024px the sidebar is a modal left drawer that closes on navigation, Esc or the scrim.
// - Start-up (8.2): the router guard has loaded the session; this loads the workspace tree, then
//   members and tags of the current workspace, registers the global shortcuts, installs the
//   pagehide flush of pending deletes and the focus refresh of the tree.
// - States: a page skeleton while the tree loads (after 200ms), an error with retry, and "You're
//   not in a workspace yet." when the user has no workspace (profile and appearance still work).
//   The error and no-workspace states render a bare PageHeader, so the sidebar's button ("Open
//   navigation" below 1024px, "Show sidebar" when collapsed) still reaches the user menu.
// - The skip link is the first focusable element; the global dialogs and the FAB mount here.
import { computed, nextTick, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Building2, Plus } from '@lucide/vue'
import { useBreakpoint } from '../composables/useBreakpoint'
import { useDelayedDelete } from '../composables/useDelayedDelete'
import { useDelayedFlag } from '../composables/useDelayedFlag'
import { useMembers } from '../stores/members'
import { useTags } from '../stores/tags'
import { useUi } from '../stores/ui'
import { useWorkspaces } from '../stores/workspaces'
import AppSidebar from '../components/shell/AppSidebar.vue'
import NewTaskFab from '../components/shell/NewTaskFab.vue'
import PageHeader from '../components/shell/PageHeader.vue'
import TaskPanelHost from '../components/shell/TaskPanelHost.vue'
import { PANEL_ROUTE_NAMES, WORKSPACE_FREE_ROUTES, contentPanelKey } from '../components/shell/shellContext'
import { useShellShortcuts } from '../components/shell/useShellShortcuts'
import UiButton from '../components/ui/UiButton.vue'
import UiDrawer from '../components/ui/UiDrawer.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'
import CommandPalette from '../components/palette/CommandPalette.vue'
import QuickCreateDialog from '../components/quick-create/QuickCreateDialog.vue'
import ShortcutsDialog from '../components/shortcuts/ShortcutsDialog.vue'
import HierarchyDialogs from '../components/hierarchy/HierarchyDialogs.vue'
import MoveTaskDialog from '../components/fields/MoveTaskDialog.vue'

/** The focus refresh refetches data older than this (8.1). */
const STALE_MS = 30_000

const ui = useUi()
const workspaces = useWorkspaces()
const members = useMembers()
const tags = useTags()
const route = useRoute()
const { isDesktop, isPhone } = useBreakpoint()

const contentPanel = ref<HTMLElement | null>(null)
provide(contentPanelKey, contentPanel)

// ---- Start-up ----

void workspaces.load()
watch(
  () => workspaces.currentId,
  (id) => {
    if (!id) return
    void members.ensure(id)
    void tags.ensure(id)
  },
  { immediate: true },
)
useShellShortcuts()
// Installs the pagehide beacon for pending deletes (9.2).
useDelayedDelete()

function refreshIfStale(): void {
  if (document.visibilityState === 'hidden' || !workspaces.loaded) return
  void workspaces.refresh({ ifOlderThan: STALE_MS })
}

function onVisibility(): void {
  if (document.visibilityState === 'visible') refreshIfStale()
}

onMounted(() => {
  window.addEventListener('focus', refreshIfStale)
  document.addEventListener('visibilitychange', onVisibility)
})
onBeforeUnmount(() => {
  window.removeEventListener('focus', refreshIfStale)
  document.removeEventListener('visibilitychange', onVisibility)
})

// ---- States ----

const loading = computed(() => !workspaces.loaded && !workspaces.error)
const showSkeleton = useDelayedFlag(loading)
const failed = computed(() => !workspaces.loaded && !!workspaces.error)
const needsWorkspace = computed(() => typeof route.name !== 'string' || !WORKSPACE_FREE_ROUTES.has(route.name))
const noWorkspace = computed(() => workspaces.loaded && !workspaces.current && needsWorkspace.value)
const showPage = computed(() => (workspaces.loaded || !needsWorkspace.value) && !noWorkspace.value)

// ---- Sidebar ----

const collapsed = computed(() => isDesktop.value && ui.sidebarCollapsed)
const dock = ref<HTMLElement | null>(null)

// Focus follows the toggle. Hiding turns the sidebar inert and showing removes the header's
// button, so focus moves to the counterpart: "Show sidebar" in the page header (else #main), or
// "Hide sidebar" in the sidebar.
watch(collapsed, (now) => {
  const active = document.activeElement
  if (!(active instanceof HTMLElement)) return
  const fromSidebar = now && !!dock.value?.contains(active)
  const fromHeader = !now && active.matches('#main [data-sidebar-toggle]')
  if (!fromSidebar && !fromHeader) return
  void nextTick(() => {
    const target = now
      ? (document.querySelector<HTMLElement>('#main [data-sidebar-toggle]') ?? document.getElementById('main'))
      : dock.value?.querySelector<HTMLElement>('[data-sidebar-toggle]')
    target?.focus({ preventScroll: true })
  })
})

// Crossing to desktop closes the drawer. Its opener ("Open navigation") may be gone by then.
watch(isDesktop, (desktop) => {
  if (!desktop || !ui.sidebarDrawerOpen) return
  ui.sidebarDrawerOpen = false
  void nextTick(() => {
    const a = document.activeElement
    if (!a || a === document.body || !a.isConnected) document.getElementById('main')?.focus({ preventScroll: true })
  })
})

// The drawer closes on navigation; focus then goes to the new page.
watch(
  () => route.path,
  () => {
    if (!ui.sidebarDrawerOpen) return
    ui.sidebarDrawerOpen = false
    void nextTick(() => document.getElementById('main')?.focus({ preventScroll: true }))
  },
)

/**
 * Pages that can show the phone "New task" button keep room for it below their content: the
 * shell sets --fab-room, and adds that much under <main> unless the page scrolls an inner
 * container of its own (route meta `ownScroll`), which then reserves var(--fab-room) itself.
 */
const fabRoom = computed(
  () => isPhone.value && typeof route.name === 'string' && PANEL_ROUTE_NAMES.has(route.name) && workspaces.canEdit(),
)
const mainScrolls = computed(() => !route.meta.ownScroll)

function skipToContent(): void {
  document.getElementById('main')?.focus()
}
</script>

<template>
  <a class="skip-link" href="#main" @click.prevent="skipToContent">Skip to content</a>
  <div
    class="app-shell"
    :class="{ 'has-sidebar': isDesktop, 'is-collapsed': collapsed, 'has-fab': fabRoom, 'main-scrolls': mainScrolls }"
  >
    <div v-if="isDesktop" ref="dock" class="sidebar-dock" :inert="collapsed">
      <AppSidebar />
    </div>
    <div ref="contentPanel" class="content-panel">
      <div class="page-area">
        <main id="main" class="page" tabindex="-1" :aria-busy="showSkeleton ? 'true' : undefined">
          <RouterView v-if="showPage" />
          <!-- The bare header keeps the sidebar reachable (Open navigation, Show sidebar). -->
          <template v-else-if="failed">
            <PageHeader bare />
            <UiErrorState class="state" title="Couldn't load your workspace" :retry="() => workspaces.refresh()" />
          </template>
          <template v-else-if="noWorkspace">
            <PageHeader bare />
            <div class="state">
              <UiEmptyState :icon="Building2" title="You're not in a workspace yet." description="Create one to start adding spaces, lists and tasks.">
                <template #actions>
                  <UiButton variant="primary" :icon="Plus" @click="ui.openNewWorkspace()">Create workspace</UiButton>
                </template>
              </UiEmptyState>
            </div>
          </template>
          <div v-else-if="showSkeleton" class="page-skeleton" aria-hidden="true">
            <div class="skeleton-header">
              <UiSkeleton variant="rect" width="18px" height="18px" />
              <UiSkeleton width="180px" />
            </div>
            <div class="skeleton-rows">
              <div v-for="(w, i) in ['46%', '62%', '38%', '55%', '70%', '42%', '58%', '34%']" :key="i" class="skeleton-row">
                <UiSkeleton variant="circle" width="16px" height="16px" />
                <UiSkeleton :width="w" />
              </div>
            </div>
          </div>
        </main>
      </div>
      <TaskPanelHost />
    </div>
  </div>
  <UiDrawer v-if="!isDesktop" v-model:open="ui.sidebarDrawerOpen" side="left" aria-label="Navigation">
    <AppSidebar in-drawer />
  </UiDrawer>
  <NewTaskFab />
  <CommandPalette />
  <QuickCreateDialog />
  <ShortcutsDialog />
  <HierarchyDialogs />
  <MoveTaskDialog />
</template>

<style scoped>
.skip-link {
  position: fixed;
  top: var(--space-2);
  left: var(--space-2);
  z-index: var(--z-tooltip);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--bg-raised);
  color: var(--accent-text);
  font-weight: var(--fw-medium);
  transform: translateY(calc(-100% - var(--space-4)));
}

/* The shadow only while shown: off-screen, its blur would peek in at the top of the window. */
.skip-link:focus {
  transform: none;
  box-shadow: var(--shadow-3);
}

.app-shell {
  /* The phone FAB's room (56px, 16px below it, 8px above, the safe area); 0 without a FAB. */
  --fab-room: 0px;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  height: 100dvh;
  background: var(--bg-canvas);
}

.app-shell.has-sidebar {
  grid-template-columns: var(--sidebar-w) minmax(0, 1fr);
}

.app-shell.has-sidebar.is-collapsed {
  grid-template-columns: 0px minmax(0, 1fr);
}

@media (prefers-reduced-motion: no-preference) {
  .app-shell.has-sidebar {
    transition: grid-template-columns var(--dur-4) var(--ease-in-out);
  }
}

/* The sidebar keeps its width while its column shrinks, so nothing reflows during the animation. */
.sidebar-dock {
  position: relative;
  z-index: var(--z-sidebar);
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

.is-collapsed .sidebar-dock {
  visibility: hidden;
  transition: visibility 0s linear var(--dur-4);
}

.content-panel {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  min-width: 0;
  min-height: 0;
  margin: var(--inset);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  box-shadow: var(--shadow-1);
  overflow: hidden;
}


/* The `page` container (7.5). It doesn't scroll itself, so fixed-position page UI stays put. */
.page-area {
  container: page / inline-size;
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.page {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow-y: auto;
  scroll-padding-top: calc(var(--topbar-h) + var(--space-2));
}

/* #main takes focus after navigation (9.6); it's a landmark, not a control, so no ring. */
.page:focus {
  outline: none;
}

.state {
  margin: auto;
  padding: var(--space-8) var(--space-4);
}

.page-skeleton {
  display: flex;
  flex-direction: column;
}

.skeleton-header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--topbar-h);
  padding: 0 var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}

.skeleton-rows {
  display: flex;
  flex-direction: column;
  padding: var(--space-3) var(--space-4);
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: var(--row-list);
  padding: 0 var(--space-2);
  border-bottom: 1px solid var(--border-subtle);
}

@media (max-width: 639px) {
  .content-panel {
    margin: 0;
    border: 0;
    border-radius: 0;
    box-shadow: none;
  }

  .page {
    scroll-padding-top: calc(var(--appbar-h) + var(--space-2));
  }

  /* NewTaskFab: 56px plus 16px and the safe area from the bottom. The last row can scroll clear of
     it, and keyboard focus never lands behind it (2.4.11). A page that scrolls an inner container
     (ownScroll) puts the same room in that container, so <main> gets no dead band below it. */
  .app-shell.has-fab {
    --fab-room: calc(56px + var(--space-6) + env(safe-area-inset-bottom, 0px));
  }

  .has-fab.main-scrolls .page {
    scroll-padding-bottom: var(--fab-room);
  }

  .has-fab.main-scrolls .page::after {
    content: '';
    flex: none;
    height: var(--fab-room);
  }
}
</style>

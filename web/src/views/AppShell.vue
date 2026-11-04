<script setup lang="ts">
// The signed-in frame (spec 8.2, 3.6). This first version holds the skip link (the first focusable
// element), the inset content panel with <main id="main" tabindex="-1"> and the task panel beside
// it, and the global affordances. The sidebar, the FAB, the start-up states and the global
// shortcuts come with the full shell.
import { onMounted } from 'vue'
import { useWorkspaces } from '../stores/workspaces'
import TaskPanelHost from '../components/shell/TaskPanelHost.vue'
import CommandPalette from '../components/palette/CommandPalette.vue'
import QuickCreateDialog from '../components/quick-create/QuickCreateDialog.vue'
import ShortcutsDialog from '../components/shortcuts/ShortcutsDialog.vue'
import HierarchyDialogs from '../components/hierarchy/HierarchyDialogs.vue'
import MoveTaskDialog from '../components/fields/MoveTaskDialog.vue'

const workspaces = useWorkspaces()

onMounted(() => {
  if (!workspaces.loaded) void workspaces.load()
})

function skipToContent(): void {
  document.getElementById('main')?.focus()
}
</script>

<template>
  <a class="skip-link" href="#main" @click.prevent="skipToContent">Skip to content</a>
  <div class="app-shell">
    <div class="content-panel">
      <main id="main" class="page" tabindex="-1">
        <RouterView />
      </main>
      <TaskPanelHost />
    </div>
  </div>
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
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  height: 100dvh;
  background: var(--bg-canvas);
}

.content-panel {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  min-height: 0;
  margin: var(--inset);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  box-shadow: var(--shadow-1);
  overflow: hidden;
}

.page {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow-y: auto;
}

/* #main takes focus after navigation (9.6); it's a landmark, not a control, so no ring. */
.page:focus {
  outline: none;
}

@media (max-width: 639px) {
  .content-panel {
    margin: 0;
    border: 0;
    border-radius: 0;
    box-shadow: none;
  }
}
</style>

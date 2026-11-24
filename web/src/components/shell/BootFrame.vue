<script setup lang="ts">
// The frame App.vue shows while the first navigation waits for the session (8.2), after 200ms: the
// shell's own loading layout, static. The sidebar column (docked at 1024px and wider, unless the
// sidebar is collapsed) holds the workspace placeholder, six tree rows and the account row; the
// content panel holds the page skeleton. It reads nothing from the server. In the Pelican style
// (Look 3) it has the black top bar and navy sidebar too (the .app-sidebar region of pelican.css).
import { computed, onBeforeUnmount } from 'vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useLook } from '../../composables/useLook'
import { useUi } from '../../stores/ui'
import UiSkeleton from '../ui/UiSkeleton.vue'
import PageSkeleton from './PageSkeleton.vue'
import { skeletonHandover } from './skeletonHandover'

const ui = useUi()
const { isDesktop } = useBreakpoint()
const { look } = useLook()

const sidebar = computed(() => isDesktop.value && !ui.sidebarCollapsed)
const TREE = ['62%', '78%', '54%', '70%', '46%', '66%']

onBeforeUnmount(skeletonHandover)
</script>

<template>
  <div class="boot-frame" :class="{ 'has-sidebar': sidebar }" aria-busy="true">
    <div v-if="look === 'pelican'" class="brand-bar" aria-hidden="true">Taskloom</div>
    <div v-if="sidebar" class="sidebar app-sidebar" aria-hidden="true">
      <div class="workspace">
        <UiSkeleton variant="rect" width="24px" height="24px" />
        <UiSkeleton width="60%" />
      </div>
      <div class="tree">
        <div v-for="(w, i) in TREE" :key="i" class="tree-row" :class="{ indent: i % 3 !== 0 }">
          <UiSkeleton variant="rect" width="16px" height="16px" />
          <UiSkeleton :width="w" />
        </div>
      </div>
      <div class="foot">
        <UiSkeleton variant="circle" width="24px" height="24px" />
        <UiSkeleton width="45%" />
      </div>
    </div>
    <div class="panel">
      <PageSkeleton />
    </div>
  </div>
</template>

<style scoped>
/* The same grid, column, top bar and inset panel as AppShell. */
.boot-frame {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  grid-template-rows: auto minmax(0, 1fr);
  height: 100dvh;
  background: var(--bg-canvas);
}

.sidebar,
.panel {
  grid-row: 2;
}

.brand-bar {
  grid-row: 1;
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  height: var(--brandbar-h);
  padding: 0 var(--space-5);
  border-bottom: var(--brandbar-rule-w) solid var(--brandbar-rule);
  background: var(--brandbar-bg);
  box-shadow: var(--brandbar-shadow, none);
  color: var(--brandbar-fg);
  font-size: 15px;
  line-height: 20px;
  font-weight: var(--fw-bold);
  letter-spacing: 0.01em;
}

.boot-frame.has-sidebar {
  grid-template-columns: var(--sidebar-w) minmax(0, 1fr);
}

.sidebar {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  background: var(--bg-canvas);
}

.workspace {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--control-lg);
  margin: var(--space-2) var(--space-2) 0;
  padding: 0 var(--space-2);
}

.tree {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding: var(--space-3) var(--space-2) var(--space-4);
}

.tree-row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--row-sidebar);
  padding: 0 var(--space-2);
}

.tree-row.indent {
  padding-left: 28px;
}

.foot {
  display: flex;
  flex: none;
  align-items: center;
  gap: var(--space-2);
  height: calc(var(--control-lg) + var(--space-1) + var(--space-2));
  padding: var(--space-1) var(--space-4) var(--space-2);
  border-top: 1px solid var(--border-subtle);
}

.panel {
  min-width: 0;
  min-height: 0;
  margin: var(--inset);
  border: 1px solid var(--panel-edge);
  border-radius: var(--panel-radius);
  background: var(--bg-page);
  box-shadow: var(--shadow-1);
  overflow: hidden;
}

@media (max-width: 639px) {
  .panel {
    margin: 0;
    border: 0;
    border-radius: 0;
    box-shadow: none;
  }
}
</style>

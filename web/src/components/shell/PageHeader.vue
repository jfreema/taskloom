<script setup lang="ts">
// The page header (spec 3.5, 8.4, 7.12): 48px, 16px side padding, a hairline bottom border. The
// sidebar button appears by itself: "Show sidebar" when the docked sidebar is collapsed, "Open
// navigation" below 1024px. Phones get the 52px app bar: menu, the truncated `title`, and Search,
// with the `below` slot underneath. `bare` renders only the sidebar button, with no border (Home).
// Slots receive `compact`, true while the header is narrower than 720px.
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { Menu, PanelLeft, Search } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useUi } from '../../stores/ui'
import UiIconButton from '../ui/UiIconButton.vue'
import UiLoadingBar from '../ui/UiLoadingBar.vue'

const props = withDefaults(defineProps<{ title?: string; loading?: boolean; bare?: boolean }>(), {
  title: undefined,
  loading: false,
  bare: false,
})

const COMPACT_BELOW = 720

const ui = useUi()
const { isDesktop, isPhone } = useBreakpoint()
const root = ref<HTMLElement | null>(null)
const width = ref(Number.POSITIVE_INFINITY)
const compact = computed(() => width.value < COMPACT_BELOW)
const showSidebarButton = computed(() => !isDesktop.value || ui.sidebarCollapsed)
const appBar = computed(() => isPhone.value && !props.bare)

let observer: ResizeObserver | null = null
onMounted(() => {
  if (!root.value || typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(([entry]) => {
    if (entry) width.value = entry.contentRect.width
  })
  observer.observe(root.value)
})
onBeforeUnmount(() => observer?.disconnect())

function onSidebarButton(): void {
  if (isDesktop.value) ui.toggleSidebar()
  else ui.sidebarDrawerOpen = true
}
</script>

<template>
  <div v-if="!bare || showSidebarButton" class="page-header-wrap" :class="{ 'is-bare': bare }">
    <header ref="root" class="page-header" :class="{ 'is-bare': bare, 'is-app-bar': appBar }">
      <UiIconButton
        v-if="showSidebarButton"
        class="sidebar-button"
        :icon="isDesktop ? PanelLeft : Menu"
        :label="isDesktop ? 'Show sidebar' : 'Open navigation'"
        :kbd="isDesktop ? '[' : undefined"
        :size="isPhone ? 'lg' : 'md'"
        @click="onSidebarButton"
      />
      <template v-if="!bare">
        <template v-if="appBar">
          <p class="app-bar-title truncate">{{ title }}</p>
          <div class="actions">
            <UiIconButton :icon="Search" label="Search" size="lg" @click="ui.openPalette()" />
          </div>
        </template>
        <template v-else>
          <div v-if="$slots.leading" class="leading"><slot name="leading" :compact="compact" /></div>
          <div class="title-area"><slot :compact="compact" /></div>
          <div v-if="$slots.actions" class="actions"><slot name="actions" :compact="compact" /></div>
        </template>
      </template>
      <UiLoadingBar :active="loading" />
    </header>
    <div v-if="appBar && $slots.below" class="below"><slot name="below" /></div>
  </div>
</template>

<style scoped>
.page-header-wrap {
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-subtle);
}

.page-header-wrap.is-bare {
  border-bottom: 0;
}

.page-header {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--topbar-h);
  padding: 0 var(--space-4);
  min-width: 0;
}

.page-header.is-bare {
  height: auto;
  padding: var(--space-2) var(--space-4) 0;
}

.page-header.is-app-bar {
  height: var(--appbar-h);
  padding: 0 var(--space-1);
}

.sidebar-button {
  flex-shrink: 0;
  margin-left: calc(var(--space-1) * -1);
}

.is-app-bar .sidebar-button {
  margin-left: 0;
}

.leading {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
}

.title-area {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: 1;
  min-width: 0;
}

.app-bar-title {
  flex: 1;
  min-width: 0;
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
  margin-left: auto;
}

.below {
  padding: 0 var(--space-4) var(--space-2);
}
</style>

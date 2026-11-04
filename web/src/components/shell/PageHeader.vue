<script setup lang="ts">
// The page header (3.5, 8.4, 7.12): 48px, 16px side padding, a hairline bottom border, sticky at
// the top of the page while it scrolls. The sidebar button appears by itself: "Show sidebar"
// (PanelLeft) when the docked sidebar is collapsed, "Open navigation" (Menu) below 1024px.
// - Slots: `leading`, `default` (the title area), `actions`, and `below` (phones only: a row under
//   the app bar, e.g. the view tabs as a segmented control). Each slot gets `{ compact }`.
// - `bare` renders only the sidebar button, with no border (Home), and nothing at all while the
//   docked sidebar is open.
// - `loading` runs the UiLoadingBar along the bottom edge (it waits 150ms before it shows).
// - Phones (< 640px): the 52px app bar with Menu, `title` (truncated; the page's h1 unless
//   `titleTag` is 'p') and Search; the leading, default and actions slots are not rendered.
// - Compact (the header is narrower than 720px): slots get `compact: true` so they can go
//   icon-only. A primary button with an icon in `actions` turns into an icon button by itself: its
//   label stays as the accessible name and becomes its tooltip, with the label's global shortcut
//   from SHORTCUTS (9.5: the one source of kbd hints), e.g. "New task" {N}.
import { computed, nextTick, onBeforeUnmount, onMounted, onUpdated, ref, watch } from 'vue'
import { Menu, PanelLeft, Search } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { SHORTCUTS } from '../../lib/shortcuts'
import { useUi } from '../../stores/ui'
import UiIconButton from '../ui/UiIconButton.vue'
import UiLoadingBar from '../ui/UiLoadingBar.vue'
import type { TooltipValue } from '../ui/tooltip'

const props = withDefaults(
  defineProps<{
    title?: string
    loading?: boolean
    bare?: boolean
    /** The app bar title's element on phones. 'p' for pages whose h1 is in the page body. */
    titleTag?: 'h1' | 'p'
  }>(),
  { title: undefined, loading: false, bare: false, titleTag: 'h1' },
)

defineSlots<{
  leading?(p: { compact: boolean }): unknown
  default?(p: { compact: boolean }): unknown
  actions?(p: { compact: boolean }): unknown
  below?(p: { compact: boolean }): unknown
}>()

const COMPACT_BELOW = 720

const ui = useUi()
const { isDesktop, isPhone } = useBreakpoint()
const root = ref<HTMLElement | null>(null)
const actionsEl = ref<HTMLElement | null>(null)
const width = ref(Number.POSITIVE_INFINITY)
const compact = computed(() => width.value < COMPACT_BELOW)
const showSidebarButton = computed(() => !isDesktop.value || ui.sidebarCollapsed)
const appBar = computed(() => isPhone.value && !props.bare)
const rendered = computed(() => !props.bare || showSidebarButton.value)

let observer: ResizeObserver | null = null

function observe(el: HTMLElement | null): void {
  observer?.disconnect()
  if (!el || typeof ResizeObserver === 'undefined') return
  width.value = el.getBoundingClientRect().width || Number.POSITIVE_INFINITY
  observer ??= new ResizeObserver(([entry]) => {
    if (entry) width.value = entry.borderBoxSize?.[0]?.inlineSize ?? entry.contentRect.width
  })
  observer.observe(el)
}

watch(root, observe, { flush: 'post' })
onMounted(() => observe(root.value))
onBeforeUnmount(() => observer?.disconnect())

// The compacted primary action's label, read from the DOM, becomes the actions' tooltip.
const compactLabel = ref<string | null>(null)

function readCompactLabel(): void {
  const el = actionsEl.value
  const primaries = compact.value && el ? el.querySelectorAll<HTMLElement>('.ui-button--primary') : []
  const only = primaries.length === 1 ? primaries[0] : null
  const label = only && only.querySelector('.ui-icon') ? (only.querySelector('.label')?.textContent?.trim() ?? '') : ''
  compactLabel.value = label || null
}

watch(compact, () => void nextTick(readCompactLabel))
onMounted(readCompactLabel)
onUpdated(readCompactLabel)

/** The global shortcut whose label is the action's label ("New task" → N), if any. */
function shortcutFor(label: string): string | undefined {
  return SHORTCUTS.find((s) => s.group === 'Global' && s.label === label)?.keys
}

const actionsTooltip = computed<TooltipValue>(() =>
  compactLabel.value ? { text: compactLabel.value, kbd: shortcutFor(compactLabel.value), placement: 'bottom' } : null,
)

function onSidebarButton(): void {
  if (isDesktop.value) ui.toggleSidebar()
  else ui.sidebarDrawerOpen = true
}
</script>

<template>
  <div v-if="rendered" class="page-header-wrap" :class="{ 'is-bare': bare, 'is-app-bar': appBar }">
    <header
      ref="root"
      class="page-header"
      :class="{ 'is-bare': bare, 'is-app-bar': appBar, 'is-compact': compact }"
      :data-compact="compact ? 'true' : undefined"
    >
      <UiIconButton
        v-if="showSidebarButton"
        class="sidebar-button"
        :data-sidebar-toggle="isDesktop ? '' : undefined"
        :icon="isDesktop ? PanelLeft : Menu"
        :label="isDesktop ? 'Show sidebar' : 'Open navigation'"
        :kbd="isDesktop ? '[' : undefined"
        :size="isPhone ? 'lg' : 'md'"
        :aria-haspopup="isDesktop ? undefined : 'dialog'"
        @click="onSidebarButton"
      />
      <template v-if="!bare">
        <template v-if="appBar">
          <component :is="titleTag" class="app-bar-title truncate">{{ title }}</component>
          <div class="actions">
            <UiIconButton :icon="Search" label="Search" size="lg" @click="ui.openPalette()" />
          </div>
        </template>
        <template v-else>
          <div v-if="$slots.leading" class="leading"><slot name="leading" :compact="compact" /></div>
          <div class="title-area"><slot :compact="compact" /></div>
          <div v-if="$slots.actions" ref="actionsEl" v-tooltip="actionsTooltip" class="actions">
            <slot name="actions" :compact="compact" />
          </div>
        </template>
      </template>
      <UiLoadingBar :active="loading" />
    </header>
    <div v-if="appBar && $slots.below" class="below"><slot name="below" :compact="compact" /></div>
  </div>
</template>

<style scoped>
.page-header-wrap {
  position: sticky;
  top: 0;
  z-index: var(--z-sticky);
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-surface);
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
  min-width: 0;
  padding: 0 var(--space-4);
}

.page-header.is-app-bar {
  height: var(--appbar-h);
  padding: 0 var(--space-1-5);
}

.sidebar-button {
  flex-shrink: 0;
  margin-left: calc(var(--space-1-5) * -1);
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
  margin: 0;
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

/* Compact: a primary button with an icon keeps its label only as its accessible name. */
.is-compact .actions :deep(.ui-button--primary:has(> .ui-icon)) {
  width: var(--control-md);
  padding: 0;
}

.is-compact .actions :deep(.ui-button--primary:has(> .ui-icon) > .label),
.is-compact .actions :deep(.ui-button--primary:has(> .ui-icon) > .kbd) {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}

.below {
  padding: 0 var(--space-4) var(--space-2);
}

@media (max-width: 639px) {
  .page-header-wrap.is-bare .page-header {
    height: var(--appbar-h);
    padding: 0 var(--space-1-5);
  }
}
</style>

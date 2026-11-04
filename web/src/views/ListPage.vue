<script setup lang="ts">
// The list page (10.2, 3.5, 8.4): /l/:listId/:view.
// - useListPage resolves the list (switching workspace first when it is in another one), loads its
//   tasks with the focus refresh, sets the selection's list, records recents and the last view,
//   publishes ui.createDefaults (null on unmount) and provides the ListPageContext.
// - It renders the header (ListPageHeader), GuestNotice for guests, the toolbar (search, Filter and
//   the chips; the end slot is the div views teleport their controls into) and the view for :view,
//   each view its own lazily loaded chunk.
// - States: not found, resolving (a skeleton after 200ms), a failed switch or first load (error
//   with retry). Refreshes keep the content and run the header's loading bar.
// The view renders in .view-area, a flex column of definite height that scrolls when the view
// doesn't scroll itself; a view with sticky headers makes its own root the scroll container.
// The phone FAB's room (AppShell's --fab-room, 0 without a FAB; this route is `ownScroll`) ends
// the content, so the last row scrolls clear of the FAB and focus never lands behind it (2.4.11).
// A view that scrolls itself sets `data-own-scroll` on its root and keeps that room at the end of
// its own scroll content instead.
import { computed, defineAsyncComponent, defineComponent, h, toRef, type Component } from 'vue'
import { CircleAlert } from '@lucide/vue'
import { useDelayedFlag } from '../composables/useDelayedFlag'
import { TOOLBAR_END_ID, useListPage } from '../composables/useListPage'
import GuestNotice from '../components/shell/GuestNotice.vue'
import PageHeader from '../components/shell/PageHeader.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import FilterButton from '../components/view/FilterButton.vue'
import FilterChips from '../components/view/FilterChips.vue'
import ListPageHeader from '../components/view/ListPageHeader.vue'
import ViewBoundary from '../components/view/ViewBoundary.vue'
import ViewSearch from '../components/view/ViewSearch.vue'
import ViewSkeleton from '../components/view/ViewSkeleton.vue'
import ViewToolbar from '../components/view/ViewToolbar.vue'
import type { ViewKind } from '../types'

const props = defineProps<{ listId: string; view: string }>()

// ---- The views: one chunk each; a skeleton after 200ms; "Try again" reloads when a chunk is gone ----

const ViewLoadError = defineComponent({
  name: 'ViewLoadError',
  setup: () => () =>
    h(UiErrorState, {
      title: "Couldn't load this view",
      message: 'Check your connection and try again.',
      retry: () => window.location.reload(),
    }),
})

function lazyView(loader: () => Promise<Component>): Component {
  return defineAsyncComponent({
    loader,
    loadingComponent: ViewSkeleton,
    errorComponent: ViewLoadError,
    delay: 200,
    onError: (_err, retry, fail, attempts) => (attempts <= 2 ? void setTimeout(retry, 300 * attempts) : fail()),
  })
}

const VIEWS: Record<ViewKind, Component> = {
  list: lazyView(() => import('./list/ListView.vue')),
  board: lazyView(() => import('./list/BoardView.vue')),
  calendar: lazyView(() => import('./list/CalendarView.vue')),
}

// ---- State ----

const view = computed<ViewKind>(() => (props.view === 'board' || props.view === 'calendar' ? props.view : 'list'))
const { ctx, phase, refreshing, retrySwitch } = useListPage(toRef(props, 'listId'), view)
const { filters, search, canEdit } = ctx

const resolving = computed(() => phase.value === 'resolving')
// While the switch runs (or after it failed) the list is known: its name titles the page.
const fallbackTitle = computed(() => (phase.value === 'not-found' ? 'List not found' : (ctx.list.value?.name ?? '')))
const showSkeleton = useDelayedFlag(resolving)
const failedFirstLoad = computed(() => !!ctx.error.value && ctx.allTasks.value.length === 0)
const viewComponent = computed(() => VIEWS[view.value])
</script>

<template>
  <div class="list-page">
    <template v-if="phase === 'ready'">
      <ListPageHeader :loading="refreshing" />
      <GuestNotice v-if="!canEdit" class="guest-notice" />
      <ViewToolbar>
        <template #start>
          <ViewSearch v-model="search" />
          <FilterButton v-model="filters" :list-id="listId" />
          <FilterChips v-model="filters" :list-id="listId" />
        </template>
        <template #end>
          <div :id="TOOLBAR_END_ID" class="toolbar-end" />
        </template>
      </ViewToolbar>
      <div class="view-area">
        <UiErrorState
          v-if="failedFirstLoad"
          class="state"
          title="Couldn't load this list"
          message="Check your connection and try again."
          :retry="() => void ctx.reload()"
        />
        <ViewBoundary v-else :reset-key="`${listId}:${view}`">
          <component :is="viewComponent" />
        </ViewBoundary>
      </div>
    </template>

    <template v-else>
      <PageHeader :title="fallbackTitle">
        <h1 v-if="fallbackTitle" :class="phase === 'not-found' ? 'sr-only' : 'fallback-title truncate'">{{ fallbackTitle }}</h1>
      </PageHeader>
      <div class="view-area">
        <div v-if="phase === 'not-found'" class="state">
          <UiEmptyState :icon="CircleAlert" title="This list doesn't exist or you don't have access.">
            <template #actions>
              <UiButton variant="primary" to="/">Go to Home</UiButton>
            </template>
          </UiEmptyState>
        </div>
        <UiErrorState
          v-else-if="phase === 'failed'"
          class="state"
          title="Couldn't open this list"
          message="Its workspace didn't load. Check your connection and try again."
          :retry="retrySwitch"
        />
        <ViewSkeleton v-else-if="showSkeleton" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.list-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.fallback-title {
  min-width: 0;
  margin: 0;
  color: var(--text-primary);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.guest-notice {
  flex-shrink: 0;
  margin: var(--space-3) var(--space-4) 0;
}

.toolbar-end {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.view-area {
  position: relative;
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  overflow: auto;
  scroll-padding-bottom: var(--fab-room, 0px);
}

/* ViewBoundary's root (it carries this page's scope): the view overflows it, so the room goes
   inside it, after the view. */
.view-boundary::after {
  content: '';
  flex: none;
  height: var(--fab-room, 0px);
}

.view-boundary:has(> [data-own-scroll])::after {
  display: none;
}

.state {
  margin: auto;
  padding: var(--space-8) var(--space-4);
}
</style>

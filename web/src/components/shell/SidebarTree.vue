<script setup lang="ts">
// The Spaces section of the sidebar (3.4, 8.3): the "Spaces" label with "New space" (Member+), then
// the spaces as a <ul> of disclosure rows, each followed by its lists. A nav with disclosures, not
// an ARIA tree. Keys: Up and Down move between visible rows (roving tabindex), Home and End jump,
// Right expands a space or moves to its first list, Left collapses it or moves from a list to its
// space; Enter opens; F2 and Shift+F10 are handled by the rows. States: skeleton rows while the
// workspace loads (after 200ms), an inline error with "Try again", and the empty workspace.
import { computed, ref, useId, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Plus } from '@lucide/vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useRovingFocus } from '../../composables/useRovingFocus'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import UiButton from '../ui/UiButton.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import SidebarSpace from './SidebarSpace.vue'
import { useSidebarState } from './sidebarState'
import { skeletonDelay } from './skeletonHandover'

const workspaces = useWorkspaces()
const ui = useUi()
const route = useRoute()
const sidebar = useSidebarState()

const labelId = `${useId()}-spaces`
const tree = ref<HTMLElement | null>(null)

const spaces = computed(() => [...(workspaces.current?.spaces ?? [])].sort((a, b) => a.position - b.position))
const canEdit = computed(() => workspaces.canEdit())
const loading = computed(() => !workspaces.loaded && !workspaces.error)
const showSkeleton = useDelayedFlag(loading, skeletonDelay())
const failed = computed(() => !workspaces.loaded && !!workspaces.error)

const activeListId = computed(() => {
  if (route.name !== 'list') return null
  const id = route.params.listId
  return typeof id === 'string' ? id : null
})

// A space opens when one of its lists becomes the active page (3.4).
watch(
  () => (activeListId.value ? (workspaces.findList(activeListId.value)?.space.id ?? null) : null),
  (spaceId) => {
    if (spaceId) sidebar.setCollapsed(spaceId, false)
  },
  { immediate: true },
)

// Forget collapsed spaces that no longer exist in any workspace.
watch(
  () => workspaces.loaded && workspaces.list.length > 0,
  (ready) => {
    if (!ready) return
    sidebar.prune(new Set(workspaces.list.flatMap((w) => w.spaces.map((s) => s.id))))
  },
  { immediate: true },
)

useRovingFocus(tree, { itemSelector: '[data-sidebar-item]', orientation: 'vertical' })

function itemFor(selector: string): HTMLElement | null {
  return tree.value?.querySelector<HTMLElement>(selector) ?? null
}

function onKeydown(e: KeyboardEvent): void {
  if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
  if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return
  const item = e.target instanceof HTMLElement ? e.target : null
  // Only the rows themselves: keys in the rename field or on a row's buttons are theirs.
  if (!item || !item.matches('[data-sidebar-item]')) return
  const kind = item.dataset.sidebarItem
  const spaceId = item.dataset.spaceId
  if (!spaceId) return
  e.preventDefault()
  const spaceRow = itemFor(`[data-sidebar-item="space"][data-space-id="${CSS.escape(spaceId)}"]`)
  if (kind === 'space') {
    const open = !sidebar.isCollapsed(spaceId)
    if (e.key === 'ArrowRight') {
      if (!open) sidebar.setCollapsed(spaceId, false)
      else spaceRow?.closest('li')?.querySelector<HTMLElement>('.lists [data-sidebar-item]')?.focus()
    } else if (open) {
      sidebar.setCollapsed(spaceId, true)
    }
  } else if (e.key === 'ArrowLeft') {
    spaceRow?.focus()
  }
}
</script>

<template>
  <div class="spaces-section">
    <div class="section-head">
      <span :id="labelId" class="section-label">Spaces</span>
      <UiIconButton
        v-if="canEdit && workspaces.loaded && spaces.length"
        class="new-space"
        :icon="Plus"
        label="New space"
        size="xs"
        @click="ui.openNewSpace()"
      />
    </div>
    <div v-if="loading" class="skeleton" :aria-busy="showSkeleton ? 'true' : undefined">
      <template v-if="showSkeleton">
        <div v-for="(w, i) in ['62%', '78%', '54%', '70%', '46%', '66%']" :key="i" class="skeleton-row" :class="{ indent: i % 3 !== 0 }">
          <UiSkeleton variant="rect" width="16px" height="16px" />
          <UiSkeleton :width="w" />
        </div>
      </template>
    </div>
    <p v-else-if="failed" class="inline-error">
      Couldn't load spaces
      <UiButton variant="link" size="sm" @click="workspaces.refresh()">Try again</UiButton>
    </p>
    <div v-else-if="!spaces.length" class="empty">
      <p class="empty-text">No spaces yet</p>
      <UiButton v-if="canEdit" variant="secondary" size="sm" :icon="Plus" @click="ui.openNewSpace()">New space</UiButton>
    </div>
    <ul v-else ref="tree" class="spaces" :aria-labelledby="labelId" @keydown="onKeydown">
      <SidebarSpace v-for="s in spaces" :key="s.id" :space="s" :active-list-id="activeListId" />
    </ul>
  </div>
</template>

<style scoped>
.spaces-section {
  margin-top: var(--space-4);
}

.section-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: var(--control-xs);
  padding: 0 var(--space-1) 0 var(--space-2);
  margin-bottom: var(--space-0-5);
}

/* Touch (7.5): the label row is a full 44px row, so "New space" keeps its 44px hit area. */
@media (pointer: coarse) {
  .section-head {
    min-height: var(--touch-min);
  }
}

.section-label {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.spaces {
  margin: 0;
  padding: 0;
  list-style: none;
}

.skeleton {
  display: flex;
  flex-direction: column;
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--row-sidebar);
  padding: 0 var(--space-2);
}

.skeleton-row.indent {
  padding-left: 28px;
}

.inline-error {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1) var(--space-2);
  padding: var(--space-1) var(--space-2);
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.empty {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-2);
  padding: var(--space-1) var(--space-2);
}

.empty-text {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}
</style>

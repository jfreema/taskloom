<script setup lang="ts">
// The list page's header (3.5, 8.4): the space tile and name, "/", the list name as the page's h1
// (click to rename for Member+; Rename in the list menu does the same), the list's "..." menu (the
// sidebar's list items), the view tabs, and "New task" (Member+, quick create with
// ui.createDefaults). Narrower than 720px the tabs and "New task" go icon-only and the space name
// is left out; phones get the app bar with the list name and the tabs as a segmented row below.
import { computed, ref } from 'vue'
import { Ellipsis, SquarePen } from '@lucide/vue'
import { useListPageContext } from '../../composables/useListPageContext'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import SpaceTile from '../fields/SpaceTile.vue'
import PageHeader from '../shell/PageHeader.vue'
import { useHierarchyMenus } from '../shell/hierarchyMenus'
import UiButton from '../ui/UiButton.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'
import UiMenu from '../ui/UiMenu.vue'
import ViewTabs from './ViewTabs.vue'

defineProps<{ loading?: boolean }>()

const ctx = useListPageContext()
const ui = useUi()
const workspaces = useWorkspaces()
const menus = useHierarchyMenus()

const renaming = ref(false)
const name = computed(() => ctx.list.value?.name ?? '')
const canEdit = ctx.canEdit

function rename(value: string): void {
  void workspaces.updateList(ctx.listId.value, { name: value }).catch(() => undefined)
}

function menuItems() {
  return menus.listMenuItems(ctx.listId.value, { onRename: canEdit.value ? () => (renaming.value = true) : undefined })
}

function newTask(): void {
  ui.openQuickCreate(ui.createDefaults ?? undefined)
}
</script>

<template>
  <PageHeader :title="name" :loading="loading">
    <template #default="{ compact }">
      <div class="crumbs">
        <SpaceTile v-if="ctx.space.value" :space="ctx.space.value" :size="18" />
        <template v-if="!compact && ctx.space.value">
          <span v-tooltip="{ text: ctx.space.value.name, overflowOnly: true }" class="space-name truncate">{{ ctx.space.value.name }}</span>
          <span class="sep" aria-hidden="true">/</span>
        </template>
        <h1 class="list-name">
          <UiInlineEdit
            v-if="canEdit"
            v-model:editing="renaming"
            :model-value="name"
            aria-label="List name"
            :maxlength="200"
            @save="rename"
          />
          <span v-else v-tooltip="{ text: name, overflowOnly: true }" class="truncate">{{ name }}</span>
        </h1>
        <UiMenu :items="menuItems" :aria-label="`More actions for ${name}`">
          <template #trigger="{ props: t }">
            <UiIconButton v-bind="t" class="menu-button" :icon="Ellipsis" :label="`More actions for ${name}`" size="sm" />
          </template>
        </UiMenu>
      </div>
      <ViewTabs class="tabs" :list-id="ctx.listId.value" :current="ctx.view.value" :compact="compact" />
    </template>
    <template v-if="canEdit" #actions="{ compact }">
      <UiButton v-tooltip="compact ? undefined : { text: 'New task', kbd: 'N' }" variant="primary" :icon="SquarePen" @click="newTask">
        New task
      </UiButton>
    </template>
    <template #below>
      <ViewTabs :list-id="ctx.listId.value" :current="ctx.view.value" segmented />
    </template>
  </PageHeader>
</template>

<style scoped>
.crumbs {
  display: flex;
  flex: 0 1 auto;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
}

.space-name {
  flex: 0 1 auto;
  min-width: 0;
  max-width: 200px;
  color: var(--text-tertiary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.sep {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.list-name {
  display: flex;
  flex: 0 1 auto;
  min-width: 0;
  margin: 0;
  color: var(--text-primary);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.menu-button {
  flex-shrink: 0;
}

.tabs {
  margin-left: var(--space-3);
}
</style>

<script setup lang="ts">
// Settings › Tags (8.13) for the current workspace. Tags are shared by every list in it.
// - "New tag" (Member+) opens an inline row with a name and a colour (NewTagRow).
// - Each row (TagRow): the chip, the name (inline rename, Member+), the colour (Member+) and
//   Delete (Admin+, after a confirm). Every change is optimistic; the store toasts and rolls back.
// - States: skeleton rows while loading (after 200ms), an error with "Try again", and "No tags
//   yet" with "New tag". Guests see the tags without controls.
import { computed, nextTick, ref, watch } from 'vue'
import { Plus, Tag as TagIcon } from '@lucide/vue'
import NewTagRow from '../../components/settings/NewTagRow.vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import TagRow from '../../components/settings/TagRow.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiErrorState from '../../components/ui/UiErrorState.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import { skeletonDelay } from '../../components/shell/skeletonHandover'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { useConfirm } from '../../composables/useConfirm'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useToast } from '../../composables/useToast'
import { useTags } from '../../stores/tags'
import { useWorkspaces } from '../../stores/workspaces'
import type { Tag } from '../../types'

const tags = useTags()
const workspaces = useWorkspaces()
const confirm = useConfirm()
const toast = useToast()
const announce = useAnnouncer()

const canEdit = computed(() => workspaces.canEdit())
const canDelete = computed(() => workspaces.isAdmin())
const adding = ref(false)
const root = ref<HTMLElement | null>(null)

watch(
  () => workspaces.currentId,
  () => {
    adding.value = false
    void tags.ensure()
  },
  { immediate: true },
)

// Losing the role closes the new tag row.
watch(canEdit, (can) => {
  if (!can) adding.value = false
})

const list = computed(() => tags.current)

const state = computed<'loading' | 'error' | 'empty' | 'list'>(() => {
  if (list.value.length) return 'list'
  if (tags.loading) return 'loading'
  return tags.loadError ? 'error' : 'empty'
})

const showSkeleton = useDelayedFlag(computed(() => state.value === 'loading'), skeletonDelay())

const description = computed(() => `Tags group related tasks across the lists of ${workspaces.current?.name ?? 'this workspace'}.`)

function focusNewTagButton(): void {
  void nextTick(() => root.value?.querySelector<HTMLElement>('[data-new-tag]')?.focus({ preventScroll: true }))
}

function startAdding(): void {
  adding.value = true
}

function closeAdding(): void {
  adding.value = false
  focusNewTagButton()
}

function add(r: { name: string; color: string }): void {
  void tags.create(r).catch(() => undefined)
  announce(`Tag ${r.name} added`)
}

function rename(tag: Tag, name: string): void {
  const taken = tags.current.find((t) => t.id !== tag.id && t.name.trim().toLowerCase() === name.trim().toLowerCase())
  if (taken) {
    toast.show({ message: `A tag called “${taken.name}” already exists.`, tone: 'danger' })
    return
  }
  void tags.update(tag.id, { name }).catch(() => undefined)
}

function recolour(tag: Tag, color: string): void {
  void tags.update(tag.id, { color }).catch(() => undefined)
}

async function remove(tag: Tag): Promise<void> {
  const ok = await confirm({
    title: `Delete tag “${tag.name}”?`,
    message: "It's removed from every task. This can't be undone.",
    confirmLabel: 'Delete tag',
    danger: true,
  })
  if (!ok) return
  const index = list.value.findIndex((t) => t.id === tag.id)
  void tags.remove(tag.id).catch(() => undefined)
  announce(`Tag ${tag.name} deleted`)
  // Its row is gone: the next row's Delete button, else the previous one's, else "New tag".
  void nextTick(() => {
    const buttons = Array.from(root.value?.querySelectorAll<HTMLElement>('[data-tag-id] button[aria-label^="Delete "]') ?? [])
    const target = buttons[index] ?? buttons[index - 1] ?? root.value?.querySelector<HTMLElement>('[data-new-tag]')
    target?.focus({ preventScroll: true })
  })
}
</script>

<template>
  <div ref="root" class="tags-page">
    <SettingsPage title="Tags" :description="description" :busy="state === 'loading'">
      <template v-if="canEdit && (state === 'list' || adding)" #actions>
        <UiButton variant="primary" :icon="Plus" data-new-tag :disabled="adding" @click="startAdding">New tag</UiButton>
      </template>

      <div v-if="state === 'loading' && !adding" aria-hidden="true">
        <div v-if="showSkeleton" class="frame">
          <div v-for="i in 4" :key="i" class="skeleton-row">
            <UiSkeleton variant="rect" width="72px" height="22px" />
            <UiSkeleton width="120px" />
          </div>
        </div>
      </div>

      <UiErrorState
        v-else-if="state === 'error' && !adding"
        title="Couldn't load the tags"
        message="Check your connection and try again."
        :retry="() => void tags.reload()"
      />

      <div v-else-if="state === 'empty' && !adding" class="frame">
        <UiEmptyState :icon="TagIcon" title="No tags yet" description="Tags group related tasks across lists.">
          <template v-if="canEdit" #actions>
            <UiButton variant="primary" :icon="Plus" data-new-tag @click="startAdding">New tag</UiButton>
          </template>
        </UiEmptyState>
      </div>

      <div v-else class="frame">
        <NewTagRow v-if="adding" @add="add" @close="closeAdding" />
        <ul v-if="list.length" class="tag-list" role="list" aria-label="Tags">
          <TagRow
            v-for="t in list"
            :key="t.id"
            :tag="t"
            :can-edit="canEdit"
            :can-delete="canDelete"
            @rename="rename(t, $event)"
            @recolour="recolour(t, $event)"
            @remove="remove(t)"
          />
        </ul>
        <p v-else class="none-yet">Your first tag shows up here.</p>
      </div>
    </SettingsPage>
  </div>
</template>

<style scoped>
.tags-page {
  display: contents;
}

.frame {
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  overflow: hidden;
}

.tag-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.none-yet {
  padding: var(--space-4);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  height: 48px;
  padding: 0 var(--space-4);
}

.skeleton-row + .skeleton-row {
  border-top: 1px solid var(--border-subtle);
}
</style>

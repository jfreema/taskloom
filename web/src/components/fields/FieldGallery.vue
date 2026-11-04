<script setup lang="ts">
// The field and view components (7.11, 7.12) in the /_ui gallery, live on the signed-in user's
// own workspace (seed it for the full picture: POST /api/dev/seed). /_ui sits outside AppShell, so
// this loads the workspace tree, members, tags and one list's tasks itself, and mounts its own
// MoveTaskDialog. The demos are in ./gallery, one card each.
import { computed, watch } from 'vue'
import { useMembers } from '../../stores/members'
import { useTags } from '../../stores/tags'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import GalleryCard from '../ui/gallery/GalleryCard.vue'
import MoveTaskDialog from './MoveTaskDialog.vue'
import GalleryDates from './gallery/GalleryDates.vue'
import GalleryLists from './gallery/GalleryLists.vue'
import GalleryMarks from './gallery/GalleryMarks.vue'
import GalleryPeople from './gallery/GalleryPeople.vue'
import GalleryStatusFields from './gallery/GalleryStatusFields.vue'
import GalleryViewParts from './gallery/GalleryViewParts.vue'

const workspaces = useWorkspaces()
const members = useMembers()
const tags = useTags()
const tasks = useTasks()

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

/** The seed's "Website refresh" when there is one, else the first list. */
const sample = computed(() => workspaces.allLists.find((x) => x.list.name === 'Website refresh') ?? workspaces.allLists[0] ?? null)

watch(
  () => sample.value?.list.id,
  (id) => {
    if (id) void tasks.loadList(id)
  },
  { immediate: true },
)
</script>

<template>
  <template v-if="sample">
    <GalleryStatusFields :list-id="sample.list.id" />
    <GalleryMarks :list-id="sample.list.id" />
    <GalleryPeople />
    <GalleryDates />
    <GalleryLists :list-id="sample.list.id" />
    <GalleryViewParts :list-id="sample.list.id" />
  </template>
  <GalleryCard v-else-if="workspaces.loaded" title="Field components">
    <p class="note">This workspace has no list yet. Seed it (POST /api/dev/seed) to see the fields.</p>
  </GalleryCard>
  <MoveTaskDialog />
</template>

<style scoped>
.note {
  color: var(--text-secondary);
}
</style>

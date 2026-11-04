<script setup lang="ts">
// /_ui: SpaceTile (each space, the sizes), ListPicker (chip and field; search by list or space
// name), SubtaskProgress, TaskCounts and MoveTaskDialog, which moves two real tasks of the sample
// list (Undo in the toast moves them back).
import { computed, ref } from 'vue'
import { useTasks } from '../../../stores/tasks'
import { useUi } from '../../../stores/ui'
import { useWorkspaces } from '../../../stores/workspaces'
import type { Uuid } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import UiButton from '../../ui/UiButton.vue'
import ListPicker from '../ListPicker.vue'
import SpaceTile from '../SpaceTile.vue'
import SubtaskProgress from '../SubtaskProgress.vue'
import TaskCounts from '../TaskCounts.vue'

const props = defineProps<{ listId: Uuid }>()

const workspaces = useWorkspaces()
const tasks = useTasks()
const ui = useUi()

const spaces = computed(() => workspaces.current?.spaces ?? [])
const chipList = ref<Uuid | null>(props.listId)
const fieldList = ref<Uuid | null>(null)
const nameOf = (id: Uuid | null) => (id ? (workspaces.findList(id)?.list.name ?? '?') : 'none')

const movable = computed(() => tasks.topLevelInList(props.listId).slice(0, 2))
const listName = computed(() => workspaces.findList(props.listId)?.list.name ?? 'this list')
</script>

<template>
  <GalleryCard title="Lists, progress and moving">
    <div class="row">
      <span v-for="s in spaces" :key="s.id" class="labelled">
        <SpaceTile :space="s" />
        <span class="small muted">{{ s.name }}</span>
      </span>
    </div>
    <div v-if="spaces[0]" class="row">
      <SpaceTile v-for="size in [16, 18, 20, 24, 32] as const" :key="size" :space="spaces[0]" :size="size" />
    </div>
    <div class="row">
      <ListPicker v-model="chipList" variant="chip" />
    </div>
    <div class="stack">
      <ListPicker v-model="fieldList" class="stretch" variant="field" :exclude-list-id="listId" />
    </div>
    <p class="small muted">Chip: {{ nameOf(chipList) }} · Field: {{ nameOf(fieldList) }} (leaves out {{ listName }})</p>
    <div class="row">
      <SubtaskProgress :done="2" :total="5" />
      <SubtaskProgress :done="3" :total="3" />
      <TaskCounts :task="{ commentCount: 3, attachmentCount: 1 }" />
      <TaskCounts :task="{ commentCount: 0, attachmentCount: 2 }" />
    </div>
    <div class="row">
      <UiButton size="sm" :disabled="movable.length < 2" @click="ui.openMoveTasks(movable.map((t) => t.id))">Move 2 tasks…</UiButton>
      <span class="small muted">{{ movable.map((t) => t.title).join(' and ') || 'No tasks to move' }}</span>
    </div>
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

<style scoped>
.labelled {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
}
</style>

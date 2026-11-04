<script setup lang="ts">
// /_ui gallery: toasts (with Undo, which Mod+Z also runs), confirms, the tooltip and the announcer.
import { ref } from 'vue'
import { Calendar, Link, Trash2 } from '@lucide/vue'
import { useAnnouncer } from '../../../composables/useAnnouncer'
import { useConfirm } from '../../../composables/useConfirm'
import { useToast } from '../../../composables/useToast'
import UiButton from '../UiButton.vue'
import UiIconButton from '../UiIconButton.vue'
import GalleryCard from './GalleryCard.vue'

const toast = useToast()
const confirm = useConfirm()
const announce = useAnnouncer()

// Delete with undo (9.2): the row hides at once, Undo (or Mod+Z) shows it again, and any other
// close commits the delete.
const tasks = ref(['Plan Q1 roadmap', 'Send invoice to Contoso', 'Book team offsite'])
const hidden = ref<string[]>([])
const committed = ref<string[]>([])

function remove(title: string): void {
  hidden.value = [...hidden.value, title]
  toast.undoable(
    `Deleted “${title}”`,
    () => {
      hidden.value = hidden.value.filter((t) => t !== title)
    },
    {
      onCommit: () => {
        tasks.value = tasks.value.filter((t) => t !== title)
        hidden.value = hidden.value.filter((t) => t !== title)
        committed.value = [...committed.value, title]
      },
    },
  )
}

function reset(): void {
  tasks.value = ['Plan Q1 roadmap', 'Send invoice to Contoso', 'Book team offsite']
  hidden.value = []
  committed.value = []
}

function showMany(): void {
  for (const n of [1, 2, 3, 4]) toast.show({ message: `Notification ${n} of 4` })
}

async function askPlain(): Promise<void> {
  const ok = await confirm({ title: 'Create 3 tasks?', message: 'One, Two and Three.', confirmLabel: 'Create 3 tasks' })
  toast.show({ message: ok ? 'Created 3 tasks' : 'Cancelled', tone: ok ? 'success' : 'neutral' })
}

async function askDanger(): Promise<void> {
  const ok = await confirm({
    title: 'Delete “Admin”?',
    message: "Deletes 16 tasks. This can't be undone.",
    confirmLabel: 'Delete list',
    danger: true,
  })
  toast.show({ message: ok ? 'Confirmed' : 'Cancelled' })
}

async function askTyped(): Promise<void> {
  const ok = await confirm({
    title: 'Delete space?',
    message: "Deletes 2 lists and 37 tasks. This can't be undone.",
    confirmLabel: 'Delete space',
    danger: true,
    requireText: 'Work',
  })
  toast.show({ message: ok ? 'Confirmed' : 'Cancelled' })
}
</script>

<template>
  <GalleryCard title="Toasts" wide>
    <div class="row">
      <UiButton size="sm" @click="toast.show({ message: 'Moved to Website refresh', action: { label: 'Open', run: () => toast.success('Opened') } })">
        Neutral
      </UiButton>
      <UiButton size="sm" @click="toast.success('Link copied')">Success</UiButton>
      <UiButton size="sm" @click="toast.error(new Error('boom'), { retry: () => toast.success('Saved') })">Error</UiButton>
      <UiButton size="sm" @click="showMany">Show 4</UiButton>
    </div>
    <ul class="tasks" aria-label="Sample tasks">
      <li v-for="t in tasks.filter((x) => !hidden.includes(x))" :key="t" class="task">
        <span class="grow">{{ t }}</span>
        <UiIconButton :icon="Trash2" :label="`Delete ${t}`" size="xs" variant="danger-ghost" @click="remove(t)" />
      </li>
    </ul>
    <div class="row">
      <p class="muted small grow">Deleted for good: {{ committed.length ? committed.join(', ') : 'nothing yet' }}</p>
      <UiButton size="xs" variant="ghost" @click="reset">Reset</UiButton>
    </div>
  </GalleryCard>

  <GalleryCard title="Confirm">
    <div class="row">
      <UiButton size="sm" @click="askPlain">Confirm</UiButton>
      <UiButton size="sm" variant="danger-ghost" @click="askDanger">Danger</UiButton>
      <UiButton size="sm" variant="danger-ghost" @click="askTyped">Type to confirm</UiButton>
    </div>
  </GalleryCard>

  <GalleryCard title="Tooltip and announcer">
    <div class="row">
      <UiButton v-tooltip="'Copies a link to this task'" size="sm" :icon="Link">Copy link</UiButton>
      <UiButton v-tooltip="{ text: 'Due date', kbd: 'D', placement: 'bottom' }" size="sm" :icon="Calendar">Due</UiButton>
      <UiButton size="sm" @click="announce('Moved “Plan Q1 roadmap” to Done')">Announce</UiButton>
    </div>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.tasks {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
}

.task {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-list);
  padding: 0 var(--space-2) 0 var(--space-3);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.task + .task {
  border-top: 1px solid var(--border-subtle);
}
</style>

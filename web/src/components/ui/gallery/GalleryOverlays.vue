<script setup lang="ts">
// /_ui gallery: UiDialog with pickers inside (a select, a date popover, a menu and a confirm on
// top), and UiDrawer from the left, the right and the bottom, plus a non-modal panel.
import { computed, ref } from 'vue'
import { Calendar, Ellipsis, Link, PanelLeft, Plus, Trash2, X } from '@lucide/vue'
import { useConfirm } from '../../../composables/useConfirm'
import { useToast } from '../../../composables/useToast'
import { useToday } from '../../../composables/useToday'
import { formatDue } from '../../../lib/dates'
import type { IsoDate } from '../../../types'
import UiButton from '../UiButton.vue'
import UiDatePicker from '../UiDatePicker.vue'
import UiDialog from '../UiDialog.vue'
import UiDrawer from '../UiDrawer.vue'
import UiField from '../UiField.vue'
import UiIconButton from '../UiIconButton.vue'
import UiInput from '../UiInput.vue'
import UiMenu from '../UiMenu.vue'
import UiPopover from '../UiPopover.vue'
import UiSelect from '../UiSelect.vue'
import UiSwitch from '../UiSwitch.vue'
import GalleryCard from './GalleryCard.vue'
import type { MenuItem } from '../menu'
import { SPACE_OPTIONS, STATUS_OPTIONS } from './sample'

const toast = useToast()
const confirm = useConfirm()
const today = useToday()

// ---- Dialog ----

const dialogOpen = ref(false)
const title = ref('Plan Q1 roadmap')
const status = ref<string | null>('todo')
const due = ref<IsoDate | null>(null)
const dueOpen = ref(false)
const dueText = computed(() => (due.value ? formatDue(due.value, today.value) : null))
const saved = ref('')

const taskMenu = (): MenuItem[] => [
  { id: 'copy', label: 'Copy link', icon: Link, onSelect: () => toast.success('Link copied') },
  { kind: 'separator', id: 'sep' },
  { id: 'delete', label: 'Delete task', icon: Trash2, danger: true, onSelect: () => void askDelete() },
]

async function askDelete(): Promise<void> {
  const ok = await confirm({ title: `Delete “${title.value}”?`, message: 'Its subtasks are deleted too.', confirmLabel: 'Delete task', danger: true })
  if (ok) dialogOpen.value = false
}

function pickDue(v: IsoDate | null): void {
  due.value = v
  dueOpen.value = false
}

function save(): void {
  saved.value = `Saved “${title.value}”, ${STATUS_OPTIONS.find((o) => o.value === status.value)?.label ?? 'no status'}, due ${dueText.value ?? 'never'}`
  dialogOpen.value = false
}

// ---- Drawers ----

const left = ref(false)
const right = ref(false)
const bottom = ref(false)
const panel = ref(false)
const panelModal = ref(false)
const space = ref<string | null>('home')
</script>

<template>
  <GalleryCard title="UiDialog">
    <div class="row"><UiButton @click="dialogOpen = true">Edit task</UiButton></div>
    <p class="muted small" aria-live="polite">{{ saved }}</p>
    <UiDialog v-model:open="dialogOpen" title="Edit task" description="A dialog with pickers inside: each opens above it.">
      <template #header-actions>
        <UiMenu :items="taskMenu" aria-label="More task actions" placement="bottom-end">
          <template #trigger="{ props }">
            <UiIconButton v-bind="props" :icon="Ellipsis" label="More task actions" />
          </template>
        </UiMenu>
      </template>
      <div class="stack">
        <UiField label="Task title">
          <template #default="{ id }"><UiInput :id="id" v-model="title" @enter="save" /></template>
        </UiField>
        <UiField label="Status">
          <template #default="{ id }"><UiSelect :id="id" v-model="status" :options="STATUS_OPTIONS" /></template>
        </UiField>
        <UiPopover v-model:open="dueOpen" aria-label="Due date">
          <template #trigger="{ props }">
            <UiButton v-bind="props" :icon="due ? Calendar : Plus" size="sm">
              {{ dueText ? `Due date: ${dueText}, change` : 'Due date: none, add' }}
            </UiButton>
          </template>
          <UiDatePicker :model-value="due" @update:model-value="pickDue" @cancel="dueOpen = false" />
        </UiPopover>
      </div>
      <template #footer>
        <UiButton @click="dialogOpen = false">Cancel</UiButton>
        <UiButton variant="primary" @click="save">Save</UiButton>
      </template>
    </UiDialog>
  </GalleryCard>

  <GalleryCard title="UiDrawer" wide>
    <div class="row">
      <UiButton :icon="PanelLeft" @click="left = true">Left drawer</UiButton>
      <UiButton @click="right = true">Right drawer</UiButton>
      <UiButton @click="bottom = true">Bottom sheet</UiButton>
      <UiButton :aria-pressed="panel ? 'true' : 'false'" @click="panel = !panel">Non-modal panel</UiButton>
    </div>
    <UiSwitch v-model="panelModal" label="Bottom sheet can't be dismissed" description="Esc and the scrim do nothing; only Close works." />

    <UiDrawer v-model:open="left" side="left" aria-label="Navigation">
      <div class="drawer-head">
        <h2 class="drawer-title">Navigation</h2>
        <UiIconButton :icon="X" label="Close navigation" @click="left = false" />
      </div>
      <nav class="drawer-body" aria-label="Spaces">
        <UiButton v-for="s in SPACE_OPTIONS" :key="s.value" variant="ghost" block @click="left = false">{{ s.label }}</UiButton>
      </nav>
    </UiDrawer>

    <UiDrawer v-model:open="right" side="right" aria-label="List settings" initial-focus="input">
      <div class="drawer-head">
        <h2 class="drawer-title">List settings</h2>
        <UiIconButton :icon="X" label="Close list settings" @click="right = false" />
      </div>
      <div class="drawer-body">
        <UiField label="List name">
          <template #default="{ id }"><UiInput :id="id" model-value="Website refresh" /></template>
        </UiField>
        <UiField label="Space">
          <template #default="{ id }"><UiSelect :id="id" v-model="space" :options="SPACE_OPTIONS" /></template>
        </UiField>
      </div>
    </UiDrawer>

    <UiDrawer v-model:open="bottom" side="bottom" aria-label="Task actions" :dismissible="!panelModal">
      <div class="drawer-body">
        <h2 class="drawer-title">Task actions</h2>
        <UiButton variant="ghost" :icon="Link" block @click="bottom = false">Copy link</UiButton>
        <UiButton variant="danger-ghost" :icon="Trash2" block @click="bottom = false">Delete task</UiButton>
        <UiButton block @click="bottom = false">Close</UiButton>
      </div>
    </UiDrawer>

    <UiDrawer v-model:open="panel" side="right" :modal="false" aria-label="Details" width="320px">
      <div class="drawer-head">
        <h2 class="drawer-title">Details</h2>
        <UiIconButton :icon="X" label="Close details" @click="panel = false" />
      </div>
      <div class="drawer-body">
        <p class="muted">A non-modal panel: the page stays usable, and Esc closes it while focus is inside.</p>
        <UiField label="Space">
          <template #default="{ id }"><UiSelect :id="id" v-model="space" :options="SPACE_OPTIONS" /></template>
        </UiField>
      </div>
    </UiDrawer>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.stack > :deep(.ui-select) {
  align-self: stretch;
}

.drawer-head {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  height: var(--topbar-h);
  padding: 0 var(--space-2) 0 var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}

.drawer-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-4);
  overflow-y: auto;
}

.drawer-body :deep(.ui-button--block) {
  justify-content: flex-start;
}
</style>

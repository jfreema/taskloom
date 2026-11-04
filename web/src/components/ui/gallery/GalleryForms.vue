<script setup lang="ts">
// /_ui gallery: text fields, checkboxes, switches, segmented controls, radio cards, colour
// swatches and inline edit.
import { ref } from 'vue'
import { CalendarDays, List, Pencil, Search, SquareKanban } from '@lucide/vue'
import { DEFAULT_COLORS } from '../../../lib/colors'
import UiCheckbox from '../UiCheckbox.vue'
import UiColorSwatches from '../UiColorSwatches.vue'
import UiField from '../UiField.vue'
import UiIconButton from '../UiIconButton.vue'
import UiInlineEdit from '../UiInlineEdit.vue'
import UiInput from '../UiInput.vue'
import UiRadioCards from '../UiRadioCards.vue'
import UiSegmented from '../UiSegmented.vue'
import UiSwitch from '../UiSwitch.vue'
import UiTextarea from '../UiTextarea.vue'
import GalleryCard from './GalleryCard.vue'

const text = ref('Website refresh')
const search = ref('')
const email = ref('ana@')
const notes = ref('Draft the outline first.\nThen collect screenshots.')
const checks = ref({ a: false, b: true, c: false })
const switchOn = ref(true)
const view = ref('board')
const scope = ref('all')
const preset = ref('Simple')
const color = ref<string>(DEFAULT_COLORS.tag)

const views = [
  { value: 'list', label: 'List', icon: List },
  { value: 'board', label: 'Board', icon: SquareKanban },
  { value: 'calendar', label: 'Calendar', icon: CalendarDays },
]

// UiInlineEdit: a heading that renames on click, and a row renamed from its pencil (activate="none").
const listName = ref('Website refresh')
const taskTitle = ref('Plan Q1 roadmap and share it with the team before the offsite')
const rowEditing = ref(false)
const lastEdit = ref('')

function saveList(v: string): void {
  listName.value = v
  lastEdit.value = `Saved “${v}”`
}

function saveTask(v: string): void {
  taskTitle.value = v
  lastEdit.value = `Saved “${v}”`
}
</script>

<template>
  <GalleryCard title="UiInput">
    <div class="stack">
      <UiInput v-model="text" aria-label="List name" />
      <UiInput v-model="search" :icon="Search" placeholder="Search this list" aria-label="Search" clearable size="sm" />
      <UiInput v-model="email" type="email" invalid aria-label="Email" />
      <UiInput model-value="Read only" disabled aria-label="Disabled" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiTextarea">
    <UiTextarea v-model="notes" placeholder="Add a description" aria-label="Notes" :max-rows="5" />
  </GalleryCard>

  <GalleryCard title="UiField">
    <div class="stack">
      <UiField label="Workspace name" hint="Everyone in the workspace sees it." required>
        <template #default="{ id, describedBy }">
          <UiInput :id="id" v-model="text" :aria-describedby="describedBy" />
        </template>
      </UiField>
      <UiField label="Email" error="Enter a valid email address.">
        <template #default="{ id, describedBy, invalid }">
          <UiInput :id="id" v-model="email" :invalid="invalid" :aria-describedby="describedBy" />
        </template>
      </UiField>
    </div>
  </GalleryCard>

  <GalleryCard title="UiInlineEdit">
    <div class="stack">
      <h3 class="list-title"><UiInlineEdit :model-value="listName" aria-label="List name" :maxlength="200" @save="saveList" /></h3>
      <div class="edit-row">
        <UiInlineEdit
          v-model:editing="rowEditing"
          class="grow"
          :model-value="taskTitle"
          aria-label="Task title"
          activate="none"
          multiline
          :maxlength="500"
          @save="saveTask"
        />
        <UiIconButton :icon="Pencil" :label="`Rename ${taskTitle}`" @click="rowEditing = true" />
      </div>
      <UiInlineEdit model-value="Read only name" aria-label="Space name" disabled />
      <p class="muted small" aria-live="polite">{{ lastEdit }}</p>
    </div>
  </GalleryCard>

  <GalleryCard title="UiCheckbox">
    <div class="stack">
      <UiCheckbox v-model="checks.a" label="Show completed tasks" />
      <UiCheckbox v-model="checks.b" label="Assigned to me" />
      <UiCheckbox v-model="checks.c" indeterminate label="Select all in To do" />
      <UiCheckbox :model-value="true" disabled label="Disabled" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiSwitch">
    <div class="stack">
      <UiSwitch v-model="switchOn" label="Single-key shortcuts" description="Use keys like N, S and J without Ctrl or Cmd." />
      <UiSwitch :model-value="false" label="Create more" />
      <UiSwitch :model-value="true" label="Disabled" disabled />
    </div>
  </GalleryCard>

  <GalleryCard title="UiSegmented">
    <div class="stack">
      <UiSegmented v-model="view" aria-label="View" :options="views" />
      <UiSegmented
        v-model="scope"
        aria-label="Show"
        size="sm"
        :options="[
          { value: 'all', label: 'All mine', count: 12 },
          { value: 'assigned', label: 'Assigned to me', count: 4 },
        ]"
      />
      <UiSegmented v-model="view" aria-label="View, icons" icon-only :options="views" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiRadioCards" wide>
    <UiRadioCards
      v-model="preset"
      name="preset"
      aria-label="Statuses"
      :options="[
        { value: 'Simple', label: 'Simple', description: 'To do, In progress, Done' },
        { value: 'Review', label: 'Review', description: 'Adds In review before Done' },
        { value: 'Requests', label: 'Requests', description: 'New, Accepted, Shipped, Declined' },
      ]"
    />
  </GalleryCard>

  <GalleryCard title="UiColorSwatches">
    <UiColorSwatches v-model="color" aria-label="Space colour" />
    <p class="muted small">Selected {{ color }}</p>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.list-title {
  align-self: stretch;
  max-width: 100%;
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.edit-row {
  display: flex;
  align-self: stretch;
  align-items: flex-start;
  gap: var(--space-2);
  min-width: 0;
}
</style>

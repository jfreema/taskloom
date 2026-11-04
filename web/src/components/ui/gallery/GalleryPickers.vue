<script setup lang="ts">
// /_ui gallery: UiListbox (single with groups and a footer; multiple with search and a clear row;
// creatable), UiSelect, and UiDatePicker in a popover and inline.
import { computed, ref } from 'vue'
import { Calendar, Plus, Settings, Tag } from '@lucide/vue'
import { useToday } from '../../../composables/useToday'
import { addDaysIso, formatDue } from '../../../lib/dates'
import type { IsoDate } from '../../../types'
import UiButton from '../UiButton.vue'
import UiDatePicker from '../UiDatePicker.vue'
import UiField from '../UiField.vue'
import UiListbox from '../UiListbox.vue'
import UiPopover from '../UiPopover.vue'
import UiSelect from '../UiSelect.vue'
import GalleryCard from './GalleryCard.vue'
import { MEMBER_OPTIONS, ROLE_OPTIONS, SPACE_OPTIONS, STATUS_OPTIONS, TAG_NAMES, tagOption } from './sample'

const today = useToday()

// ---- Listboxes ----

const status = ref<string | null>('progress')
const assignees = ref<string[]>(['ana'])
const tagNames = ref([...TAG_NAMES])
const tags = ref<string[]>(['design'])
const tagOptions = computed(() => tagNames.value.map(tagOption))
const footerNote = ref('')

function asList(v: string | string[] | null): string[] {
  return Array.isArray(v) ? v : v ? [v] : []
}

function createTag(name: string): void {
  if (!tagNames.value.includes(name)) tagNames.value = [...tagNames.value, name]
  tags.value = [...tags.value, name]
}

const labelOf = (options: { value: string; label: string }[], values: string[]) =>
  values.map((v) => options.find((o) => o.value === v)?.label ?? v).join(', ') || 'none'

// ---- Selects ----

const space = ref<string | null>('work')
const role = ref<string | null>('Member')
const empty = ref<string | null>(null)

// ---- Dates ----

const due = ref<IsoDate | null>(addDaysIso(today.value, 2))
const start = ref<IsoDate | null>(today.value)
const dueOpen = ref(false)
const dueText = computed(() => (due.value ? formatDue(due.value, today.value) : null))

function pickDue(v: IsoDate | null): void {
  due.value = v
  dueOpen.value = false
}
</script>

<template>
  <GalleryCard title="UiListbox, single">
    <div class="frame">
      <UiListbox v-model="status" :options="STATUS_OPTIONS" aria-label="Status">
        <template #footer>
          <UiButton variant="ghost" size="sm" :icon="Settings" block @click="footerNote = 'Edit statuses'">Edit statuses</UiButton>
        </template>
      </UiListbox>
    </div>
    <p class="muted small">Status: {{ labelOf(STATUS_OPTIONS, asList(status)) }} {{ footerNote && `· ${footerNote}` }}</p>
  </GalleryCard>

  <GalleryCard title="UiListbox, multiple with search">
    <div class="frame">
      <UiListbox
        :model-value="assignees"
        :options="MEMBER_OPTIONS"
        multiple
        clear-label="No assignees"
        search-placeholder="Search people"
        aria-label="Assignees"
        :max-height="232"
        @update:model-value="assignees = asList($event)"
      />
    </div>
    <p class="muted small">Assignees: {{ labelOf(MEMBER_OPTIONS, assignees) }}</p>
  </GalleryCard>

  <GalleryCard title="UiListbox, creatable">
    <div class="frame">
      <UiListbox
        :model-value="tags"
        :options="tagOptions"
        multiple
        searchable
        creatable
        search-placeholder="Search or create tags"
        aria-label="Tags"
        :max-height="232"
        @update:model-value="tags = asList($event)"
        @create="createTag"
      >
        <template #footer>
          <UiButton variant="ghost" size="sm" :icon="Tag" block @click="footerNote = 'Manage tags'">Manage tags</UiButton>
        </template>
      </UiListbox>
    </div>
    <p class="muted small">Tags: {{ tags.join(', ') || 'none' }}</p>
  </GalleryCard>

  <GalleryCard title="UiSelect">
    <div class="stack">
      <UiField label="Space">
        <template #default="{ id, describedBy }">
          <UiSelect :id="id" v-model="space" :options="SPACE_OPTIONS" :aria-describedby="describedBy" />
        </template>
      </UiField>
      <UiSelect v-model="role" :options="ROLE_OPTIONS" aria-label="Role for Ana Moreno" size="sm" />
      <UiField label="List" error="Choose a list.">
        <template #default="{ id, describedBy, invalid }">
          <UiSelect :id="id" v-model="empty" :options="SPACE_OPTIONS" placeholder="Choose a list" :invalid="invalid" :aria-describedby="describedBy" />
        </template>
      </UiField>
      <UiSelect :model-value="'home'" :options="SPACE_OPTIONS" aria-label="Disabled select" disabled />
    </div>
  </GalleryCard>

  <GalleryCard title="UiDatePicker" wide>
    <div class="row">
      <UiPopover v-model:open="dueOpen" aria-label="Due date">
        <template #trigger="{ props }">
          <UiButton v-bind="props" :icon="due ? Calendar : Plus">
            {{ dueText ? `Due date: ${dueText}, change` : 'Due date: none, add' }}
          </UiButton>
        </template>
        <UiDatePicker :model-value="due" :highlight="start" @update:model-value="pickDue" @cancel="dueOpen = false" />
      </UiPopover>
      <span class="muted small">Start {{ start ? formatDue(start, today) : 'none' }}; the start date is ringed.</span>
    </div>
    <div class="frame inline-picker">
      <UiDatePicker v-model="start" :max="due ?? undefined" :highlight="due" clear-label="No start date" />
    </div>
    <p class="muted small">Inline, with the due date as its max: start {{ start ?? 'none' }}</p>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.frame {
  width: 100%;
  max-width: 320px;
}

.frame.inline-picker {
  width: auto;
  max-width: 100%;
  align-self: flex-start;
}
</style>

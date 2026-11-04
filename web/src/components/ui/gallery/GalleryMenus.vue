<script setup lang="ts">
// /_ui gallery: UiPopover and UiMenu (item kinds, groups, submenus, swatches, kbd hints, typeahead).
import { ref } from 'vue'
import { ChevronDown, Ellipsis, Eye, Flag, Keyboard, Link, ListFilter, ListTree, LogOut, Pencil, Trash2 } from '@lucide/vue'
import { useShortcut } from '../../../composables/useShortcut'
import { useTheme } from '../../../composables/useTheme'
import { DEFAULT_COLORS, SWATCHES } from '../../../lib/colors'
import type { ThemePreference } from '../../../types'
import UiButton from '../UiButton.vue'
import UiCheckbox from '../UiCheckbox.vue'
import UiField from '../UiField.vue'
import UiIconButton from '../UiIconButton.vue'
import UiInput from '../UiInput.vue'
import UiMenu from '../UiMenu.vue'
import UiPopover from '../UiPopover.vue'
import GalleryCard from './GalleryCard.vue'
import type { MenuItem } from '../menu'

const { preference, setPreference } = useTheme()
const last = ref('Nothing chosen yet')

function say(text: string): void {
  last.value = text
}

// ---- Popovers ----

const filters = ref({ mine: true, overdue: false, done: false })
const name = ref('Website refresh')
const draft = ref('')

function saveName(close: () => void): void {
  name.value = draft.value.trim() || name.value
  close()
}

// ---- The list menu ----

const STATUSES = [
  { id: 'todo', label: 'To do' },
  { id: 'progress', label: 'In progress' },
  { id: 'done', label: 'Done' },
]
const status = ref('todo')
const SORTS = [
  { id: 'manual', label: 'Manual' },
  { id: 'due', label: 'Due date' },
  { id: 'priority', label: 'Priority' },
]
const sort = ref('manual')
const color = ref<string>(DEFAULT_COLORS.space)
const showCompleted = ref(true)
const showSubtasks = ref(false)

function listItems(): MenuItem[] {
  return [
    { id: 'rename', label: 'Rename', icon: Pencil, kbd: 'F2', onSelect: () => say('Rename') },
    { id: 'copy', label: 'Copy link', icon: Link, onSelect: () => say('Copy link') },
    {
      kind: 'submenu',
      id: 'status',
      label: 'Move to status',
      items: STATUSES.map((s) => ({
        kind: 'radio' as const,
        id: `status-${s.id}`,
        label: s.label,
        checked: status.value === s.id,
        onSelect: () => {
          status.value = s.id
          say(`Status: ${s.label}`)
        },
      })),
    },
    {
      kind: 'submenu',
      id: 'sort',
      label: 'Sort by',
      items: SORTS.map((o) => ({
        kind: 'radio' as const,
        id: `sort-${o.id}`,
        label: o.label,
        checked: sort.value === o.id,
        onSelect: () => {
          sort.value = o.id
          say(`Sort: ${o.label}`)
        },
      })),
    },
    {
      kind: 'swatches',
      id: 'colour',
      label: 'Colour',
      value: color.value,
      onSelect: (hex) => {
        color.value = hex
        say(`Colour: ${SWATCHES.find((s) => s.hex === hex)?.name ?? hex}`)
      },
    },
    { kind: 'separator', id: 'sep-1' },
    { kind: 'label', id: 'show', label: 'Show' },
    { kind: 'checkbox', id: 'completed', label: 'Completed tasks', icon: Eye, checked: showCompleted.value, onSelect: () => (showCompleted.value = !showCompleted.value) },
    { kind: 'checkbox', id: 'subtasks', label: 'Subtasks', icon: ListTree, checked: showSubtasks.value, onSelect: () => (showSubtasks.value = !showSubtasks.value) },
    { kind: 'separator', id: 'sep-2' },
    { id: 'archive', label: 'Archive list', disabled: true, hint: 'Only admins can archive lists', onSelect: () => say('Archive') },
    { id: 'delete', label: 'Delete list', icon: Trash2, kbd: 'Delete', danger: true, onSelect: () => say('Delete list') },
  ]
}

// ---- The priority menu: digits pick while it is open (registered in the layer scope, 9.5) ----

const PRIORITIES = [
  { key: '1', label: 'Urgent' },
  { key: '2', label: 'High' },
  { key: '3', label: 'Normal' },
  { key: '4', label: 'Low' },
]
const priority = ref('Normal')
const priorityOpen = ref(false)

function setPriority(label: string): void {
  priority.value = label
  say(`Priority: ${label}`)
}

const priorityItems = (): MenuItem[] => [
  ...PRIORITIES.map((p) => ({ id: `p-${p.key}`, label: p.label, icon: Flag, kbd: p.key, onSelect: () => setPriority(p.label) })),
  { kind: 'separator', id: 'p-sep' },
  { id: 'p-0', label: 'Clear priority', kbd: '0', onSelect: () => setPriority('None') },
]

useShortcut(
  ['1', '2', '3', '4', '0'],
  (e) => {
    setPriority(PRIORITIES.find((p) => p.key === e.key)?.label ?? 'None')
    priorityOpen.value = false
  },
  { scope: 'layer', when: () => priorityOpen.value },
)

// ---- The account menu: radio items under a group label ----

const THEMES: { value: ThemePreference; label: string }[] = [
  { value: 'system', label: 'System' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

const accountItems = (): MenuItem[] => [
  { kind: 'label', id: 'theme', label: 'Theme' },
  ...THEMES.map((t) => ({ kind: 'radio' as const, id: `theme-${t.value}`, label: t.label, checked: preference.value === t.value, onSelect: () => setPreference(t.value) })),
  { kind: 'separator', id: 'a-sep' },
  { id: 'shortcuts', label: 'Keyboard shortcuts', icon: Keyboard, kbd: '?', onSelect: () => say('Keyboard shortcuts') },
  { id: 'signout', label: 'Sign out', icon: LogOut, onSelect: () => say('Sign out') },
]
</script>

<template>
  <GalleryCard title="UiPopover">
    <div class="row">
      <UiPopover aria-label="Filters" :width="260">
        <template #trigger="{ props }">
          <UiButton v-bind="props" :icon="ListFilter">Filters</UiButton>
        </template>
        <template #default="{ close }">
          <div class="pad">
            <UiCheckbox v-model="filters.mine" label="Assigned to me" />
            <UiCheckbox v-model="filters.overdue" label="Overdue" />
            <UiCheckbox v-model="filters.done" label="Show completed" />
            <div class="row end">
              <UiButton size="sm" variant="primary" @click="close()">Done</UiButton>
            </div>
          </div>
        </template>
      </UiPopover>
      <UiPopover aria-label="Rename list" trap-focus :width="280" @close="draft = ''">
        <template #trigger="{ props }">
          <UiButton v-bind="props" :icon="Pencil" @click="draft = name">Rename</UiButton>
        </template>
        <template #default="{ close }">
          <form class="pad" @submit.prevent="saveName(close)">
            <UiField label="List name">
              <template #default="{ id }"><UiInput :id="id" v-model="draft" select-on-focus /></template>
            </UiField>
            <div class="row end">
              <UiButton size="sm" variant="ghost" @click="close('programmatic')">Cancel</UiButton>
              <UiButton size="sm" variant="primary" type="submit">Save</UiButton>
            </div>
          </form>
        </template>
      </UiPopover>
    </div>
    <p class="muted small">List name: {{ name }}</p>
  </GalleryCard>

  <GalleryCard title="UiMenu">
    <div class="row">
      <UiMenu :items="listItems" aria-label="List actions">
        <template #trigger="{ props }">
          <UiIconButton v-bind="props" :icon="Ellipsis" label="More actions for Website refresh" variant="secondary" />
        </template>
      </UiMenu>
      <UiMenu v-model:open="priorityOpen" :items="priorityItems" aria-label="Priority" :min-width="180">
        <template #trigger="{ props }">
          <UiButton v-bind="props" :icon="Flag" size="sm">Priority: {{ priority }}, change</UiButton>
        </template>
      </UiMenu>
      <UiMenu :items="accountItems" aria-label="Account" placement="bottom-end">
        <template #trigger="{ props }">
          <UiButton v-bind="props" variant="ghost" size="sm" :icon="ChevronDown">Account: Jared Doe</UiButton>
        </template>
      </UiMenu>
    </div>
    <p class="muted small" aria-live="polite">{{ last }}</p>
    <p class="muted small">
      Status {{ STATUSES.find((s) => s.id === status)?.label }} · completed {{ showCompleted ? 'shown' : 'hidden' }} · subtasks
      {{ showSubtasks ? 'shown' : 'hidden' }}
    </p>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

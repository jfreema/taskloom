<script setup lang="ts">
// /_ui state table of the 7.10 controls: buttons, icon buttons, text fields, select, checkbox,
// switch, chip, segmented control, radio cards, colour swatches, inline edit and a labelled field,
// each in every state it has (states.ts). Hover and Focus are forced, so they show without a
// pointer or the keyboard.
import { Ellipsis, Plus, Search } from '@lucide/vue'
import { SWATCHES } from '../../../lib/colors'
import UiButton from '../UiButton.vue'
import UiCheckbox from '../UiCheckbox.vue'
import UiChip from '../UiChip.vue'
import UiColorSwatches from '../UiColorSwatches.vue'
import UiField from '../UiField.vue'
import UiIconButton from '../UiIconButton.vue'
import UiInlineEdit from '../UiInlineEdit.vue'
import UiInput from '../UiInput.vue'
import UiRadioCards from '../UiRadioCards.vue'
import UiSegmented from '../UiSegmented.vue'
import UiSelect from '../UiSelect.vue'
import UiSwitch from '../UiSwitch.vue'
import UiTextarea from '../UiTextarea.vue'
import { ROLE_OPTIONS } from './sample'
import StateRow from './StateRow.vue'
import StateTable from './StateTable.vue'
import type { SampleState } from './states'

const BUTTON: SampleState[] = ['default', 'hover', 'focus', 'disabled', 'loading']
const TEXT: SampleState[] = ['default', 'hover', 'focus', 'disabled', 'invalid', 'empty']
const TOGGLE: SampleState[] = ['default', 'hover', 'focus', 'disabled', 'empty']
const POINTER: SampleState[] = ['default', 'hover', 'focus']

const BUTTON_VARIANTS = [
  { variant: 'primary', label: 'Save' },
  { variant: 'secondary', label: 'Cancel' },
  { variant: 'ghost', label: 'Filter' },
  { variant: 'danger', label: 'Delete' },
  { variant: 'danger-ghost', label: 'Remove' },
  { variant: 'link', label: 'Clear all' },
] as const

const ICON_VARIANTS = ['ghost', 'secondary', 'primary', 'danger-ghost'] as const

const VIEWS = [
  { value: 'list', label: 'List' },
  { value: 'board', label: 'Board' },
]

const PRESET = [{ value: 'simple', label: 'Simple', description: 'To do · Done' }]
const COLORS = SWATCHES.slice(0, 4)

const off = (s: SampleState) => s === 'disabled'
const blank = (s: SampleState) => s === 'empty'
const ignore = () => undefined
</script>

<template>
  <StateTable
    title="States · controls"
    note="Hover and Focus are forced with copies of the components' own :hover and :focus rules. An empty cell is a state the control doesn't have."
  >
    <StateRow v-for="b in BUTTON_VARIANTS" :key="b.variant" :label="`Button · ${b.variant}`" :states="b.variant === 'link' ? POINTER.concat('disabled') : BUTTON">
      <template #default="{ state }">
        <UiButton
          :variant="b.variant"
          :icon="b.variant === 'primary' ? Plus : undefined"
          :disabled="off(state)"
          :loading="state === 'loading'"
          size="sm"
        >
          {{ b.label }}
        </UiButton>
      </template>
    </StateRow>

    <StateRow v-for="v in ICON_VARIANTS" :key="v" :label="`Icon button · ${v}`" :states="['default', 'hover', 'focus', 'disabled']">
      <template #default="{ state }">
        <UiIconButton :icon="v === 'primary' ? Plus : Ellipsis" :label="`More actions (${state})`" :variant="v" :disabled="off(state)" :tooltip="false" />
      </template>
    </StateRow>

    <StateRow label="Input" :states="TEXT">
      <template #default="{ state }">
        <UiInput
          :model-value="blank(state) ? '' : state === 'invalid' ? 'ana@' : 'Website refresh'"
          :placeholder="blank(state) ? 'List name' : undefined"
          :invalid="state === 'invalid'"
          :disabled="off(state)"
          :aria-label="`Input (${state})`"
          size="sm"
          @update:model-value="ignore"
        />
      </template>
    </StateRow>

    <StateRow label="Search input" note="With an icon" :states="TEXT">
      <template #default="{ state }">
        <UiInput
          :model-value="blank(state) ? '' : 'invoice'"
          :icon="Search"
          placeholder="Search this list"
          :invalid="state === 'invalid'"
          :disabled="off(state)"
          :aria-label="`Search (${state})`"
          size="sm"
          type="search"
          @update:model-value="ignore"
        />
      </template>
    </StateRow>

    <StateRow label="Textarea" :states="TEXT">
      <template #default="{ state }">
        <UiTextarea
          :model-value="blank(state) ? '' : 'Draft the outline first.'"
          placeholder="Add a description"
          :rows="2"
          :invalid="state === 'invalid'"
          :disabled="off(state)"
          :aria-label="`Textarea (${state})`"
          @update:model-value="ignore"
        />
      </template>
    </StateRow>

    <StateRow label="Select" :states="TEXT">
      <template #default="{ state }">
        <UiSelect
          :model-value="blank(state) ? null : 'Member'"
          :options="ROLE_OPTIONS"
          placeholder="Choose a role"
          :invalid="state === 'invalid'"
          :disabled="off(state)"
          :aria-label="`Role (${state})`"
          size="sm"
          @update:model-value="ignore"
        />
      </template>
    </StateRow>

    <StateRow label="Field" note="Label, hint or error" :states="TEXT">
      <template #default="{ state }">
        <UiField
          label="Workspace name"
          :hint="state === 'invalid' ? undefined : 'Everyone sees it.'"
          :error="state === 'invalid' ? 'Enter a workspace name.' : undefined"
        >
          <template #default="{ id, describedBy, invalid }">
            <UiInput
              :id="id"
              :model-value="blank(state) || state === 'invalid' ? '' : 'Doe family'"
              :aria-describedby="describedBy"
              :invalid="invalid"
              :disabled="off(state)"
              size="sm"
              @update:model-value="ignore"
            />
          </template>
        </UiField>
      </template>
    </StateRow>

    <StateRow label="Checkbox" note="Empty is unchecked" :states="TOGGLE">
      <template #default="{ state }">
        <UiCheckbox :model-value="!blank(state)" :disabled="off(state)" label="Done" @update:model-value="ignore" />
      </template>
    </StateRow>

    <StateRow label="Switch" note="Empty is off" :states="TOGGLE">
      <template #default="{ state }">
        <UiSwitch :model-value="!blank(state)" :disabled="off(state)" label="Create more" @update:model-value="ignore" />
      </template>
    </StateRow>

    <StateRow label="Inline edit" note="Empty shows the placeholder" :states="['default', 'hover', 'focus', 'disabled', 'empty']">
      <template #default="{ state }">
        <UiInlineEdit
          :model-value="blank(state) ? '' : 'Q4 launch'"
          placeholder="List name"
          :aria-label="`List name (${state})`"
          :disabled="off(state)"
        />
      </template>
    </StateRow>

    <StateRow label="Chip" :states="POINTER">
      <template #default>
        <UiChip label="Assigned to me" />
      </template>
    </StateRow>

    <StateRow label="Segmented" :states="POINTER">
      <template #default="{ state }">
        <UiSegmented model-value="board" :options="VIEWS" :aria-label="`View (${state})`" size="sm" @update:model-value="ignore" />
      </template>
    </StateRow>

    <StateRow label="Radio cards" note="Empty has no choice" :states="['default', 'hover', 'focus', 'empty']">
      <template #default="{ state }">
        <UiRadioCards
          :model-value="blank(state) ? '' : 'simple'"
          :options="PRESET"
          :name="`matrix-preset-${state}`"
          :columns="1"
          :aria-label="`Statuses (${state})`"
          @update:model-value="ignore"
        />
      </template>
    </StateRow>

    <StateRow label="Colour swatches" :states="POINTER">
      <template #default="{ state }">
        <UiColorSwatches :model-value="COLORS[1].hex" :colors="COLORS" :size="20" :aria-label="`Colour (${state})`" @update:model-value="ignore" />
      </template>
    </StateRow>
  </StateTable>
</template>

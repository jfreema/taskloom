<script setup lang="ts">
// List picker (7.11): a single-select, searchable listbox of the current workspace's lists, one
// group per space in sidebar order, each option with its SpaceTile. The space name is a search
// keyword, so "work" finds every list in Work. `excludeListId` leaves out the tasks' own list.
// Triggers, named "List: <name>, change" or "List: none, add":
// - 'chip': a round chip "[W] Work / Admin ⌄" (quick create);
// - 'field': a full-width select ("Choose a list" when empty), for dialogs and forms.
import { computed } from 'vue'
import { ChevronDown } from '@lucide/vue'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import type { ListboxOption } from '../ui/listbox'
import UiIcon from '../ui/UiIcon.vue'
import UiListbox from '../ui/UiListbox.vue'
import UiPopover from '../ui/UiPopover.vue'
import FieldTrigger from './FieldTrigger.vue'
import SpaceTile from './SpaceTile.vue'
import { triggerLabel } from './fieldLabel'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: Uuid | null
    variant?: 'chip' | 'field'
    excludeListId?: Uuid | null
    placeholder?: string
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
  }>(),
  { variant: 'field', excludeListId: null, placeholder: 'Choose a list', readonly: false, anchor: null, hideTrigger: false },
)

const emit = defineEmits<{ 'update:modelValue': [listId: Uuid | null] }>()

const workspaces = useWorkspaces()

const chosen = computed(() => (props.modelValue ? workspaces.findList(props.modelValue) : null))
const label = computed(() => triggerLabel('List', chosen.value?.list.name ?? null))
const tip = computed(() => (chosen.value ? `${chosen.value.space.name} / ${chosen.value.list.name}` : undefined))

const spaceByList = computed(() => new Map(workspaces.allLists.map((x) => [x.list.id, x.space])))

function options(): ListboxOption[] {
  return workspaces.allLists
    .filter((x) => x.list.id !== props.excludeListId)
    .map((x) => ({ value: x.list.id, label: x.list.name, group: x.space.name, keywords: [x.space.name] }))
}

function update(next: string | string[] | null): void {
  const id = typeof next === 'string' ? next : null
  open.value = false
  if (id && id !== props.modelValue) emit('update:modelValue', id)
}
</script>

<template>
  <span v-if="readonly" v-bind="$attrs" class="static" :class="`static--${variant}`">
    <template v-if="chosen">
      <SpaceTile :space="chosen.space" :size="16" />
      <span class="path truncate">{{ chosen.space.name }} / {{ chosen.list.name }}</span>
    </template>
    <span v-else class="muted">{{ placeholder }}</span>
  </span>
  <UiPopover v-else v-model:open="open" role="listbox" :anchor="anchor" :width="variant === 'chip' ? 280 : undefined" :match-width="variant === 'field'">
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="variant === 'chip' ? 'chip' : 'select'"
        :label="label"
        :empty="!chosen"
        :tooltip="variant === 'chip' ? tip : undefined"
      >
        <template v-if="chosen">
          <SpaceTile :space="chosen.space" :size="16" />
          <span class="path truncate">
            <span class="space-name">{{ chosen.space.name }} /</span>
            {{ chosen.list.name }}
          </span>
        </template>
        <span v-else class="path truncate">{{ placeholder }}</span>
        <UiIcon class="chevron" :icon="ChevronDown" :size="variant === 'chip' ? 14 : 16" />
      </FieldTrigger>
    </template>
    <template #default>
      <UiListbox
        :options="options"
        :model-value="modelValue"
        :searchable="true"
        search-placeholder="Search lists"
        aria-label="Lists"
        @update:model-value="update"
      >
        <template #option="{ option }">
          <SpaceTile v-if="spaceByList.get(option.value)" :space="spaceByList.get(option.value)!" :size="16" />
          <span class="option-label">{{ option.label }}</span>
        </template>
        <template #empty="{ query }">
          <span v-if="query">No lists match “{{ query }}”.</span>
          <span v-else-if="excludeListId">There is no other list to move to.</span>
          <span v-else>No lists yet.</span>
        </template>
      </UiListbox>
    </template>
  </UiPopover>
</template>

<style scoped>
.static {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  min-height: var(--control-sm);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.static--field {
  min-height: var(--control-md);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.muted {
  color: var(--text-tertiary);
}

.path {
  flex: 1 1 auto;
  min-width: 0;
}

.field-trigger--chip .path {
  flex: 0 1 auto;
  max-width: 220px;
}

.space-name {
  color: var(--text-secondary);
}

.field-trigger.is-empty .path {
  color: var(--text-tertiary);
}

.chevron {
  flex-shrink: 0;
  margin-left: auto;
  color: var(--text-tertiary);
}

.field-trigger--chip .chevron {
  margin-left: 0;
}

.option-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>

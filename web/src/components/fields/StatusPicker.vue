<script setup lang="ts">
// Status picker (7.11): a single-select listbox of the list's statuses grouped "Not started",
// "Active" and "Done", each with its StatusIcon. Number keys 1–9 pick the n-th status while it is
// open. Members see an "Edit statuses" footer. The trigger is the icon (List rows), an interactive
// pill (the task panel) or a round chip (quick create), named "Status: <name>, change".
// Shared field props: `readonly` renders the value with no button; `hideTrigger` + `anchor` +
// v-model:open let a view open it from a shortcut against a row or card.
import { computed } from 'vue'
import { ChevronDown, ListChecks } from '@lucide/vue'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import { STATUS_CATEGORY_LABEL, type Status, type Uuid } from '../../types'
import type { ListboxOption } from '../ui/listbox'
import UiIcon from '../ui/UiIcon.vue'
import UiKbd from '../ui/UiKbd.vue'
import UiListbox from '../ui/UiListbox.vue'
import UiPopover from '../ui/UiPopover.vue'
import FieldTrigger from './FieldTrigger.vue'
import StatusIcon from './StatusIcon.vue'
import StatusPill from './StatusPill.vue'
import { triggerLabel } from './fieldLabel'
import { digitOf, useKeysWhileOpen } from './pickerKeys'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: Uuid
    listId: Uuid
    variant?: 'icon' | 'pill' | 'chip'
    size?: 20 | 24 | 28
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
  }>(),
  { variant: 'pill', size: 24, readonly: false, anchor: null, hideTrigger: false },
)

const emit = defineEmits<{ 'update:modelValue': [statusId: Uuid] }>()

const workspaces = useWorkspaces()
const ui = useUi()

const statuses = computed(() => workspaces.statusesFor(props.listId))
const byId = computed(() => new Map(statuses.value.map((s) => [s.id, s])))

/** Shown while the status isn't known (a list still loading): an empty grey ring. */
const placeholder = computed<Status>(() => ({
  id: '',
  listId: props.listId,
  name: 'No status',
  color: 'var(--border-control)',
  category: 'NotStarted',
  position: 0,
}))
const current = computed<Status | null>(() => workspaces.statusById(props.modelValue) ?? null)
const shown = computed(() => current.value ?? placeholder.value)

const canEdit = computed(() => workspaces.canEdit(workspaces.findList(props.listId)?.workspace.id))
const label = computed(() => triggerLabel('Status', current.value?.name))
const iconSize = computed(() => (props.size === 28 ? 18 : props.size === 20 ? 14 : 16))
const tip = computed(() => (props.variant === 'icon' ? { text: shown.value.name, kbd: 'S' } : undefined))

function options(): ListboxOption[] {
  return statuses.value.map((s) => ({ value: s.id, label: s.name, group: STATUS_CATEGORY_LABEL[s.category] }))
}

function digitFor(id: string): string | null {
  const i = statuses.value.findIndex((s) => s.id === id)
  return i >= 0 && i < 9 ? String(i + 1) : null
}

function pick(id: string): void {
  open.value = false
  if (id && id !== props.modelValue) emit('update:modelValue', id)
}

useKeysWhileOpen(open, ['1', '2', '3', '4', '5', '6', '7', '8', '9'], (e) => {
  const n = digitOf(e)
  const s = n ? statuses.value[n - 1] : undefined
  if (!s) return false
  pick(s.id)
})

function editStatuses(): void {
  open.value = false
  ui.openStatusEditor(props.listId)
}
</script>

<template>
  <template v-if="readonly">
    <StatusPill v-if="variant !== 'icon'" v-bind="$attrs" :status="shown" :size="size" />
    <span v-else v-bind="$attrs" class="static-icon">
      <StatusIcon :status="shown" :size="iconSize" :decorative="false" />
    </span>
  </template>
  <UiPopover v-else v-model:open="open" role="listbox" :anchor="anchor" :width="240">
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="variant === 'icon' ? 'icon' : variant === 'chip' ? 'chip' : 'round'"
        :label="label"
        :tooltip="tip"
        :class="{ 'is-28': variant === 'icon' && size === 28 }"
      >
        <StatusIcon v-if="variant === 'icon'" :status="shown" :size="iconSize" />
        <template v-else-if="variant === 'chip'">
          <StatusIcon :status="shown" :size="14" />
          <span class="chip-text">{{ shown.name }}</span>
          <UiIcon class="chip-chevron" :icon="ChevronDown" :size="14" />
        </template>
        <StatusPill v-else :status="shown" :size="size" interactive />
      </FieldTrigger>
    </template>
    <template #default>
      <UiListbox :options="options" :model-value="modelValue" aria-label="Status" :searchable="false" @select="pick">
        <template #option="{ option }">
          <StatusIcon v-if="byId.get(option.value)" class="option-icon" :status="byId.get(option.value)!" :size="16" />
          <span class="option-label">{{ option.label }}</span>
          <span v-if="digitFor(option.value)" class="option-key" aria-hidden="true">
            <UiKbd :keys="digitFor(option.value)!" />
          </span>
        </template>
        <template v-if="canEdit" #footer>
          <button type="button" class="footer-action focus-inset" @click="editStatuses">
            <UiIcon :icon="ListChecks" :size="16" />
            <span>Edit statuses</span>
          </button>
        </template>
      </UiListbox>
    </template>
  </UiPopover>
</template>

<style scoped>
.static-icon {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: var(--control-xs);
  height: var(--control-xs);
}

.is-28 {
  width: var(--control-sm);
  height: var(--control-sm);
}

.option-icon {
  flex-shrink: 0;
}

.chip-text {
  min-width: 0;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
}

.chip-chevron {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.option-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.option-key {
  flex-shrink: 0;
  display: inline-flex;
}

.footer-action {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-height: var(--row-menu);
  padding: var(--space-1-5) var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-align: left;
  cursor: pointer;
}

.footer-action:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}
</style>

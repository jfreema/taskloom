<script setup lang="ts">
// Priority picker (7.11): a menu of Urgent (1), High (2), Normal (3), Low (4), a separator and
// "Clear priority" (0); the digits pick while the menu is open (9.5, layer scope).
// Triggers, named "Priority: <label>, change" or "Priority: none, add":
// - 'icon': the flag alone (narrow List cells), a muted outline flag when empty;
// - 'label': the flag and its label (List cells, the task panel), "Add" when empty;
// - 'chip': a round chip reading "Priority" when empty (quick create).
// Shared field props: readonly, v-model:open, anchor, hideTrigger (7.11).
import { computed } from 'vue'
import { PRIORITY_LABEL, type Priority } from '../../types'
import type { MenuItem } from '../ui/menu'
import FieldTrigger from './FieldTrigger.vue'
import LazyMenu from './LazyMenu.vue'
import PriorityFlag from './PriorityFlag.vue'
import { triggerLabel } from './fieldLabel'
import { digitOf, useKeysWhileOpen } from './pickerKeys'
import { PRIORITY_GLYPHS } from './priorityGlyphs'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: Priority
    variant?: 'icon' | 'label' | 'chip'
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
    /** Mounts the popover only once used (List rows the pointer or focus hasn't reached yet). */
    lazy?: boolean
  }>(),
  { variant: 'label', readonly: false, anchor: null, hideTrigger: false, lazy: false },
)

const emit = defineEmits<{ 'update:modelValue': [priority: Priority] }>()

const BY_DIGIT: Record<number, Priority> = { 1: 'Urgent', 2: 'High', 3: 'Normal', 4: 'Low', 0: 'None' }
const CHOICES: Priority[] = ['Urgent', 'High', 'Normal', 'Low']

const none = computed(() => props.modelValue === 'None')
const label = computed(() => triggerLabel('Priority', none.value ? null : PRIORITY_LABEL[props.modelValue]))
const tip = computed(() =>
  props.variant === 'icon' ? { text: none.value ? 'Priority' : PRIORITY_LABEL[props.modelValue], kbd: 'P' } : undefined,
)

function pick(p: Priority): void {
  open.value = false
  if (p !== props.modelValue) emit('update:modelValue', p)
}

function items(): MenuItem[] {
  return [
    ...CHOICES.map((p, i) => ({
      id: `priority-${p}`,
      label: PRIORITY_LABEL[p],
      icon: PRIORITY_GLYPHS[p],
      kbd: String(i + 1),
      onSelect: () => pick(p),
    })),
    { kind: 'separator', id: 'priority-sep' },
    { id: 'priority-None', label: 'Clear priority', kbd: '0', onSelect: () => pick('None') },
  ]
}

useKeysWhileOpen(open, ['1', '2', '3', '4', '0'], (e) => {
  const n = digitOf(e)
  const p = n === null ? undefined : BY_DIGIT[n]
  if (!p) return false
  pick(p)
})
</script>

<template>
  <span v-if="readonly" v-bind="$attrs" class="static" :class="`static--${variant}`">
    <span v-if="none && variant !== 'icon'" class="empty-text">None</span>
    <PriorityFlag v-else :priority="modelValue" :show-label="variant !== 'icon'" />
  </span>
  <LazyMenu v-else v-model:open="open" :lazy="lazy" :items="items" :anchor="anchor" aria-label="Priority" :min-width="200">
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="variant === 'icon' ? 'icon' : variant === 'chip' ? 'chip' : 'cell'"
        :label="label"
        :empty="none"
        :tooltip="tip"
      >
        <PriorityFlag :priority="modelValue" :show-label="variant !== 'icon'" show-empty />
        <span v-if="none && variant === 'label'" class="empty-text">Add</span>
        <span v-else-if="none && variant === 'chip'">Priority</span>
      </FieldTrigger>
    </template>
  </LazyMenu>
</template>

<style scoped>
.static {
  display: inline-flex;
  align-items: center;
  min-width: 0;
}

.static--icon {
  justify-content: center;
  width: var(--control-xs);
  height: var(--control-xs);
}

.static--label,
.static--chip {
  min-height: var(--control-sm);
  padding: 0 var(--space-1-5);
}

.empty-text {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}
</style>

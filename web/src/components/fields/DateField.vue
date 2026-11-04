<script setup lang="ts">
// Date field (7.11): a trigger and a popover holding UiDatePicker. It keeps start ≤ due: a due
// date before the start also moves the start (`update:otherDate`, sent first), and a start after
// the due date also moves the due date. The other date is ringed in the picker (`highlight`).
// Triggers, named "Due date: <text>, change" or "Due date: none, add" ("Start date: …" for kind
// start): 'cell' (List cells; a muted calendar when empty), 'field' (property rows; "Add" when
// empty), 'chip' (quick create, the panel's dates; the field name when empty), 'icon' (a calendar
// alone). A due date that is overdue (and not done) reads in --danger-text, today in --warning-text.
import { computed } from 'vue'
import { Calendar } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { formatDateLong, formatDue } from '../../lib/dates'
import type { IsoDate } from '../../types'
import UiDatePicker from '../ui/UiDatePicker.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiPopover from '../ui/UiPopover.vue'
import DueDate from './DueDate.vue'
import FieldTrigger from './FieldTrigger.vue'
import { triggerLabel } from './fieldLabel'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: IsoDate | null
    kind: 'due' | 'start'
    /** The task's other date: the start date for kind 'due', the due date for kind 'start'. */
    otherDate?: IsoDate | null
    variant?: 'cell' | 'field' | 'chip' | 'icon'
    done?: boolean
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
  }>(),
  { otherDate: null, variant: 'cell', done: false, readonly: false, anchor: null, hideTrigger: false },
)

const emit = defineEmits<{
  'update:modelValue': [date: IsoDate | null]
  'update:otherDate': [date: IsoDate | null]
}>()

const today = useToday()

const fieldName = computed(() => (props.kind === 'due' ? 'Due date' : 'Start date'))
const text = computed(() => (props.modelValue ? formatDue(props.modelValue, today.value) : null))
const label = computed(() => triggerLabel(fieldName.value, text.value))
const overdue = computed(() => props.kind === 'due' && !props.done && !!props.modelValue && props.modelValue < today.value)
const triggerVariant = computed(() =>
  props.variant === 'field' ? 'ghost' : props.variant === 'chip' ? 'chip' : props.variant === 'icon' ? 'icon' : 'cell',
)
const tip = computed(() => {
  if (props.variant === 'icon') {
    return { text: text.value ? `${fieldName.value}: ${text.value}` : fieldName.value, kbd: props.kind === 'due' ? 'D' : undefined }
  }
  return props.modelValue ? formatDateLong(props.modelValue) : undefined
})

function pick(v: IsoDate | null): void {
  open.value = false
  if (v === props.modelValue) return
  const other = props.otherDate
  if (v && other && ((props.kind === 'due' && v < other) || (props.kind === 'start' && v > other))) {
    emit('update:otherDate', v)
  }
  emit('update:modelValue', v)
}
</script>

<template>
  <span v-if="readonly" v-bind="$attrs" class="static" :class="`static--${variant}`">
    <template v-if="modelValue">
      <DueDate v-if="kind === 'due'" :date="modelValue" :done="done" :show-icon="variant !== 'cell'" />
      <span v-else class="plain"><UiIcon v-if="variant !== 'cell'" :icon="Calendar" :size="14" />{{ text }}</span>
    </template>
    <span v-else-if="variant === 'field'" class="none">None</span>
  </span>
  <UiPopover
    v-else
    v-model:open="open"
    :anchor="anchor"
    :aria-label="fieldName"
    width="auto"
  >
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="triggerVariant"
        :label="label"
        :empty="!modelValue"
        :tooltip="tip"
        :class="{ 'is-overdue': overdue }"
      >
        <UiIcon v-if="variant === 'icon'" :icon="Calendar" :size="16" />
        <template v-else-if="modelValue">
          <DueDate v-if="kind === 'due'" :date="modelValue" :done="done" :show-icon="variant !== 'cell'" />
          <span v-else class="plain"><UiIcon v-if="variant !== 'cell'" class="lead" :icon="Calendar" :size="14" />{{ text }}</span>
        </template>
        <UiIcon v-else-if="variant === 'cell'" :icon="Calendar" :size="16" />
        <template v-else>
          <UiIcon :icon="Calendar" :size="variant === 'chip' ? 16 : 14" />
          <span>{{ variant === 'chip' ? fieldName : 'Add' }}</span>
        </template>
      </FieldTrigger>
    </template>
    <template #default>
      <UiDatePicker
        :model-value="modelValue"
        :highlight="otherDate"
        :clear-label="kind === 'due' ? 'No due date' : 'No start date'"
        @update:model-value="pick"
        @cancel="open = false"
      />
    </template>
  </UiPopover>
</template>

<style scoped>
.static {
  display: inline-flex;
  align-items: center;
  min-width: 0;
  min-height: var(--control-sm);
  padding: 0 var(--space-1-5);
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.static--field {
  min-height: var(--control-md);
  padding: var(--space-1) var(--space-2);
}

.static--icon {
  display: none;
}

.plain {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  color: var(--text-secondary);
  white-space: nowrap;
}

.plain .lead {
  color: var(--text-tertiary);
}

.none {
  color: var(--text-tertiary);
}

.field-trigger--icon.is-overdue {
  color: var(--danger-text);
}
</style>

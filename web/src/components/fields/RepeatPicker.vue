<script setup lang="ts">
// Repeat picker (Features 1): a menu of one-click rules worded for the task's due date (else today),
// "Custom…" (RepeatDialog) and "Don't repeat". A rule that isn't one of the presets shows first, checked.
// The trigger is a ghost row (the task panel), named "Repeat: <rule>, change" or "Repeat: none, add".
// The app never works out dates: the server sets the next due date, and the first one when a rule
// on schedule goes on a task without a due date.
import { computed, ref } from 'vue'
import { Repeat } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { describeRepeat, repeatPresets, sameRule } from '../../lib/repeat'
import type { IsoDate, RepeatRequest, RepeatRule } from '../../types'
import type { MenuItem } from '../ui/menu'
import UiIcon from '../ui/UiIcon.vue'
import FieldTrigger from './FieldTrigger.vue'
import LazyMenu from './LazyMenu.vue'
import RepeatDialog from './RepeatDialog.vue'
import { triggerLabel } from './fieldLabel'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: RepeatRule | null
    /** The task's due date: the presets and the custom dialog are worded for it. */
    dueDate?: IsoDate | null
    readonly?: boolean
  }>(),
  { dueDate: null, readonly: false },
)

const emit = defineEmits<{ 'update:modelValue': [rule: RepeatRequest | null] }>()

const today = useToday()
const custom = ref(false)

const anchor = computed(() => props.dueDate ?? today.value)
const text = computed(() => (props.modelValue ? describeRepeat(props.modelValue, today.value) : null))
const label = computed(() => triggerLabel('Repeat', text.value))

function pick(rule: RepeatRequest | null): void {
  open.value = false
  if (!sameRule(props.modelValue, rule)) emit('update:modelValue', rule)
}

function items(): MenuItem[] {
  const presets = repeatPresets(anchor.value, today.value)
  const current = props.modelValue
  const preset = presets.some((p) => sameRule(current, p.rule))
  return [
    ...(!current || preset
      ? []
      : [
          { kind: 'radio' as const, id: 'repeat-current', label: describeRepeat(current, today.value), checked: true, onSelect: () => {} },
          { kind: 'separator' as const, id: 'repeat-sep-current' },
        ]),
    ...presets.map((p) => ({
      kind: 'radio' as const,
      id: `repeat-${p.key}`,
      label: p.label,
      checked: sameRule(current, p.rule),
      onSelect: () => pick(p.rule),
    })),
    { kind: 'separator', id: 'repeat-sep' },
    { id: 'repeat-custom', label: 'Custom…', onSelect: () => (custom.value = true) },
    ...(current ? [{ id: 'repeat-none', label: "Don't repeat", onSelect: () => pick(null) }] : []),
  ]
}
</script>

<template>
  <span v-if="readonly" v-bind="$attrs" class="static">
    <template v-if="text"><UiIcon class="lead" :icon="Repeat" :size="14" />{{ text }}</template>
    <span v-else class="none">None</span>
  </span>
  <template v-else>
    <LazyMenu v-model:open="open" :items="items" aria-label="Repeat" :min-width="240">
      <template #trigger="{ props: t }">
        <FieldTrigger v-bind="{ ...$attrs, ...t }" variant="ghost" :label="label" :empty="!modelValue">
          <span class="value">
            <UiIcon class="lead" :icon="Repeat" :size="14" />
            <span>{{ text ?? 'Add' }}</span>
          </span>
        </FieldTrigger>
      </template>
    </LazyMenu>
    <RepeatDialog v-model:open="custom" :rule="modelValue" :anchor="anchor" @save="pick" />
  </template>
</template>

<style scoped>
.static,
.value {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
}

.static {
  min-height: var(--control-md);
  padding: var(--space-1) var(--space-2);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.lead {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.none {
  color: var(--text-tertiary);
}
</style>

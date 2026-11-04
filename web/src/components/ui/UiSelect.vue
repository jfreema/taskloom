<script setup lang="ts">
// Select (spec 7.10): a trigger button styled like UiInput (the value and a ChevronDown) that opens
// a UiPopover holding a single-select UiListbox. The trigger has aria-haspopup="listbox"; picking
// closes the popover and focus returns to the trigger. Attributes (aria-describedby from UiField,
// class, data-*) go to the trigger. The current value is added to its description, because a
// label (aria-label or <label for>) replaces the button's text as its name.
import { computed, ref, useAttrs, useId } from 'vue'
import { ChevronDown } from '@lucide/vue'
import UiIcon from './UiIcon.vue'
import UiListbox from './UiListbox.vue'
import UiPopover from './UiPopover.vue'
import type { ListboxOption } from './listbox'

defineOptions({ inheritAttrs: false })

const model = defineModel<string | null>({ required: true })

const props = withDefaults(
  defineProps<{
    options: ListboxOption[]
    placeholder?: string
    size?: 'sm' | 'md'
    disabled?: boolean
    invalid?: boolean
    id?: string
    ariaLabel?: string
  }>(),
  { placeholder: 'Choose…', size: 'md', disabled: false, invalid: false, id: undefined, ariaLabel: undefined },
)

const attrs = useAttrs()
const open = ref(false)
const valueId = `${useId()}-value`

const selected = computed(() => props.options.find((o) => o.value === model.value) ?? null)

const describedBy = computed(() => {
  const own = typeof attrs['aria-describedby'] === 'string' ? attrs['aria-describedby'] : ''
  const labelled = !!props.ariaLabel || !!props.id
  return [labelled ? valueId : '', own].filter(Boolean).join(' ') || undefined
})

/** The listbox is named like the select: its aria-label, else the text of its <label for>. */
const listLabel = computed(() => {
  if (props.ariaLabel) return props.ariaLabel
  if (!open.value || !props.id) return 'Options'
  return document.querySelector(`label[for="${CSS.escape(props.id)}"]`)?.textContent?.trim() || 'Options'
})

function pick(value: string | string[] | null, close: () => void): void {
  model.value = typeof value === 'string' ? value : null
  close()
}
</script>

<template>
  <UiPopover v-model:open="open" role="listbox" match-width :width="280">
    <template #trigger="{ props: trigger }">
      <button
        v-bind="{ ...attrs, ...trigger }"
        :id="id"
        type="button"
        class="ui-select"
        :class="[`ui-select--${size}`, { 'is-invalid': invalid, 'is-empty': !selected }]"
        :disabled="disabled"
        :aria-label="ariaLabel"
        :aria-describedby="describedBy"
        :aria-invalid="invalid ? 'true' : undefined"
      >
        <UiIcon v-if="selected?.icon" class="lead" :icon="selected.icon" :size="16" />
        <span :id="valueId" class="value">{{ selected?.label ?? placeholder }}</span>
        <UiIcon class="chevron" :icon="ChevronDown" :size="16" />
      </button>
    </template>
    <template #default="{ close }">
      <UiListbox
        :options="options"
        :model-value="model"
        :aria-label="listLabel"
        @update:model-value="pick($event, close)"
      />
    </template>
  </UiPopover>
</template>

<style scoped>
.ui-select {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  max-width: 100%;
  height: var(--control-md);
  padding: 0 var(--space-2) 0 10px;
  background: var(--bg-surface);
  border: 1px solid var(--border-default);
  border-bottom-color: var(--border-control);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-align: left;
  cursor: pointer;
  transition:
    border-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.ui-select--sm {
  height: var(--control-sm);
  padding-left: var(--space-2);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.ui-select:hover:not(:disabled) {
  border-color: color-mix(in srgb, var(--border-control) 45%, var(--border-default));
  border-bottom-color: var(--border-control);
}

.ui-select:focus-visible,
.ui-select[aria-expanded="true"] {
  border-color: var(--focus-ring);
  box-shadow: 0 0 0 1px var(--focus-ring);
  outline: none;
}

.ui-select.is-invalid {
  border-color: var(--danger);
}

.ui-select.is-invalid:focus-visible {
  box-shadow: 0 0 0 1px var(--danger);
}

.ui-select:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.lead {
  color: var(--text-secondary);
}

.value {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.is-empty .value {
  color: var(--text-tertiary);
}

.chevron {
  color: var(--text-tertiary);
}
</style>

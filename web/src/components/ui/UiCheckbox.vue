<script setup lang="ts">
// Square checkbox: it selects (spec 7.10). A native checkbox is visually hidden over the box, so
// forms, labels and keyboard work natively. `change` carries the click event, with shiftKey, for
// range selection.
import { nextTick, onMounted, ref, watch } from 'vue'
import { Check } from '@lucide/vue'
import UiIcon from './UiIcon.vue'

const model = defineModel<boolean>({ required: true })

const props = withDefaults(
  defineProps<{
    indeterminate?: boolean
    disabled?: boolean
    /** Visible label text. */
    label?: string
    /** Accessible name when there is no visible label. */
    ariaLabel?: string
    size?: 14 | 16
  }>(),
  { indeterminate: false, disabled: false, label: undefined, ariaLabel: undefined, size: 16 },
)

const emit = defineEmits<{ change: [value: boolean, e: MouseEvent | KeyboardEvent] }>()

const input = ref<HTMLInputElement | null>(null)

function syncIndeterminate(): void {
  if (input.value) input.value.indeterminate = props.indeterminate
}

watch(() => props.indeterminate, syncIndeterminate)
onMounted(syncIndeterminate)

function onClick(e: MouseEvent): void {
  const value = (e.currentTarget as HTMLInputElement).checked
  model.value = value
  emit('change', value, e)
  // The model decides: if the parent refused the change, the native state follows it back.
  void nextTick(() => {
    if (input.value) input.value.checked = model.value
    syncIndeterminate()
  })
}

function onMousedown(e: MouseEvent): void {
  // Shift-click selects a range of rows, not a range of page text.
  if (e.shiftKey) e.preventDefault()
}

defineExpose({ focus: () => input.value?.focus(), el: input })
</script>

<template>
  <component
    :is="label ? 'label' : 'span'"
    class="ui-checkbox"
    :class="[`ui-checkbox--${size}`, { 'is-disabled': disabled }]"
  >
    <span class="control">
      <input
        ref="input"
        type="checkbox"
        class="native"
        :checked="model"
        :disabled="disabled"
        :aria-label="label ? undefined : ariaLabel"
        @click="onClick"
        @mousedown="onMousedown"
      />
      <span class="box" :class="{ 'is-on': model || indeterminate }" aria-hidden="true">
        <span v-if="indeterminate" class="bar" />
        <UiIcon v-else-if="model" :icon="Check" :size="12" :stroke-width="3" />
      </span>
    </span>
    <span v-if="label" class="text">{{ label }}</span>
  </component>
</template>

<style scoped>
.ui-checkbox {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  vertical-align: middle;
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

label.ui-checkbox {
  cursor: pointer;
}

.control {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
  width: 16px;
  height: 16px;
}

.ui-checkbox--14 .control {
  width: 14px;
  height: 14px;
}

.native {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  opacity: 0;
  cursor: pointer;
  z-index: 1;
}

.native:disabled {
  cursor: not-allowed;
}

.box {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
  border: 1.5px solid var(--border-control);
  border-radius: var(--radius-xs);
  background: var(--bg-surface);
  color: var(--accent-fg);
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out);
}

.native:hover + .box {
  border-color: var(--text-secondary);
}

.box.is-on,
.native:hover + .box.is-on {
  background: var(--accent);
  border-color: var(--accent);
}

.ui-checkbox--14 .box :deep(svg) {
  width: 10px;
  height: 10px;
}

.bar {
  width: 8px;
  height: 2px;
  border-radius: 1px;
  background: currentColor;
}

.ui-checkbox--14 .bar {
  width: 6px;
}

.native:focus-visible + .box {
  outline: 2px solid var(--focus-ring);
  outline-offset: 2px;
}

.is-disabled {
  opacity: 0.45;
}

.text {
  min-width: 0;
}

/* A 44px target for fingers, centred on the box. */
@media (pointer: coarse) {
  .native {
    inset: -14px;
    width: auto;
    height: auto;
  }
}
</style>

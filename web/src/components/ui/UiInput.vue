<script setup lang="ts">
// Text input (spec 7.10). Attributes other than class and style (aria-*, name, @paste, @keydown…)
// go to the <input>; class and style stay on the frame.
import { computed, onMounted, ref, useAttrs, type Component, type InputHTMLAttributes } from 'vue'
import { X } from '@lucide/vue'
import UiIcon from './UiIcon.vue'
import UiIconButton from './UiIconButton.vue'

defineOptions({ inheritAttrs: false })

const model = defineModel<string>({ required: true })

const props = withDefaults(
  defineProps<{
    type?: 'text' | 'email' | 'search' | 'url' | 'password'
    size?: 'sm' | 'md' | 'lg'
    placeholder?: string
    /** Left icon. */
    icon?: Component
    clearable?: boolean
    invalid?: boolean
    disabled?: boolean
    readonly?: boolean
    autofocus?: boolean
    selectOnFocus?: boolean
    maxlength?: number
    id?: string
    autocomplete?: string
    inputmode?: string
  }>(),
  {
    type: 'text',
    size: 'md',
    placeholder: undefined,
    icon: undefined,
    clearable: false,
    invalid: false,
    disabled: false,
    readonly: false,
    autofocus: false,
    selectOnFocus: false,
    maxlength: undefined,
    id: undefined,
    autocomplete: undefined,
    inputmode: undefined,
  },
)

const emit = defineEmits<{
  enter: [e: KeyboardEvent]
  escape: [e: KeyboardEvent]
  focus: [e: FocusEvent]
  blur: [e: FocusEvent]
}>()

const attrs = useAttrs()
const el = ref<HTMLInputElement | null>(null)

const frameAttrs = computed(() => ({ class: attrs.class, style: attrs.style }))
const inputAttrs = computed(() => {
  const rest: Record<string, unknown> = { ...attrs }
  delete rest.class
  delete rest.style
  return rest
})
const inputMode = computed(() => props.inputmode as InputHTMLAttributes['inputmode'])
const showClear = computed(() => props.clearable && !props.disabled && !props.readonly && model.value.length > 0)

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter') emit('enter', e)
  else if (e.key === 'Escape') emit('escape', e)
}

function onFocus(e: FocusEvent): void {
  if (props.selectOnFocus) el.value?.select()
  emit('focus', e)
}

function clear(): void {
  model.value = ''
  el.value?.focus()
}

function focus(): void {
  el.value?.focus()
}

function select(): void {
  el.value?.select()
}

onMounted(() => {
  if (props.autofocus) el.value?.focus({ preventScroll: true })
})

defineExpose({ focus, select, el })
</script>

<template>
  <div
    class="ui-input"
    :class="[`ui-input--${size}`, { 'is-invalid': invalid, 'is-disabled': disabled, 'is-readonly': readonly }]"
    v-bind="frameAttrs"
  >
    <span v-if="$slots.prefix" class="affix"><slot name="prefix" /></span>
    <UiIcon v-else-if="icon" class="lead-icon" :icon="icon" :size="size === 'lg' ? 20 : 16" />
    <input
      :id="id"
      ref="el"
      v-model="model"
      class="control"
      :type="type"
      :placeholder="placeholder"
      :disabled="disabled"
      :readonly="readonly"
      :maxlength="maxlength"
      :autocomplete="autocomplete"
      :inputmode="inputMode"
      :aria-invalid="invalid ? 'true' : undefined"
      v-bind="inputAttrs"
      @keydown="onKeydown"
      @focus="onFocus"
      @blur="emit('blur', $event)"
    />
    <UiIconButton v-if="showClear" class="clear" :icon="X" label="Clear" size="xs" @click="clear" />
    <span v-if="$slots.suffix" class="affix"><slot name="suffix" /></span>
  </div>
</template>

<style scoped>
.ui-input {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  height: var(--control-md);
  padding: 0 10px;
  background: var(--bg-surface);
  border: 1px solid var(--border-default);
  border-bottom-color: var(--border-control);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  transition:
    border-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.ui-input--sm {
  height: var(--control-sm);
  padding: 0 var(--space-2);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.ui-input--lg {
  height: var(--control-lg);
  padding: 0 var(--space-3);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
}

.ui-input:hover:not(.is-disabled):not(:focus-within) {
  border-color: color-mix(in srgb, var(--border-control) 45%, var(--border-default));
  border-bottom-color: var(--border-control);
}

.ui-input:focus-within {
  border-color: var(--focus-ring);
  box-shadow: 0 0 0 1px var(--focus-ring);
}

.ui-input.is-invalid {
  border-color: var(--danger);
}

.ui-input.is-invalid:focus-within {
  box-shadow: 0 0 0 1px var(--danger);
}

.ui-input.is-disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.ui-input.is-readonly {
  background: var(--bg-subtle);
}

.control {
  flex: 1;
  min-width: 0;
  height: 100%;
  padding: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: inherit;
  font: inherit;
  text-overflow: ellipsis;
}

.control:focus-visible {
  outline: none;
}

.control::placeholder {
  color: var(--text-tertiary);
  opacity: 1;
}

.control:disabled {
  cursor: not-allowed;
}

.control[type="search"]::-webkit-search-cancel-button,
.control[type="search"]::-webkit-search-decoration {
  -webkit-appearance: none;
  appearance: none;
}

.lead-icon {
  color: var(--text-tertiary);
}

.affix {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.clear {
  margin-right: calc(var(--space-1) * -1);
}
</style>

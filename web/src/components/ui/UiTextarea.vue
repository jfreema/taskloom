<script setup lang="ts">
// Autosizing textarea (spec 7.10): grows between `rows` and `maxRows`. submitOn 'enter' sends on
// Enter (Shift+Enter adds a line); Mod+Enter always submits unless submitOn is 'none'; never during
// IME composition. Attributes other than class and style (aria-*, @paste…) go to the <textarea>.
import { computed, nextTick, onMounted, ref, useAttrs, watch } from 'vue'
import { IS_APPLE } from './keys'

defineOptions({ inheritAttrs: false })

const model = defineModel<string>({ required: true })

const props = withDefaults(
  defineProps<{
    rows?: number
    maxRows?: number
    placeholder?: string
    maxlength?: number
    invalid?: boolean
    disabled?: boolean
    readonly?: boolean
    id?: string
    submitOn?: 'enter' | 'mod-enter' | 'none'
  }>(),
  {
    rows: 2,
    maxRows: 12,
    placeholder: undefined,
    maxlength: undefined,
    invalid: false,
    disabled: false,
    readonly: false,
    id: undefined,
    submitOn: 'none',
  },
)

const emit = defineEmits<{
  submit: [e: KeyboardEvent]
  escape: [e: KeyboardEvent]
  focus: [e: FocusEvent]
  blur: [e: FocusEvent]
}>()

const attrs = useAttrs()
const el = ref<HTMLTextAreaElement | null>(null)
const frameAttrs = computed(() => ({ class: attrs.class, style: attrs.style }))
const fieldAttrs = computed(() => {
  const rest: Record<string, unknown> = { ...attrs }
  delete rest.class
  delete rest.style
  return rest
})

function resize(): void {
  const t = el.value
  if (!t) return
  const cs = getComputedStyle(t)
  const line = parseFloat(cs.lineHeight) || 20
  const chrome = parseFloat(cs.paddingTop) + parseFloat(cs.paddingBottom)
  const borders = parseFloat(cs.borderTopWidth) + parseFloat(cs.borderBottomWidth)
  const min = line * props.rows + chrome + borders
  const max = line * Math.max(props.maxRows, props.rows) + chrome + borders
  t.style.height = 'auto'
  const wanted = t.scrollHeight + borders
  t.style.height = `${Math.min(Math.max(wanted, min), max)}px`
  t.style.overflowY = wanted > max ? 'auto' : 'hidden'
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Escape') {
    emit('escape', e)
    return
  }
  if (e.key !== 'Enter' || props.submitOn === 'none') return
  const mod = IS_APPLE ? e.metaKey : e.ctrlKey
  if (mod || (props.submitOn === 'enter' && !e.shiftKey && !e.altKey && !e.ctrlKey && !e.metaKey)) {
    e.preventDefault()
    emit('submit', e)
  }
}

function focus(): void {
  el.value?.focus()
}

watch(model, () => nextTick(resize))
watch(() => [props.rows, props.maxRows], () => nextTick(resize))
onMounted(resize)

defineExpose({ focus, el })
</script>

<template>
  <div class="ui-textarea" :class="{ 'is-invalid': invalid, 'is-disabled': disabled, 'is-readonly': readonly }" v-bind="frameAttrs">
    <textarea
      :id="id"
      ref="el"
      v-model="model"
      class="control"
      :rows="rows"
      :placeholder="placeholder"
      :maxlength="maxlength"
      :disabled="disabled"
      :readonly="readonly"
      :aria-invalid="invalid ? 'true' : undefined"
      v-bind="fieldAttrs"
      @keydown="onKeydown"
      @input="resize"
      @focus="emit('focus', $event)"
      @blur="emit('blur', $event)"
    />
  </div>
</template>

<style scoped>
.ui-textarea {
  display: flex;
  min-width: 0;
  background: var(--bg-surface);
  border: 1px solid var(--border-default);
  border-bottom-color: var(--border-control);
  border-radius: var(--radius-sm);
  transition:
    border-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.ui-textarea:hover:not(.is-disabled):not(:focus-within) {
  border-color: color-mix(in srgb, var(--border-control) 45%, var(--border-default));
  border-bottom-color: var(--border-control);
}

.ui-textarea:focus-within {
  border-color: var(--focus-ring);
  box-shadow: 0 0 0 1px var(--focus-ring);
}

.ui-textarea.is-invalid {
  border-color: var(--danger);
}

.ui-textarea.is-invalid:focus-within {
  box-shadow: 0 0 0 1px var(--danger);
}

.ui-textarea.is-disabled {
  opacity: 0.45;
}

.ui-textarea.is-readonly {
  background: var(--bg-subtle);
}

.control {
  display: block;
  flex: 1;
  width: 100%;
  min-width: 0;
  margin: 0;
  padding: 6px 10px;
  border: 0;
  outline: none;
  resize: none;
  overflow-y: hidden;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.control:focus-visible {
  outline: none;
}

.control::placeholder {
  color: var(--text-tertiary);
  opacity: 1;
}

/* Touch (7.5): a one-row field is 44px tall (the autosize reads this padding). The field reaches
   over the frame's top and bottom border (it is transparent), so the whole 44px takes a tap. */
@media (pointer: coarse) {
  .control {
    margin: -1px 0;
    padding-top: calc((var(--touch-min) - var(--lh-md)) / 2);
    padding-bottom: calc((var(--touch-min) - var(--lh-md)) / 2);
  }
}
</style>

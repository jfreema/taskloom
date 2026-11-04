<script setup lang="ts">
// Inline edit (spec 7.10): text that turns into a field in place, for names and titles.
// - View mode is a <button> styled as the surrounding text; click, Enter or F2 starts editing with
//   all the text selected. With activate="none" (the parent starts editing through v-model:editing,
//   from F2 on a row or a Rename item) or `disabled`, the view is plain text.
// - Enter or blur saves; `save` fires only when the trimmed value is non-empty and changed. The
//   parent applies it (the component never changes modelValue). Esc restores the old value and
//   stops there, so a dialog or panel around the field stays open.
// - Afterwards focus returns to the view button, or, without one, to what had focus when editing
//   started (the row, the pencil, a menu's trigger). Blur leaves focus where the user put it.
// - multiline: an autosizing textarea where Enter still commits; pasted line breaks become spaces.
import { computed, nextTick, ref, useId, watch } from 'vue'

const editing = defineModel<boolean>('editing', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: string
    /** The field's name, e.g. "List name". Required in practice; optional in the type so `aria-label="…"` also type-checks. */
    ariaLabel?: string
    placeholder?: string
    maxlength?: number
    multiline?: boolean
    activate?: 'click' | 'none'
    disabled?: boolean
  }>(),
  {
    ariaLabel: 'Name',
    placeholder: undefined,
    maxlength: undefined,
    multiline: false,
    activate: 'click',
    disabled: false,
  },
)

const emit = defineEmits<{ save: [value: string]; cancel: [] }>()

const hintId = `${useId()}-hint`
const root = ref<HTMLElement | null>(null)
const view = ref<HTMLElement | null>(null)
const field = ref<HTMLInputElement | HTMLTextAreaElement | null>(null)
const draft = ref('')

/** An edit is running; guards against the blur that can follow Enter or Esc. */
let live = false
/** What had focus when editing started, when that was outside this component. */
let returnTo: HTMLElement | null = null

const interactive = computed(() => props.activate === 'click' && !props.disabled)
const empty = computed(() => props.modelValue.trim() === '')
const hint = computed(() => `Edit ${props.ariaLabel.charAt(0).toLowerCase()}${props.ariaLabel.slice(1)}`)

function oneLine(text: string): string {
  return text.replace(/[\r\n]+/g, ' ')
}

function autosize(): void {
  const el = field.value
  if (!props.multiline || !(el instanceof HTMLTextAreaElement)) return
  el.style.height = 'auto'
  el.style.height = `${el.scrollHeight}px`
}

function begin(): void {
  if (props.disabled) {
    editing.value = false
    return
  }
  live = true
  draft.value = props.modelValue
  void nextTick(() => {
    // Read after the flush: a menu that started the edit has returned focus to its trigger by now.
    const active = document.activeElement
    returnTo = active instanceof HTMLElement && active !== document.body && !root.value?.contains(active) ? active : null
    const el = field.value
    if (!el) return
    el.focus({ preventScroll: true })
    el.select()
    autosize()
  })
}

watch(
  editing,
  (on) => {
    if (on && !live) begin()
    // Turned off by the parent (the item went away): end without saving.
    else if (!on) live = false
  },
  { immediate: true },
)

function start(): void {
  if (!interactive.value || editing.value) return
  editing.value = true
}

function focusBack(): void {
  const target = view.value instanceof HTMLButtonElement ? view.value : returnTo
  returnTo = null
  if (target?.isConnected && !target.closest('[inert]')) target.focus({ preventScroll: true })
}

function end(refocus: boolean): void {
  live = false
  editing.value = false
  if (refocus) void nextTick(focusBack)
  else returnTo = null
}

function commit(refocus: boolean): void {
  if (!live) return
  const value = oneLine(draft.value).trim()
  end(refocus)
  if (value && value !== props.modelValue.trim()) emit('save', value)
}

function cancel(): void {
  if (!live) return
  draft.value = props.modelValue
  end(true)
  emit('cancel')
}

function onViewKeydown(e: KeyboardEvent): void {
  if (e.key !== 'F2' || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
  e.preventDefault()
  start()
}

function onFieldKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter' && !e.altKey && !e.ctrlKey && !e.metaKey) {
    e.preventDefault()
    commit(true)
  } else if (e.key === 'Escape') {
    // Consumed here, so the layer stack doesn't also close the dialog or panel around the field.
    e.preventDefault()
    e.stopPropagation()
    cancel()
  }
}

function onInput(): void {
  if (props.multiline && /[\r\n]/.test(draft.value)) draft.value = oneLine(draft.value)
  autosize()
}

function onBlur(): void {
  // The window lost focus (another app, devtools): the field is still the active element.
  if (field.value && document.activeElement === field.value) return
  commit(false)
}

defineExpose({ start, field })
</script>

<template>
  <span ref="root" class="ui-inline-edit" :class="{ 'is-editing': editing, 'is-multiline': multiline }">
    <template v-if="editing">
      <textarea
        v-if="multiline"
        ref="field"
        v-model="draft"
        class="field"
        rows="1"
        :aria-label="ariaLabel"
        :placeholder="placeholder"
        :maxlength="maxlength"
        @keydown="onFieldKeydown"
        @input="onInput"
        @blur="onBlur"
      />
      <input
        v-else
        ref="field"
        v-model="draft"
        class="field"
        type="text"
        autocomplete="off"
        :aria-label="ariaLabel"
        :placeholder="placeholder"
        :maxlength="maxlength"
        @keydown="onFieldKeydown"
        @blur="onBlur"
      />
    </template>
    <button
      v-else-if="interactive"
      ref="view"
      type="button"
      class="view"
      :class="{ 'is-empty': empty }"
      :aria-describedby="hintId"
      @click="start"
      @keydown="onViewKeydown"
    >
      <span class="text">{{ empty ? placeholder : modelValue }}</span>
    </button>
    <span v-else ref="view" class="view is-static" :class="{ 'is-empty': empty }">
      <span class="text">{{ empty ? placeholder : modelValue }}</span>
    </span>
    <!-- The button's description; hidden, so a heading around the field keeps just the name. -->
    <span v-if="interactive && !editing" :id="hintId" hidden>{{ hint }}</span>
  </span>
</template>

<style scoped>
.ui-inline-edit {
  display: inline-flex;
  min-width: 0;
  max-width: 100%;
  vertical-align: top;
}

.ui-inline-edit.is-editing {
  display: flex;
  width: 100%;
}

/* The view and the field share padding and a matching negative margin, so text doesn't move. */
.view,
.field {
  min-width: 0;
  max-width: calc(100% + 2 * var(--space-1));
  margin: calc(var(--space-0-5) * -1) calc(var(--space-1) * -1);
  padding: var(--space-0-5) var(--space-1);
  border-radius: var(--radius-sm);
  font: inherit;
  letter-spacing: inherit;
  color: inherit;
  text-align: left;
}

.view {
  display: inline-flex;
  background: transparent;
  cursor: text;
  transition: background-color var(--dur-1) var(--ease-out);
}

.view.is-static {
  cursor: inherit;
}

button.view:hover {
  background: var(--bg-hover);
}

.text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.is-multiline .text {
  white-space: normal;
  overflow-wrap: anywhere;
}

.view.is-empty .text {
  color: var(--text-tertiary);
}

.field {
  flex: 1;
  width: calc(100% + 2 * var(--space-1));
  background: var(--bg-surface);
  border: 0;
  outline: none;
  box-shadow: 0 0 0 1px var(--focus-ring);
  color: var(--text-primary);
}

.field::placeholder {
  color: var(--text-tertiary);
}

textarea.field {
  display: block;
  resize: none;
  overflow: hidden;
}
</style>

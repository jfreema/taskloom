<script setup lang="ts">
// "+ Add status" at the end of a status editor section (8.13). The button turns into a name field:
// Enter adds the status and keeps the field open for the next one; Enter on an empty field or Esc
// closes it (Esc stops there, so the dialog stays open). A name still in the field when focus
// leaves it is added too, so nothing typed is lost.
import { nextTick, ref } from 'vue'
import { Plus } from '@lucide/vue'
import type { StatusCategory } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiInput from '../ui/UiInput.vue'
import { sectionLabel } from './statusRules'

const props = defineProps<{ category: StatusCategory }>()

const emit = defineEmits<{ add: [name: string] }>()

const adding = ref(false)
const draft = ref('')
const root = ref<HTMLElement | null>(null)
const input = ref<InstanceType<typeof UiInput> | null>(null)

function start(): void {
  draft.value = ''
  adding.value = true
  void nextTick(() => input.value?.focus())
}

function commit(): boolean {
  const name = draft.value.trim()
  if (!name) return false
  emit('add', name)
  draft.value = ''
  return true
}

function close(refocus: boolean): void {
  adding.value = false
  draft.value = ''
  if (refocus) void nextTick(() => root.value?.querySelector<HTMLElement>('button')?.focus({ preventScroll: true }))
}

function onEnter(e: KeyboardEvent): void {
  e.preventDefault()
  if (!commit()) close(true)
}

function onEscape(e: KeyboardEvent): void {
  // Handled here: the layer stack skips an Esc whose default was prevented.
  e.preventDefault()
  close(true)
}

function onBlur(): void {
  if (!adding.value) return
  commit()
  close(false)
}
</script>

<template>
  <div ref="root" class="add">
    <div v-if="adding" class="field">
      <UiInput
        ref="input"
        v-model="draft"
        size="sm"
        :maxlength="100"
        placeholder="Status name"
        autocomplete="off"
        :aria-label="`New status in ${sectionLabel(props.category)}`"
        @enter="onEnter"
        @escape="onEscape"
        @blur="onBlur"
      />
      <span class="hint" aria-hidden="true">Enter to add · Esc to finish</span>
    </div>
    <UiButton v-else variant="ghost" size="sm" :icon="Plus" :aria-label="`Add status to ${sectionLabel(props.category)}`" @click="start">
      Add status
    </UiButton>
  </div>
</template>

<style scoped>
.add {
  display: flex;
  align-items: center;
  min-height: var(--control-md);
  margin-top: var(--space-1-5);
}

.add :deep(.ui-button--ghost) {
  margin-left: calc(var(--space-2) * -1);
  color: var(--text-secondary);
}

.field {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
}

.field :deep(.ui-input) {
  flex: 1;
  max-width: 320px;
}

.hint {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
  white-space: nowrap;
}

@media (max-width: 639px) {
  .hint {
    display: none;
  }

  .field :deep(.ui-input) {
    max-width: none;
  }
}
</style>

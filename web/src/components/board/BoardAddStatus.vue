<script setup lang="ts">
// "+ Add status" after the last column (8.8, Member+). It turns into a name field: Enter creates an
// Active status in the next swatch after the last status's colour (7.2), at the end of the list's
// statuses (createStatus places it at max + 1024), and keeps the field open for another one. An
// Active column shows before the Done ones, so the field is scrolled back into view after it. Esc,
// or clicking away from an empty field, turns it back into the button.
import { nextTick, ref } from 'vue'
import { Plus } from '@lucide/vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { noop } from '../../lib/async'
import { nextSwatch } from '../../lib/colors'
import { useWorkspaces } from '../../stores/workspaces'
import UiIcon from '../ui/UiIcon.vue'
import { useBoard } from './boardContext'

const board = useBoard()
const workspaces = useWorkspaces()
const announce = useAnnouncer()

const editing = ref(false)
const draft = ref('')
const editor = ref<HTMLElement | null>(null)
const field = ref<HTMLInputElement | null>(null)
const button = ref<HTMLButtonElement | null>(null)

function keepInView(): void {
  void nextTick(() => editor.value?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' }))
}

function open(): void {
  editing.value = true
  void nextTick(() => field.value?.focus({ preventScroll: true }))
  keepInView()
}

function close(refocus: boolean): void {
  editing.value = false
  draft.value = ''
  if (refocus) void nextTick(() => button.value?.focus({ preventScroll: true }))
}

function submit(): void {
  const name = draft.value.trim()
  if (!name) return
  const listId = board.listId.value
  const byPosition = workspaces.statusesByPosition(listId)
  const color = nextSwatch(byPosition[byPosition.length - 1]?.color)
  draft.value = ''
  void workspaces.createStatus(listId, { name, color, category: 'Active' }).catch(noop)
  announce(`${name} added`)
  keepInView()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter') {
    e.preventDefault()
    submit()
  } else if (e.key === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    close(true)
  }
}

function onBlur(): void {
  if (!draft.value.trim()) close(false)
}
</script>

<template>
  <div class="add-status">
    <div v-if="editing" ref="editor" class="editor">
      <input
        ref="field"
        v-model="draft"
        class="field"
        type="text"
        autocomplete="off"
        enterkeyhint="done"
        aria-label="Status name"
        placeholder="Status name"
        maxlength="100"
        @keydown="onKeydown"
        @blur="onBlur"
      />
      <p class="hint">Enter to add · Esc to close</p>
    </div>
    <button v-else ref="button" type="button" class="add" @click="open">
      <UiIcon :icon="Plus" :size="16" />
      <span>Add status</span>
    </button>
  </div>
</template>

<style scoped>
.add-status {
  flex: none;
  width: 280px;
}

.add {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  height: 40px;
  padding: 0 var(--space-3);
  border: 1px dashed var(--border-default);
  border-radius: var(--radius-lg);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out);
}

.add:hover {
  border-color: var(--border-control);
  background: var(--bg-hover);
  color: var(--text-primary);
}

.editor {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  padding: var(--space-2);
  border-radius: var(--radius-lg);
  background: var(--bg-column);
}

.field {
  width: 100%;
  height: var(--control-md);
  padding: 0 var(--space-2);
  border: 1px solid var(--accent);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  box-shadow: 0 0 0 3px var(--accent-subtle);
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.field::placeholder {
  color: var(--text-tertiary);
  font-weight: var(--fw-regular);
}

.field:focus-visible {
  outline: none;
}

.hint {
  margin: 0;
  padding: 0 var(--space-0-5);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}
</style>

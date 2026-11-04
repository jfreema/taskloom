<script setup lang="ts">
// The add card of a column (8.8, Member+): opened by the header "+" (top, placement 'Top') or by
// "+ Add task" (bottom, placement 'Bottom').
// - Enter creates the task in this status with the inherited filter values (8.1), and keeps the
//   field open and focused for the next one. "Add task" does the same for the pointer.
// - Esc clears the text, or on an empty field closes the card; clicking away closes an empty one.
//   The column puts focus back on the button that opened it.
// - Pasting 2–100 lines asks "Create N tasks?" and creates them in the pasted order (9.3).
import { nextTick, onMounted, ref } from 'vue'
import { X } from '@lucide/vue'
import { newId } from '../../api'
import { useConfirm } from '../../composables/useConfirm'
import { useToast } from '../../composables/useToast'
import { noop } from '../../lib/async'
import { MAX_PASTED_TASKS, pasteSummary, splitPastedLines } from '../../lib/paste'
import { useTasks } from '../../stores/tasks'
import type { CreateTaskRequest, Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { useBoard } from './boardContext'

const props = defineProps<{
  statusId: Uuid
  placement: 'Top' | 'Bottom'
  /** The inherited filter values (ctx.createDefaults). */
  defaults: Partial<CreateTaskRequest>
}>()

const emit = defineEmits<{ close: [refocus: boolean] }>()

const board = useBoard()
const tasks = useTasks()
const confirm = useConfirm()
const toast = useToast()

const field = ref<HTMLTextAreaElement | null>(null)
const draft = ref('')
let pasting = false

function autosize(): void {
  const el = field.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${el.scrollHeight}px`
}

function keepInView(): void {
  void nextTick(() => {
    autosize()
    field.value?.closest('.add-card')?.scrollIntoView?.({ block: 'nearest' })
  })
}

onMounted(() => {
  field.value?.focus({ preventScroll: true })
  keepInView()
})

function create(title: string): void {
  const id = newId()
  const r: CreateTaskRequest = { ...props.defaults, statusId: props.statusId, id, title, placement: props.placement }
  board.created(id)
  void tasks.create(board.listId.value, r).catch(noop)
}

function submit(): void {
  const title = draft.value.replace(/[\r\n]+/g, ' ').trim()
  if (!title) return
  draft.value = ''
  create(title)
  field.value?.focus({ preventScroll: true })
  keepInView()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter' && !e.altKey && !e.ctrlKey && !e.metaKey) {
    e.preventDefault()
    submit()
  } else if (e.key === 'Escape') {
    // Consumed here, so the board's Esc (leave the board) doesn't also run.
    e.preventDefault()
    e.stopPropagation()
    if (draft.value) draft.value = ''
    else emit('close', true)
  }
}

function onBlur(): void {
  if (pasting) return
  if (!draft.value.trim()) emit('close', false)
}

async function onPaste(e: ClipboardEvent): Promise<void> {
  const lines = splitPastedLines(e.clipboardData?.getData('text/plain') ?? '')
  if (lines.length < 2) return
  e.preventDefault()
  if (lines.length > MAX_PASTED_TASKS) {
    toast.show({ message: `You can paste up to ${MAX_PASTED_TASKS} tasks at a time.` })
    return
  }
  const n = lines.length
  pasting = true
  let ok = false
  try {
    ok = await confirm({ title: `Create ${n} tasks?`, message: pasteSummary(lines), confirmLabel: `Create ${n} tasks` })
  } finally {
    pasting = false
  }
  if (!ok) return
  // Each Top create lands above the previous one, so they are sent in reverse to keep the pasted order.
  for (const title of props.placement === 'Top' ? [...lines].reverse() : lines) create(title)
  toast.success(`Created ${n} tasks`)
  field.value?.focus({ preventScroll: true })
  keepInView()
}
</script>

<template>
  <div class="add-card">
    <textarea
      ref="field"
      v-model="draft"
      class="field"
      rows="1"
      autocomplete="off"
      enterkeyhint="done"
      aria-label="Task name"
      placeholder="Task name"
      maxlength="500"
      @input="autosize"
      @keydown="onKeydown"
      @blur="onBlur"
      @paste="onPaste"
    />
    <div class="actions">
      <UiButton variant="primary" size="xs" @mousedown.prevent @click="submit">Add task</UiButton>
      <UiIconButton :icon="X" label="Close" size="xs" @mousedown.prevent @click="emit('close', true)" />
    </div>
  </div>
</template>

<style scoped>
.add-card {
  display: flex;
  flex: none;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-2);
  border: 1px solid var(--accent);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  box-shadow: 0 0 0 3px var(--accent-subtle);
  scroll-margin: var(--space-2);
}

.field {
  display: block;
  width: 100%;
  min-height: calc(var(--lh-md) + 4px);
  padding: 2px var(--space-1);
  border: 0;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  resize: none;
  overflow: hidden;
  overflow-wrap: anywhere;
}

.field::placeholder {
  color: var(--text-tertiary);
  font-weight: var(--fw-regular);
}

.field:focus-visible {
  outline: none;
}

.actions {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}
</style>

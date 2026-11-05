<script setup lang="ts">
// An add row of the List (8.7, Member+): "+ Add task" at the end of a group, the header "+" row
// under a group header (placement Top), or "+ Add subtask" after a parent's subtasks. It is one
// spanning gridcell that turns into a title input.
// - Enter creates with the given defaults (the group's value over the inherited filter values) and
//   keeps the input open and focused; the store sends the creates of one list, status and parent
//   one at a time, so quick Enters keep their order.
// - Esc clears the text, or on an empty input closes the row and returns focus to its button
//   (the header's "+" for a Top row). Clicking away closes an empty row.
// - Pasting 2–100 lines asks "Create N tasks?" and creates them in the pasted order.
import { computed, nextTick, ref, watch } from 'vue'
import { Plus } from '@lucide/vue'
import { newId } from '../../api'
import { useConfirm } from '../../composables/useConfirm'
import { useToast } from '../../composables/useToast'
import { noop } from '../../lib/async'
import { MAX_PASTED_TASKS, pasteSummary, splitPastedLines } from '../../lib/paste'
import { useTasks } from '../../stores/tasks'
import type { CreateTaskRequest, Uuid } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import { useListTable } from './listTable'

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    rowKey: string
    groupKey: string
    parentId: Uuid | null
    placement: 'Top' | 'Bottom'
    /** What a new task gets: the group's value over the inherited filter values. */
    defaults: Partial<CreateTaskRequest>
    tabStop: boolean
    placeholder?: string
    drop: 'top' | 'bottom' | null
  }>(),
  { placeholder: undefined },
)

const table = useListTable()
const tasks = useTasks()
const confirm = useConfirm()
const toast = useToast()

const rowEl = ref<HTMLElement | null>(null)
const input = ref<HTMLInputElement | null>(null)
const button = ref<HTMLButtonElement | null>(null)
const draft = ref('')
const sub = computed(() => props.parentId !== null)
const top = computed(() => props.placement === 'Top')
const label = computed(() => (sub.value ? 'Add subtask' : 'Add task'))
const inputLabel = computed(() => (sub.value ? 'Subtask name' : 'Task name'))
const shownPlaceholder = computed(() => props.placeholder ?? (sub.value ? 'Subtask name' : 'Task name'))
let pasting = false

table.dnd.target(
  rowEl,
  () =>
    props.parentId
      ? { kind: 'subtask-add', key: props.rowKey, parentId: props.parentId, groupKey: props.groupKey }
      : top.value
        ? { kind: 'header', key: props.rowKey, groupKey: props.groupKey }
        : { kind: 'end', key: props.rowKey, groupKey: props.groupKey },
  () => true,
)

watch(
  open,
  (on) => {
    if (!on) {
      draft.value = ''
      return
    }
    void nextTick(() => {
      input.value?.focus({ preventScroll: true })
      input.value?.scrollIntoView?.({ block: 'nearest' })
    })
  },
  { immediate: true },
)

function create(title: string): void {
  const id = newId()
  const r: CreateTaskRequest = { ...props.defaults, id, title, placement: props.placement }
  if (props.parentId) r.parentTaskId = props.parentId
  table.created(id, { groupKey: props.groupKey, placement: props.placement, parentId: props.parentId })
  void tasks.create(table.listId.value, r).catch(noop)
}

function keepInView(): void {
  void nextTick(() => input.value?.scrollIntoView?.({ block: 'nearest' }))
}

function submit(): void {
  const title = draft.value.trim()
  if (!title) return
  draft.value = ''
  create(title)
  keepInView()
}

/** Where focus goes when the row closes: its button, or the header's "+" for a Top row. */
function closeTarget(): HTMLElement | null {
  if (!top.value) return null
  return rowEl.value?.closest('[role="rowgroup"]')?.querySelector<HTMLElement>('[data-add-top]') ?? null
}

function close(refocus: boolean): void {
  const target = refocus ? closeTarget() : null
  open.value = false
  if (!refocus) return
  void nextTick(() => (target ?? button.value)?.focus({ preventScroll: true }))
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter' && !e.shiftKey && !e.altKey && !e.ctrlKey && !e.metaKey) {
    e.preventDefault()
    submit()
  } else if (e.key === 'Escape') {
    // Consumed here: the grid's Esc (clear selection, leave the grid) doesn't also run.
    e.preventDefault()
    e.stopPropagation()
    if (draft.value) draft.value = ''
    else close(true)
  }
}

function onBlur(): void {
  if (pasting) return
  // Clicking away closes an empty row; typed text stays until Enter or Esc.
  if (!draft.value.trim()) close(false)
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
  // Each Top create lands above the last one, so they are sent in reverse to keep the pasted order.
  for (const title of top.value ? [...lines].reverse() : lines) create(title)
  toast.success(`Created ${n} tasks`)
  if (open.value) {
    input.value?.focus({ preventScroll: true })
    keepInView()
  }
}
</script>

<template>
  <div
    ref="rowEl"
    role="row"
    class="add-row"
    :class="{ 'is-sub': sub, 'is-open': open, 'drop-top': drop === 'top', 'drop-bottom': drop === 'bottom' }"
    data-row
    :data-row-key="rowKey"
    :aria-level="sub ? 2 : 1"
  >
    <div role="gridcell" class="add-cell" :aria-colspan="table.colCount.value">
      <template v-if="open">
        <span class="plus" aria-hidden="true"><UiIcon :icon="Plus" :size="14" /></span>
        <input
          ref="input"
          v-model="draft"
          class="add-input"
          data-nav
          type="text"
          autocomplete="off"
          enterkeyhint="done"
          :aria-label="inputLabel"
          :placeholder="shownPlaceholder"
          :tabindex="tabStop ? 0 : -1"
          @keydown="onKeydown"
          @blur="onBlur"
          @paste="onPaste"
        />
      </template>
      <button
        v-else
        ref="button"
        type="button"
        class="add-button focus-inset"
        data-nav
        :tabindex="tabStop ? 0 : -1"
        @click="open = true"
      >
        <span class="plus"><UiIcon :icon="Plus" :size="14" /></span>
        <span>{{ label }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.add-row {
  position: relative;
  display: flex;
  height: var(--row-list);
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-surface);
}

/* The "+" lines up with the rows' status icons and the text (and the input's text) with the titles. */
.add-cell {
  position: relative;
  display: flex;
  flex: 1 1 auto;
  align-items: center;
  min-width: 0;
  padding-left: calc(var(--gutter-w) + 20px + var(--space-1-5) + var(--touch-shift, 0px));
  padding-right: var(--space-3);
}

.is-sub .add-cell {
  padding-left: calc(var(--gutter-w) + var(--space-6) + 20px + var(--space-1-5) + var(--touch-shift, 0px));
}

.plus {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: var(--control-xs);
  color: var(--text-tertiary);
}

/* The thread line ends at the subtask add row. */
.is-sub .add-cell::before {
  content: '';
  position: absolute;
  top: 0;
  bottom: 50%;
  left: calc(var(--gutter-w) + 20px + var(--space-1-5) + 11.5px + var(--touch-shift, 0px));
  width: 1px;
  background: var(--border-default);
}

.add-button {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  height: var(--control-sm);
  padding: 0 var(--space-2) 0 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-tertiary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.add-button:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.add-button:hover .plus {
  color: inherit;
}

.add-input {
  flex: 1 1 auto;
  min-width: 0;
  height: var(--control-sm);
  padding: 0 var(--space-1-5) 0 calc(var(--space-1-5) - 1px);
  border: 1px solid var(--accent);
  border-radius: var(--radius-sm);
  background: var(--bg-surface);
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  box-shadow: 0 0 0 3px var(--accent-subtle);
}

.add-input::placeholder {
  color: var(--text-tertiary);
}

.add-input:focus {
  outline: none;
}

.drop-top::before,
.drop-bottom::after {
  content: '';
  position: absolute;
  z-index: 1;
  left: var(--gutter-w);
  right: 0;
  height: 6px;
  background:
    radial-gradient(circle at 3px 3px, var(--accent) 3px, transparent 3.5px),
    linear-gradient(var(--accent), var(--accent)) 0 2px / 100% 2px no-repeat;
  pointer-events: none;
}

.drop-top::before {
  top: -4px;
}

.drop-bottom::after {
  bottom: -4px;
}

@container page (width < 560px) {
  .add-row {
    height: 44px;
    padding-left: var(--space-3);
  }
}

/* Touch (7.5): "Add task" is a 44px target, the height of the row. */
@media (pointer: coarse) {
  .add-button {
    position: relative;
  }

  .add-button::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: var(--touch-min);
    transform: translateY(-50%);
  }
}
</style>

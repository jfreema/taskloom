<script setup lang="ts">
// The task title (8.10): a borderless textarea that grows with its text, 22/28 600 in the panel
// and 28/34 650 on the full page. Enter and blur save; Esc restores. A blank title is never saved.
// Guests see plain text.
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useTaskActions } from '../../composables/useTaskActions'
import type { Task } from '../../types'

const MAX_TITLE = 500

const props = defineProps<{ task: Task; canEdit: boolean; size: 'panel' | 'page' }>()

const actions = useTaskActions()
const draft = ref(props.task.title)
const focused = ref(false)
const el = ref<HTMLTextAreaElement | null>(null)
let observer: ResizeObserver | null = null

watch(
  () => [props.task.id, props.task.title] as const,
  ([id, title], [prevId]) => {
    if (!focused.value || id !== prevId) draft.value = title
  },
)

function resize(): void {
  const t = el.value
  if (!t) return
  t.style.height = 'auto'
  t.style.height = `${t.scrollHeight}px`
}

watch(draft, () => void nextTick(resize))

onMounted(() => {
  resize()
  if (el.value && typeof ResizeObserver !== 'undefined') {
    let width = 0
    observer = new ResizeObserver(([entry]) => {
      const w = entry?.contentRect.width ?? 0
      if (w !== width) {
        width = w
        resize()
      }
    })
    observer.observe(el.value)
  }
})
onBeforeUnmount(() => observer?.disconnect())

/** Focus goes back to the panel (or page) root, where J, K and the other keys work. */
function leave(): void {
  const scope = el.value?.closest<HTMLElement>('[data-task-panel-root], [data-shortcut-scope="panel"]')
  if (scope) scope.focus({ preventScroll: true })
  else el.value?.blur()
}

function commit(): void {
  const next = draft.value.replace(/\s+/g, ' ').trim()
  if (!next) {
    draft.value = props.task.title
    return
  }
  draft.value = next
  if (next !== props.task.title) void actions.rename(props.task.id, next)
}

function onInput(): void {
  // No line breaks, even pasted ones.
  if (/[\r\n]/.test(draft.value)) draft.value = draft.value.replace(/[\r\n]+/g, ' ')
  resize()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter') {
    e.preventDefault()
    commit()
    leave()
  } else if (e.key === 'Escape') {
    e.preventDefault()
    draft.value = props.task.title
    leave()
  }
}

function onBlur(): void {
  focused.value = false
  commit()
}
</script>

<template>
  <textarea
    v-if="canEdit"
    ref="el"
    v-model="draft"
    class="task-title cv11"
    :class="`size-${size}`"
    rows="1"
    aria-label="Task title"
    :maxlength="MAX_TITLE"
    spellcheck="true"
    @focus="focused = true"
    @blur="onBlur"
    @input="onInput"
    @keydown="onKeydown"
  />
  <p v-else class="task-title is-static cv11" :class="`size-${size}`">{{ task.title }}</p>
</template>

<style scoped>
.task-title {
  display: block;
  width: calc(100% + var(--space-2) * 2);
  margin: 0 calc(var(--space-2) * -1);
  padding: var(--space-0-5) var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-primary);
  font-family: inherit;
  font-size: var(--fs-2xl);
  --field-font-size: var(--fs-2xl);
  line-height: var(--lh-2xl);
  font-weight: var(--fw-semibold);
  letter-spacing: -0.01em;
  overflow: hidden;
  overflow-wrap: anywhere;
  resize: none;
  transition: background-color var(--dur-1) var(--ease-out);
}

textarea.task-title:hover:not(:focus) {
  background: var(--bg-hover);
}

textarea.task-title:focus-visible {
  outline-offset: 0;
}

.task-title.size-page {
  font-size: var(--fs-3xl);
  --field-font-size: var(--fs-3xl);
  line-height: var(--lh-3xl);
  font-weight: var(--fw-bold);
  letter-spacing: -0.015em;
}

.is-static {
  white-space: pre-wrap;
}

/* Touch (7.5): the field is a 44px target; the extra padding is taken back by margins, so the
   title doesn't move. It grows 4px up (the status row's 44px targets reach to 4px above it) and
   the rest down. */
@media (pointer: coarse) {
  textarea.task-title {
    --title-lh: var(--lh-2xl);
    padding-top: var(--space-1-5);
    padding-bottom: max(var(--space-0-5), calc(var(--touch-min) - var(--title-lh) - var(--space-1-5)));
    margin-top: calc(var(--space-1) * -1);
    margin-bottom: min(0px, calc(var(--space-0-5) + var(--space-1-5) + var(--title-lh) - var(--touch-min)));
  }

  textarea.task-title.size-page {
    --title-lh: var(--lh-3xl);
  }
}

/* Phones: the page title is 22/28 like the panel's. */
@media (max-width: 639px) {
  .task-title.size-page,
  textarea.task-title.size-page {
    --title-lh: var(--lh-2xl);
    font-size: var(--fs-2xl);
    --field-font-size: var(--fs-2xl);
    line-height: var(--lh-2xl);
    font-weight: var(--fw-semibold);
    letter-spacing: -0.01em;
  }
}
</style>

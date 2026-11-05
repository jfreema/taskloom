<script setup lang="ts">
// The description (8.10): the label row ("Description", "Add checklist", the formatting toolbar and
// the save state) and the editor, which loads on demand. TaskDetail keys this by task id, so J and
// K remount it, and a pending save is flushed on the way out.
// - The toolbar sits in the label row and fades in while the editor or the toolbar has focus, and
//   while the pointer is over an empty description, so showing it never moves the text. On a phone
//   (a narrow section and a touch screen) it takes a row of its own, shown only while editing.
// - Autosave: 800ms after typing stops, and when focus leaves the editor. Nothing is sent while the
//   HTML equals what the server has, so viewing or switching tasks never saves.
// - A newer detail replaces the content only while the editor is idle: not focused, nothing
//   unsaved or being saved, and requested after the last save began.
// - Text the server hasn't accepted stays in tasks.descriptionDrafts (a failed save, or an edit
//   still unsaved when the editor closes): opening the task again restores it with "Couldn't save ·
//   Try again", and coming back online sends it again.
import { computed, defineAsyncComponent, onBeforeUnmount, onMounted, ref, shallowRef, useId, watch } from 'vue'
import { ListChecks } from '@lucide/vue'
import type { Editor } from '@tiptap/vue-3'
import { useDebouncedFn } from '../../composables/useDebouncedFn'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { errorMessage } from '../../composables/useToast'
import { noop } from '../../lib/async'
import { isEmptyHtml } from '../../lib/html'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import EditorToolbar from './EditorToolbar.vue'

const loadEditor = () => import('./DescriptionEditor.vue')
const DescriptionEditor = defineAsyncComponent(loadEditor)

// Warm the editor's chunk once the app is idle (this module loads with the app shell), so the first
// task opened shows its description at once instead of after the chunk arrives.
if (typeof window !== 'undefined') {
  const warm = () => void loadEditor().catch(noop)
  if ('requestIdleCallback' in window) window.requestIdleCallback(warm, { timeout: 4000 })
  else setTimeout(warm, 1500)
}

interface EditorApi {
  load(html: string): string
  addChecklist(): void
  focusEnd(): void
  editor: Editor | undefined
}

const SAVE_DELAY_MS = 800
const SAVED_SHOWN_MS = 2000

const props = defineProps<{
  taskId: Uuid
  /** The server's description, or null until the detail arrives. */
  html: string | null
  /** performance.now() when the request behind `html` was queued. */
  requestedAt: number
  canEdit: boolean
  headingTag: 'h2' | 'h3'
}>()
const emit = defineEmits<{ files: [files: File[]] }>()

const tasks = useTasks()
const labelId = `${useId()}-description`
const releaseHold = tasks.holdDescription(props.taskId)

const editor = shallowRef<EditorApi | null>(null)
/** The editor's rendering of what the server has; null until the editor has loaded. */
const lastSaved = ref<string | null>(null)
/** The editor's current HTML. */
const current = ref<string | null>(null)
/** The server HTML last loaded into the editor. */
let loadedHtml: string | null = null
/** Focus is in the editor or its toolbar. */
const focusIn = ref(false)
const linkOpen = ref(false)
const hovering = ref(false)
const inFlight = ref(0)
let saveStartedAt = 0
let saveSeq = 0

type SaveState = 'idle' | 'saving' | 'saved' | 'error'
const saveState = ref<SaveState>('idle')
const savedVisible = ref(false)
const errorText = ref('')
const retryable = ref(true)
let savedTimer: ReturnType<typeof setTimeout> | undefined

const ready = computed(() => lastSaved.value !== null)
const dirty = computed(() => ready.value && current.value !== lastSaved.value)
const editing = computed(() => focusIn.value || linkOpen.value)
const readOnlyEmpty = computed(() => !props.canEdit && props.html !== null && isEmptyHtml(props.html))
const waiting = computed(() => props.html === null || (!readOnlyEmpty.value && !ready.value))
const showSkeleton = useDelayedFlag(waiting, 200)
const toolbarEditor = computed(() => (props.canEdit && ready.value ? editor.value?.editor : undefined))
const toolbarShown = computed(() => editing.value || (hovering.value && current.value === ''))

function clearSavedTimer(): void {
  if (savedTimer !== undefined) clearTimeout(savedTimer)
  savedTimer = undefined
}

function showError(text: string, canRetry: boolean): void {
  saveState.value = 'error'
  retryable.value = canRetry
  errorText.value = canRetry ? "Couldn't save" : text || "Couldn't save"
}

function save(): void {
  debouncedSave.cancel()
  const html = current.value
  if (html === null || html === lastSaved.value || !props.canEdit) return
  const seq = ++saveSeq
  inFlight.value++
  saveStartedAt = performance.now()
  saveState.value = 'saving'
  clearSavedTimer()
  savedVisible.value = false
  // The store keeps a failed text in descriptionDrafts and forgets it once a save succeeds.
  tasks.update(props.taskId, { description: html }, { undo: false }).then(
    () => {
      inFlight.value--
      lastSaved.value = html
      if (seq !== saveSeq) return
      saveState.value = 'saved'
      savedVisible.value = true
      savedTimer = setTimeout(() => (savedVisible.value = false), SAVED_SHOWN_MS)
    },
    (err: unknown) => {
      inFlight.value--
      if (seq !== saveSeq) return
      // The store has already toasted the failure (with Retry where it helps).
      const { message, retryable: canRetry } = errorMessage(err)
      showError(message, canRetry)
    },
  )
}

const debouncedSave = useDebouncedFn(save, SAVE_DELAY_MS)

/** Loads the server's HTML when nothing local could be lost. */
function tryLoad(): void {
  const html = props.html
  const ed = editor.value
  if (html === null || !ed || !ready.value) return
  if (editing.value || dirty.value || inFlight.value > 0 || props.requestedAt < saveStartedAt) return
  if (html === loadedHtml) return
  loadedHtml = html
  const normalized = ed.load(html)
  lastSaved.value = normalized
  current.value = normalized
}

watch(() => [props.html, props.requestedAt] as const, tryLoad)

/** Puts back text a failed save (or a closed editor) left unsaved, as an unsaved edit. */
function restoreDraft(): void {
  const draft = tasks.descriptionDrafts.get(props.taskId)
  const ed = editor.value
  if (!draft || !ed || !props.canEdit) return
  const restored = ed.load(draft.html)
  current.value = restored
  if (restored === lastSaved.value) {
    tasks.clearDescriptionDraft(props.taskId)
    return
  }
  showError('', draft.retryable)
}

function onReady(normalized: string): void {
  loadedHtml = props.html
  lastSaved.value = normalized
  current.value = normalized
  restoreDraft()
  // A detail may have arrived while the editor was loading.
  tryLoad()
}

// A Retry toast (or a closed editor's flush) saved the draft this editor shows: it is saved now.
watch(
  () => tasks.descriptionDrafts.get(props.taskId)?.html,
  (now, before) => {
    if (now !== undefined || before === undefined || current.value !== before || inFlight.value > 0) return
    lastSaved.value = before
    if (saveState.value !== 'error') return
    saveState.value = 'saved'
    savedVisible.value = true
    clearSavedTimer()
    savedTimer = setTimeout(() => (savedVisible.value = false), SAVED_SHOWN_MS)
  },
)

function onChange(html: string): void {
  current.value = html
  if (html !== lastSaved.value) {
    if (saveState.value === 'saved') savedVisible.value = false
    debouncedSave()
  } else {
    debouncedSave.cancel()
  }
}

watch(editing, (on) => {
  if (on) return
  save()
  tryLoad()
})

function inEditor(node: EventTarget | null): boolean {
  return node instanceof Element && !!node.closest('[data-description-editor], [data-editor-toolbar]')
}

function onFocusIn(e: FocusEvent): void {
  focusIn.value = inEditor(e.target)
}

function onFocusOut(e: FocusEvent): void {
  if (!inEditor(e.relatedTarget)) focusIn.value = false
}

/** Hover is a mouse thing: a tap would otherwise leave the toolbar up until the next tap elsewhere. */
function onPointer(e: PointerEvent, inside: boolean): void {
  if (e.pointerType === 'mouse' || e.pointerType === 'pen') hovering.value = inside
}

function addChecklist(): void {
  editor.value?.addChecklist()
}

/** Back online after a failed save: send it again. */
function onOnline(): void {
  if (saveState.value === 'error' && retryable.value && dirty.value) save()
}

onMounted(() => window.addEventListener('online', onOnline))

onBeforeUnmount(() => {
  window.removeEventListener('online', onOnline)
  clearSavedTimer()
  debouncedSave.cancel()
  releaseHold()
  const html = current.value
  if (!props.canEdit || html === null || html === lastSaved.value || tasks.isPendingDelete(props.taskId)) return
  // Kept until the server has it, so reopening the task restores it if this last save fails.
  tasks.setDescriptionDraft(props.taskId, html, retryable.value)
  tasks.sendDescriptionDraft(props.taskId)
})

const stateText = computed(() => {
  if (saveState.value === 'saving') return 'Saving…'
  if (saveState.value === 'error') return errorText.value
  if (saveState.value === 'saved') return 'Saved'
  return ''
})
</script>

<template>
  <section
    class="description"
    :aria-labelledby="labelId"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
    @pointerenter="onPointer($event, true)"
    @pointerleave="onPointer($event, false)"
  >
    <div class="label-row">
      <component :is="headingTag" :id="labelId" class="section-title">Description</component>
      <template v-if="canEdit && ready">
        <UiButton class="add-checklist is-wide" variant="ghost" size="sm" :icon="ListChecks" @mousedown.prevent @click="addChecklist">
          Add checklist
        </UiButton>
        <UiIconButton class="add-checklist is-narrow" :icon="ListChecks" label="Add checklist" @mousedown.prevent @click="addChecklist" />
      </template>
      <EditorToolbar
        v-if="toolbarEditor"
        class="toolbar"
        :class="{ 'is-shown': toolbarShown }"
        :editor="toolbarEditor"
        @link-open="(v: boolean) => (linkOpen = v)"
      />
      <span class="save-state" :class="`is-${saveState}`" aria-live="polite">
        <span class="state-text" :class="{ 'is-fading': saveState === 'saved' && !savedVisible }">{{ stateText }}</span>
        <UiButton v-if="saveState === 'error' && retryable" variant="link" size="xs" @click="save">Try again</UiButton>
      </span>
    </div>
    <p v-if="readOnlyEmpty" class="none">No description</p>
    <template v-else-if="html !== null">
      <DescriptionEditor
        ref="editor"
        :html="html"
        :editable="canEdit"
        :active="editing"
        @ready="onReady"
        @change="onChange"
        @files="(files: File[]) => emit('files', files)"
      />
    </template>
    <div v-if="waiting && showSkeleton" class="skeleton" aria-hidden="true">
      <UiSkeleton :lines="3" />
    </div>
  </section>
</template>

<style scoped>
.description {
  container: description / inline-size;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.label-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
  min-height: var(--control-sm);
}

.section-title {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.add-checklist.is-narrow {
  display: none;
}

/* The toolbar follows "Add checklist" after a short rule. Hidden, it keeps its place. */
.toolbar {
  transition:
    opacity var(--dur-2) var(--ease-out),
    visibility 0s;
}

.toolbar::before {
  content: "";
  flex: none;
  width: 1px;
  height: 16px;
  margin-right: var(--space-1-5);
  background: var(--border-subtle);
}

.toolbar:not(.is-shown) {
  opacity: 0;
  visibility: hidden;
  transition:
    opacity var(--dur-2) var(--ease-in),
    visibility 0s linear var(--dur-2);
}

.save-state {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  margin-left: auto;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.save-state.is-error {
  color: var(--danger-text);
}

.state-text {
  transition: opacity var(--dur-3) var(--ease-out);
}

.state-text.is-fading {
  opacity: 0;
}

/* A narrow panel: "Add checklist" goes icon-only, so the toolbar still fits beside it. */
@media not (pointer: coarse) {
  @container description (max-width: 459px) {
    .add-checklist.is-wide {
      display: none;
    }

    .add-checklist.is-narrow {
      display: inline-flex;
    }
  }
}

/* A phone: 44px buttons don't fit the label row, so the toolbar takes a row under it while editing. */
@media (pointer: coarse) {
  .label-row {
    min-height: var(--touch-min);
  }

  @container description (max-width: 599px) {
    .toolbar {
      order: 1;
      flex-basis: 100%;
    }

    .toolbar::before {
      display: none;
    }

    .toolbar:not(.is-shown) {
      display: none;
    }
  }
}

.none {
  margin: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-tertiary);
}

.skeleton {
  padding: var(--space-1) 0;
}
</style>

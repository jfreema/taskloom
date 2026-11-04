<script setup lang="ts">
// The description (8.10): the label row ("Description", "Add checklist", the save state) and the
// editor, which loads on demand. TaskDetail keys this by task id, so J and K remount it, and a
// pending save is flushed on the way out.
// - Autosave: 800ms after typing stops, and when focus leaves the editor. Nothing is sent while the
//   HTML equals what the server has, so viewing or switching tasks never saves.
// - A newer detail replaces the content only while the editor is idle: not focused, nothing
//   unsaved or being saved, and requested after the last save began.
import { computed, defineAsyncComponent, onBeforeUnmount, ref, useId, watch } from 'vue'
import { ListChecks } from '@lucide/vue'
import { useDebouncedFn } from '../../composables/useDebouncedFn'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { errorMessage } from '../../composables/useToast'
import { noop } from '../../lib/async'
import { isEmptyHtml } from '../../lib/html'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'

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

const editor = ref<EditorApi | null>(null)
/** The editor's rendering of what the server has; null until the editor has loaded. */
const lastSaved = ref<string | null>(null)
/** The editor's current HTML. */
const current = ref<string | null>(null)
/** The server HTML last loaded into the editor. */
let loadedHtml: string | null = null
const active = ref(false)
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
const readOnlyEmpty = computed(() => !props.canEdit && props.html !== null && isEmptyHtml(props.html))
const waiting = computed(() => props.html === null || (!readOnlyEmpty.value && !ready.value))
const showSkeleton = useDelayedFlag(waiting, 200)

function clearSavedTimer(): void {
  if (savedTimer !== undefined) clearTimeout(savedTimer)
  savedTimer = undefined
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
      const { message, retryable: canRetry } = errorMessage(err)
      // The store has already toasted the failure (with Retry where it helps).
      saveState.value = 'error'
      retryable.value = canRetry
      errorText.value = canRetry ? "Couldn't save" : message || "Couldn't save"
    },
  )
}

const debouncedSave = useDebouncedFn(save, SAVE_DELAY_MS)

/** Loads the server's HTML when nothing local could be lost. */
function tryLoad(): void {
  const html = props.html
  const ed = editor.value
  if (html === null || !ed || !ready.value) return
  if (active.value || dirty.value || inFlight.value > 0 || props.requestedAt < saveStartedAt) return
  if (html === loadedHtml) return
  loadedHtml = html
  const normalized = ed.load(html)
  lastSaved.value = normalized
  current.value = normalized
}

watch(() => [props.html, props.requestedAt] as const, tryLoad)

function onReady(normalized: string): void {
  loadedHtml = props.html
  lastSaved.value = normalized
  current.value = normalized
  // A detail may have arrived while the editor was loading.
  tryLoad()
}

function onChange(html: string): void {
  current.value = html
  if (html !== lastSaved.value) {
    if (saveState.value === 'saved') savedVisible.value = false
    debouncedSave()
  } else {
    debouncedSave.cancel()
  }
}

function onActive(on: boolean): void {
  active.value = on
  if (on) return
  save()
  tryLoad()
}

function addChecklist(): void {
  editor.value?.addChecklist()
}

onBeforeUnmount(() => {
  clearSavedTimer()
  save()
})

const stateText = computed(() => {
  if (saveState.value === 'saving') return 'Saving…'
  if (saveState.value === 'error') return errorText.value
  if (saveState.value === 'saved') return 'Saved'
  return ''
})
</script>

<template>
  <section class="description" :aria-labelledby="labelId">
    <div class="label-row">
      <component :is="headingTag" :id="labelId" class="section-title">Description</component>
      <UiButton
        v-if="canEdit && ready"
        variant="ghost"
        size="sm"
        :icon="ListChecks"
        @mousedown.prevent
        @click="addChecklist"
      >
        Add checklist
      </UiButton>
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
        @ready="onReady"
        @change="onChange"
        @active="onActive"
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
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.label-row {
  display: flex;
  align-items: center;
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

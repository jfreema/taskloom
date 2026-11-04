<script setup lang="ts">
// The description editor (8.10, 7.13), loaded on demand by DescriptionSection. Tiptap with the spec
// 5.7 tags only: headings 2 and 3, lists, checklists, quotes, code, links. Markdown shortcuts come
// from the input rules (## , ### , - , 1. , [ ] , > , backticks).
// - Content is only ever loaded with setContent(html, { emitUpdate: false }) outside the undo
//   history, so loading a task never counts as an edit and Mod+Z can't undo a load.
// - Files dropped or pasted on the editor go to the uploader, never into the text.
// - The toolbar shows while the editor (or the toolbar) has focus, and on hover while it is empty.
// - Emits `change` with the HTML ("" when empty) and `active` when focus enters or leaves it.
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import StarterKit from '@tiptap/starter-kit'
import { TaskItem, TaskList } from '@tiptap/extension-list'
import { Placeholder } from '@tiptap/extensions'
import { EditorContent, useEditor, type Editor } from '@tiptap/vue-3'
import EditorToolbar from './EditorToolbar.vue'
import { filesOf, pastedFiles } from './useFileDrop'

const PLACEHOLDER = 'Add a description, or type [ ] for a checklist'

const props = defineProps<{ html: string; editable: boolean }>()
const emit = defineEmits<{
  ready: [html: string]
  change: [html: string]
  active: [active: boolean]
  files: [files: File[]]
}>()

const root = ref<HTMLElement | null>(null)
const focusWithin = ref(false)
const hovering = ref(false)
const linkOpen = ref(false)

function htmlOf(e: Editor): string {
  return e.isEmpty ? '' : e.getHTML()
}

/** Guests can't tick checklist items: their checkboxes are disabled, not just inert. */
function syncCheckboxes(e: Editor): void {
  if (e.isDestroyed) return
  for (const box of e.view.dom.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')) box.disabled = !props.editable
}

function loadInto(e: Editor, html: string): string {
  e.chain().setMeta('addToHistory', false).setContent(html, { emitUpdate: false }).run()
  syncCheckboxes(e)
  return htmlOf(e)
}

const editor = useEditor({
  content: '',
  editable: props.editable,
  extensions: [
    StarterKit.configure({
      heading: { levels: [2, 3] },
      link: {
        openOnClick: false,
        autolink: true,
        protocols: ['http', 'https', 'mailto'],
        defaultProtocol: 'https',
      },
      dropcursor: { color: 'var(--accent)', width: 2 },
    }),
    TaskList,
    TaskItem.configure({
      nested: false,
      // The node view's <li> gets these too, so the .rich-text checklist styles match it.
      HTMLAttributes: { 'data-type': 'taskItem' },
      a11y: { checkboxLabel: (node) => node.textContent.trim() || 'Empty checklist item' },
    }),
    Placeholder.configure({ placeholder: PLACEHOLDER }),
  ],
  editorProps: {
    attributes: {
      class: 'rich-text description-content',
      role: 'textbox',
      'aria-label': 'Description',
      'aria-multiline': 'true',
      ...(props.editable ? {} : { 'aria-readonly': 'true' }),
    },
    handleDrop: (_view, event, _slice, moved) => {
      if (moved) return false
      const files = filesOf(event.dataTransfer)
      if (!files.length) return false
      if (props.editable) emit('files', files)
      return true
    },
    handlePaste: (_view, event) => {
      const files = pastedFiles(event.clipboardData)
      if (!files.length) return false
      if (props.editable) emit('files', files)
      return true
    },
    // ProseMirror swallows Escape (it calls preventDefault), so the panel's "Esc leaves the field"
    // never sees it: leave the editor here, to the panel root (or the page's shortcut scope).
    handleKeyDown: (view, event) => {
      if (event.key !== 'Escape' || event.isComposing) return false
      const dom = view.dom as HTMLElement
      const scope = dom.closest<HTMLElement>('[data-task-panel-root], [data-shortcut-scope="panel"]')
      if (scope) scope.focus({ preventScroll: true })
      else dom.blur()
      return true
    },
    // Links open with Mod+click while editing (a plain click places the caret).
    handleClick: (_view, _pos, event) => {
      if (!(event.metaKey || event.ctrlKey)) return false
      const target = event.target instanceof Element ? event.target.closest('a[href]') : null
      const href = target?.getAttribute('href')
      if (!href) return false
      window.open(href, '_blank', 'noopener,noreferrer')
      return true
    },
  },
  onCreate: ({ editor: e }) => {
    emit('ready', loadInto(e as Editor, props.html))
  },
  onUpdate: ({ editor: e }) => {
    emit('change', htmlOf(e as Editor))
  },
})

watch(
  () => props.editable,
  (on) => {
    const e = editor.value
    if (!e) return
    e.setEditable(on, false)
    syncCheckboxes(e)
  },
)

const empty = computed(() => editor.value?.isEmpty ?? true)
const active = computed(() => focusWithin.value || linkOpen.value)
const toolbarShown = computed(() => props.editable && (active.value || (hovering.value && empty.value)))

watch(active, (on) => emit('active', on))

function onFocusIn(): void {
  focusWithin.value = true
}

function onFocusOut(e: FocusEvent): void {
  const next = e.relatedTarget
  if (next instanceof Node && root.value?.contains(next)) return
  focusWithin.value = false
}

onBeforeUnmount(() => {
  if (active.value) emit('active', false)
})

/** Loads server HTML (when the caller allows it) and returns the editor's own rendering of it. */
function load(html: string): string {
  const e = editor.value
  return e ? loadInto(e, html) : html
}

/**
 * "Add checklist": a checklist at the end of the description, after any text already there. The
 * description usually ends with empty paragraphs (the trailing node keeps one there, and Enter on an
 * empty list item leaves one more): the first of them becomes the checklist, else a new one at the end.
 */
function addChecklist(): void {
  const e = editor.value
  if (!e || !props.editable) return
  const doc = e.state.doc
  let before = doc.content.size
  let target: number | null = null
  for (let i = doc.childCount - 1; i >= 0; i--) {
    const child = doc.child(i)
    if (child.type.name !== 'paragraph' || child.content.size !== 0) break
    before -= child.nodeSize
    target = before
  }
  const chain = e.chain()
  if (target === null) {
    target = doc.content.size
    chain.insertContentAt(target, { type: 'paragraph' })
  }
  chain
    .setTextSelection(target + 1)
    .toggleTaskList()
    .focus(null, { scrollIntoView: true })
    .run()
}

function focusEnd(): void {
  editor.value?.commands.focus('end')
}

defineExpose({ load, addChecklist, focusEnd })
</script>

<template>
  <div
    ref="root"
    class="description-editor"
    :class="{ 'is-editable': editable, 'is-active': active, 'has-toolbar': toolbarShown }"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
    @mouseenter="hovering = true"
    @mouseleave="hovering = false"
  >
    <EditorToolbar v-if="editable && editor" v-show="toolbarShown" :editor="editor" @link-open="(v: boolean) => (linkOpen = v)" />
    <EditorContent v-if="editor" :editor="editor" class="content" />
  </div>
</template>

<style scoped>
.description-editor {
  display: flex;
  flex-direction: column;
  margin: 0 calc(var(--space-2) * -1);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  transition:
    border-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out),
    background-color var(--dur-1) var(--ease-out);
}

.description-editor.is-editable:hover:not(.is-active) {
  background: var(--bg-hover);
}

.description-editor.is-active {
  border-color: var(--focus-ring);
  box-shadow: 0 0 0 1px var(--focus-ring);
  background: var(--bg-surface);
}

.content {
  min-width: 0;
}

.content :deep(.ProseMirror) {
  min-height: 22px;
  padding: var(--space-1-5) var(--space-2);
  border-radius: var(--radius-md);
  outline: none;
  white-space: pre-wrap;
  word-wrap: break-word;
}

.is-editable .content :deep(.ProseMirror) {
  min-height: 66px;
  cursor: text;
}

/* Read-only, the empty paragraph the editor keeps after a final list or heading is just a gap. */
.content :deep(.ProseMirror[contenteditable="false"] > p:last-child:has(> br.ProseMirror-trailingBreak:only-child)) {
  display: none;
}

.content :deep(.ProseMirror p.is-editor-empty:first-child::before) {
  content: attr(data-placeholder);
  float: left;
  height: 0;
  color: var(--text-tertiary);
  pointer-events: none;
}

.content :deep(.ProseMirror li[data-type="taskItem"] > label) {
  cursor: pointer;
}

/* The checklist box stays 16px, but its target is 24px (WCAG 2.5.8: the editor around it is a
   target too, so spacing can't make up for it): a 4px transparent border, pulled back by margin. */
.content :deep(.ProseMirror input[type="checkbox"]) {
  box-sizing: border-box;
  width: 24px;
  height: 24px;
  margin: -4px;
  border: 4px solid transparent;
  border-radius: calc(var(--radius-xs) + 4px);
  background-clip: padding-box;
  box-shadow: inset 0 0 0 1.5px var(--border-control);
}

.content :deep(.ProseMirror input[type="checkbox"]:checked) {
  border-color: transparent;
  box-shadow: inset 0 0 0 1.5px var(--accent);
}

.content :deep(.ProseMirror input[type="checkbox"]:focus-visible) {
  outline-offset: -2px;
}

.content :deep(.ProseMirror-selectednode) {
  outline: 2px solid var(--focus-ring);
  outline-offset: 1px;
}
</style>

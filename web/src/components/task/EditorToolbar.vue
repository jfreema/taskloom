<script setup lang="ts">
// The description toolbar (8.10): Bold, Italic, Bulleted list, Numbered list, Checklist and Link.
// Buttons keep the editor's selection (mousedown.prevent) and show aria-pressed for what is active
// at the caret. Link opens a small popover with a URL field: Enter applies it, an empty URL removes
// the link.
import { computed, ref, watch, type Component } from 'vue'
import { Bold, Italic, Link as LinkIcon, List, ListChecks, ListOrdered } from '@lucide/vue'
import type { Editor } from '@tiptap/vue-3'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInput from '../ui/UiInput.vue'
import UiPopover from '../ui/UiPopover.vue'
import type { PopoverCloseReason } from '../ui/popover'

const props = defineProps<{ editor: Editor }>()
const emit = defineEmits<{ 'link-open': [open: boolean] }>()

interface ToolButton {
  key: string
  label: string
  icon: Component
  kbd?: string
  active: boolean
  run: () => void
}

const buttons = computed<ToolButton[]>(() => {
  const e = props.editor
  return [
    { key: 'bold', label: 'Bold', icon: Bold, kbd: 'Mod+B', active: e.isActive('bold'), run: () => e.chain().focus().toggleBold().run() },
    { key: 'italic', label: 'Italic', icon: Italic, kbd: 'Mod+I', active: e.isActive('italic'), run: () => e.chain().focus().toggleItalic().run() },
    {
      key: 'bullets',
      label: 'Bulleted list',
      icon: List,
      active: e.isActive('bulletList'),
      run: () => e.chain().focus().toggleBulletList().run(),
    },
    {
      key: 'numbers',
      label: 'Numbered list',
      icon: ListOrdered,
      active: e.isActive('orderedList'),
      run: () => e.chain().focus().toggleOrderedList().run(),
    },
    { key: 'checklist', label: 'Checklist', icon: ListChecks, active: e.isActive('taskList'), run: () => e.chain().focus().toggleTaskList().run() },
  ]
})

const linkOpen = ref(false)
const url = ref('')
const linkActive = computed(() => props.editor.isActive('link'))

watch(linkOpen, (open) => {
  if (open) {
    const href: unknown = props.editor.getAttributes('link').href
    url.value = typeof href === 'string' ? href : ''
  }
  emit('link-open', open)
})

/** "example.com" becomes https://example.com and "ana@example.com" a mailto link; other schemes are refused. */
function normalize(raw: string): string | null {
  const s = raw.trim()
  if (!s) return ''
  if (/^(https?:\/\/|mailto:)/i.test(s)) return s
  if (/^[a-z][a-z0-9+.-]*:/i.test(s)) return null
  if (/^[^\s@/]+@[^\s@/]+\.[^\s@/]+$/.test(s)) return `mailto:${s}`
  return `https://${s.replace(/^\/+/, '')}`
}

function apply(): void {
  const href = normalize(url.value)
  if (href === null) return
  const e = props.editor
  linkOpen.value = false
  if (!href) {
    e.chain().focus().extendMarkRange('link').unsetLink().run()
    return
  }
  if (e.state.selection.empty && !e.isActive('link')) {
    e.chain().focus().insertContent({ type: 'text', text: href, marks: [{ type: 'link', attrs: { href } }] }).run()
    return
  }
  e.chain().focus().extendMarkRange('link').setLink({ href }).run()
}

function onLinkClose(reason: PopoverCloseReason): void {
  // Esc goes back to the text; a click elsewhere leaves focus where it went.
  if (reason === 'escape') props.editor.commands.focus()
}
</script>

<template>
  <div class="editor-toolbar" role="group" aria-label="Formatting">
    <UiIconButton
      v-for="b in buttons"
      :key="b.key"
      :icon="b.icon"
      :label="b.label"
      :kbd="b.kbd"
      :pressed="b.active"
      size="sm"
      @mousedown.prevent
      @click="b.run"
    />
    <UiPopover v-model:open="linkOpen" :return-focus="false" aria-label="Link" :width="300" @close="onLinkClose">
      <template #trigger="{ props: t }">
        <UiIconButton v-bind="t" :icon="LinkIcon" label="Link" :pressed="linkActive" size="sm" @mousedown.prevent />
      </template>
      <div class="link-form">
        <UiInput v-model="url" type="url" size="sm" placeholder="Paste or type a link" aria-label="Link URL" @enter.prevent="apply" />
        <p class="hint">Enter to apply. Leave it empty to remove the link.</p>
      </div>
    </UiPopover>
  </div>
</template>

<style scoped>
.editor-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-0-5);
  padding: var(--space-1);
  border-bottom: 1px solid var(--border-subtle);
}

.link-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  padding: var(--space-2);
}

.hint {
  margin: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}
</style>

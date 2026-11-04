<script setup lang="ts">
// A comment in the activity feed (8.10): avatar, name, time, "(edited)", and the body as plain
// text with clickable links (linkify, never v-html). The author's own comments reveal a "..." menu:
// Edit (in place; Enter saves, Esc cancels) and Delete (with Undo).
import { computed, nextTick, ref } from 'vue'
import { Ellipsis, Pencil, Trash2 } from '@lucide/vue'
import { linkify } from '../../lib/html'
import type { Comment } from '../../types'
import UiAvatar from '../ui/UiAvatar.vue'
import UiButton from '../ui/UiButton.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiMenu from '../ui/UiMenu.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import type { MenuItem } from '../ui/menu'
import RelativeTime from './RelativeTime.vue'
import { MAX_COMMENT_LENGTH, useComments } from './useComments'

const props = defineProps<{ comment: Comment; mine: boolean; sending: boolean }>()

const comments = useComments()
const root = ref<HTMLElement | null>(null)
const editor = ref<InstanceType<typeof UiTextarea> | null>(null)
const editing = ref(false)
const draft = ref('')

const segments = computed(() => linkify(props.comment.body))

const items = computed<MenuItem[]>(() => [
  { id: 'edit', label: 'Edit', icon: Pencil, onSelect: () => void startEditing() },
  { kind: 'separator', id: 'sep' },
  { id: 'delete', label: 'Delete', icon: Trash2, danger: true, onSelect: () => comments.remove(props.comment) },
])

async function startEditing(): Promise<void> {
  draft.value = props.comment.body
  editing.value = true
  await nextTick()
  editor.value?.focus()
}

async function stopEditing(): Promise<void> {
  editing.value = false
  await nextTick()
  root.value?.querySelector<HTMLElement>('.menu-trigger')?.focus()
}

function save(): void {
  const text = draft.value.trim()
  if (!text) return
  comments.edit(props.comment, text)
  void stopEditing()
}

function onEscape(e: KeyboardEvent): void {
  // Esc cancels the edit only; the panel stays open.
  e.preventDefault()
  void stopEditing()
}
</script>

<template>
  <li ref="root" class="comment-item" data-row :class="{ 'is-sending': sending }" :aria-busy="sending ? 'true' : undefined">
    <UiAvatar class="avatar" :user="comment.author" :size="28" decorative />
    <div class="main">
      <div class="meta">
        <span class="name truncate">{{ comment.author.displayName }}</span>
        <RelativeTime class="time" :at="comment.createdAt" />
        <span v-if="comment.editedAt" class="edited">(edited)</span>
        <UiMenu v-if="mine && !sending && !editing" :items="items" placement="bottom-end" aria-label="Comment actions">
          <template #trigger="{ props: t }">
            <UiIconButton v-bind="t" class="menu-trigger reveal reveal-touch" :icon="Ellipsis" label="Comment actions" size="xs" />
          </template>
        </UiMenu>
      </div>
      <div v-if="editing" class="edit">
        <UiTextarea
          ref="editor"
          v-model="draft"
          :rows="1"
          :max-rows="8"
          submit-on="enter"
          :maxlength="MAX_COMMENT_LENGTH"
          aria-label="Edit comment"
          @submit="save"
          @escape="onEscape"
        />
        <div class="edit-actions">
          <UiButton variant="ghost" size="sm" @click="stopEditing">Cancel</UiButton>
          <UiButton variant="primary" size="sm" :disabled="!draft.trim()" @click="save">Save</UiButton>
        </div>
      </div>
      <p v-else class="body">
        <template v-for="(s, i) in segments" :key="i">
          <a v-if="s.href" class="link" :href="s.href" target="_blank" rel="noopener noreferrer nofollow">{{ s.text }}</a>
          <template v-else>{{ s.text }}</template>
        </template>
      </p>
    </div>
  </li>
</template>

<style scoped>
.comment-item {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  padding: var(--space-1-5) 0;
}

.avatar {
  flex-shrink: 0;
}

.main {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  flex: 1;
  min-width: 0;
}

.meta {
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  min-height: var(--control-xs);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.name {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.menu-trigger {
  margin-left: auto;
}

.body {
  margin: 0;
  font-size: var(--fs-md);
  line-height: 22px;
  color: var(--text-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.is-sending .body {
  color: var(--text-secondary);
}

.link {
  color: var(--accent-text);
  text-decoration: underline;
  text-underline-offset: 2px;
  text-decoration-color: color-mix(in srgb, currentColor 40%, transparent);
}

.link:hover {
  text-decoration-color: currentColor;
}

.edit {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin-top: var(--space-1);
}

.edit-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
}
</style>

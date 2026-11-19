<script setup lang="ts">
// The comment composer (8.10), sticky at the bottom of the panel or page; guests can comment too.
// Enter sends, Shift+Enter adds a line; "@" lists the people to mention (MentionTextarea). Sending
// is optimistic (useComments). The unsent text and its mentions are kept per task, so J and K or
// closing the panel don't lose them.
import { computed } from 'vue'
import { encodeMentions, type MentionPicks } from '../../lib/mentions'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import type { Uuid } from '../../types'
import UiAvatar from '../ui/UiAvatar.vue'
import UiButton from '../ui/UiButton.vue'
import MentionTextarea from './MentionTextarea.vue'
import { detailState } from './taskDetailState'
import { MAX_COMMENT_LENGTH, useComments } from './useComments'

const props = withDefaults(defineProps<{ taskId: Uuid; layout?: 'panel' | 'page' }>(), { layout: 'panel' })

const session = useSession()
const tasks = useTasks()
const comments = useComments()

const draft = computed<string>({
  get: () => detailState.drafts.get(props.taskId) ?? '',
  set: (v) => {
    if (v) detailState.drafts.set(props.taskId, v)
    else detailState.drafts.delete(props.taskId)
  },
})
const picks = computed<MentionPicks>({
  get: () => detailState.draftMentions.get(props.taskId) ?? {},
  set: (v) => detailState.draftMentions.set(props.taskId, v),
})
const empty = computed(() => !draft.value.trim())

function send(): void {
  const text = draft.value.trim()
  if (!text) return
  const { body } = encodeMentions(text, picks.value)
  draft.value = ''
  detailState.draftMentions.delete(props.taskId)
  comments.send(props.taskId, body)
}
</script>

<template>
  <div class="comment-composer" :class="`layout-${layout}`">
    <div class="inner">
      <UiAvatar class="avatar" :user="session.user" :size="28" decorative />
      <div class="field">
        <MentionTextarea
          v-model="draft"
          v-model:picks="picks"
          :list-id="tasks.get(taskId)?.listId"
          placeholder="Write a comment…"
          label="Write a comment"
          :maxlength="MAX_COMMENT_LENGTH"
          @submit="send"
        />
        <p class="hint">Enter to send, Shift+Enter for a new line, @ to mention someone</p>
      </div>
      <UiButton class="send" variant="primary" size="sm" :disabled="empty" @click="send">Send</UiButton>
    </div>
  </div>
</template>

<style scoped>
.comment-composer {
  flex-shrink: 0;
  padding: var(--space-3) var(--space-6) calc(var(--space-3) + env(safe-area-inset-bottom));
  border-top: 1px solid var(--border-subtle);
  background: inherit;
}

.inner {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
}

.layout-page .inner {
  max-width: calc(var(--reading-w) - 2 * var(--space-6));
  margin: 0 auto;
}

.avatar {
  flex-shrink: 0;
  margin-top: 2px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  flex: 1;
  min-width: 0;
}

.hint {
  margin: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.send {
  flex-shrink: 0;
  margin-top: 2px;
}

@media (max-width: 639px) {
  .comment-composer {
    padding-left: var(--space-4);
    padding-right: var(--space-4);
  }

  .hint {
    display: none;
  }
}
</style>

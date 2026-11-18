<script setup lang="ts">
// The parts quick add recognised in a title (Look 1), one chip each: the due date, the priority, a tag
// (marked "new" when it will be created) or a person. Each chip's × keeps that text in the title instead.
// A click on × leaves focus in the title input; the parent puts focus back there after a keyboard ×
// too (the chip goes away). A polite live region says what was recognised.
import { computed } from 'vue'
import { Calendar, X } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { formatDateLong, formatDue } from '../../lib/dates'
import type { QuickToken } from '../../lib/quickAdd'
import { useMembers } from '../../stores/members'
import { useTags } from '../../stores/tags'
import { PRIORITY_LABEL } from '../../types'
import PriorityFlag from '../fields/PriorityFlag.vue'
import UiAvatar from '../ui/UiAvatar.vue'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(defineProps<{ tokens: readonly QuickToken[]; size?: 'sm' | 'md' }>(), { size: 'md' })

const emit = defineEmits<{ dismiss: [token: QuickToken] }>()

const today = useToday()
const tags = useTags()
const members = useMembers()

interface Chip {
  token: QuickToken
  text: string
  /** What the chip means, for the live region and the tooltip. */
  spoken: string
  color?: string
}

const chips = computed<Chip[]>(() =>
  props.tokens.map((t) => {
    if (t.kind === 'date' && t.date) {
      const year = t.date.slice(0, 4) === today.value.slice(0, 4) ? '' : `, ${t.date.slice(0, 4)}`
      return { token: t, text: formatDue(t.date, today.value), spoken: `Due ${formatDateLong(t.date)}${year}` }
    }
    if (t.kind === 'priority' && t.priority) {
      return { token: t, text: PRIORITY_LABEL[t.priority], spoken: `${PRIORITY_LABEL[t.priority]} priority` }
    }
    if (t.kind === 'tag') {
      const tag = t.tagId ? tags.get(t.tagId) : undefined
      const name = tag?.name ?? t.newTag ?? t.text.slice(1)
      return {
        token: t,
        text: name,
        spoken: tag ? `Tag ${name}` : `New tag ${name}`,
        color: tag?.color,
      }
    }
    const name = t.userId ? members.nameOf(t.userId) : t.text.slice(1)
    return { token: t, text: name, spoken: `Assign ${name}` }
  }),
)

const summary = computed(() => (chips.value.length ? `Recognised: ${chips.value.map((c) => c.spoken).join(', ')}` : ''))
</script>

<template>
  <span class="quick-add-chips" :class="`quick-add-chips--${size}`">
    <span v-for="c in chips" :key="`${c.token.kind}:${c.token.start}`" v-tooltip="c.spoken" class="chip" :data-kind="c.token.kind">
      <UiIcon v-if="c.token.kind === 'date'" :icon="Calendar" :size="14" class="lead" />
      <PriorityFlag v-else-if="c.token.kind === 'priority' && c.token.priority" :priority="c.token.priority" :size="14" decorative />
      <span v-else-if="c.token.kind === 'tag'" class="dot" :class="{ 'is-new': !c.color }" :style="c.color ? { background: c.color } : undefined" />
      <UiAvatar v-else-if="c.token.userId" :user="{ id: c.token.userId, displayName: c.text }" :size="16" decorative />
      <span class="text">{{ c.text }}</span>
      <span v-if="c.token.kind === 'tag' && !c.color" class="new">new</span>
      <button
        type="button"
        class="undo"
        :aria-label="`Keep “${c.token.text}” as text`"
        @mousedown.prevent
        @click="emit('dismiss', c.token)"
      >
        <UiIcon :icon="X" :size="12" />
      </button>
    </span>
    <span class="sr-only" aria-live="polite">{{ summary }}</span>
  </span>
</template>

<style scoped>
.quick-add-chips {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  max-width: 180px;
  height: 22px;
  padding: 0 2px 0 var(--space-1-5);
  border: 1px solid color-mix(in srgb, var(--accent) 35%, var(--border-default));
  border-radius: var(--radius-full);
  background: var(--accent-subtle);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  white-space: nowrap;
}

.quick-add-chips--sm .chip {
  height: 20px;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.lead {
  flex: none;
  color: var(--accent-text);
}

.dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.dot.is-new {
  border: 1.5px dashed var(--text-tertiary);
}

.text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

.new {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.undo {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  color: var(--text-tertiary);
  cursor: pointer;
}

.undo:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

/* Touch (7.5): the × reaches a 32px target beyond its chip. */
@media (pointer: coarse) {
  .undo {
    position: relative;
  }

  .undo::after {
    content: "";
    position: absolute;
    inset: -7px;
  }
}
</style>

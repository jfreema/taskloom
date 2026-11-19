<script setup lang="ts">
// A comment box with the @ list (Features 2). Typing "@" and the start of a name lists the people who
// can see the task (everyone in the workspace, or the people of its private space), never yourself.
// Focus stays in the text: Up and Down choose, Enter or Tab mentions, Esc closes the list only. The
// text shows "@Name"; `picks` remembers who each name stands for, and lib/mentions.ts turns them
// into the tokens the server stores. The list floats above or below the box, outside any scroller.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { autoUpdate, flip, offset, shift, size, useFloating } from '@floating-ui/vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { pluralize } from '../../lib/format'
import { matchMentions, mentionQueryAt, type MentionPicks, type MentionQuery } from '../../lib/mentions'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import { useWorkspaces } from '../../stores/workspaces'
import type { Member, Uuid } from '../../types'
import UiAvatar from '../ui/UiAvatar.vue'
import UiTextarea from '../ui/UiTextarea.vue'

const text = defineModel<string>({ required: true })
const picks = defineModel<MentionPicks>('picks', { required: true })

const props = withDefaults(
  defineProps<{
    /** The task's list: in a private space only its people can be mentioned. */
    listId: Uuid | null | undefined
    /** Where the list opens when there is room. */
    placement?: 'top' | 'bottom'
    rows?: number
    maxRows?: number
    placeholder?: string
    maxlength?: number
    /** Names the text box for screen readers. */
    label: string
  }>(),
  { placement: 'top', rows: 1, maxRows: 8, placeholder: undefined, maxlength: undefined },
)

const emit = defineEmits<{ submit: [e: KeyboardEvent]; escape: [e: KeyboardEvent] }>()

const members = useMembers()
const workspaces = useWorkspaces()
const session = useSession()
const announce = useAnnouncer()

const uid = useId()
const listId = `${uid}-mentions`
const wrap = ref<HTMLElement | null>(null)
const field = ref<InstanceType<typeof UiTextarea> | null>(null)
const list = ref<HTMLElement | null>(null)

const people = computed<Member[]>(() => {
  const only = props.listId ? workspaces.assignableIn([props.listId]) : null
  const me = session.user?.id
  return members.current.filter((m) => m.userId !== me && !m.disabled && (!only || only.has(m.userId)))
})

/** The "@…" being typed at the caret, or null. */
const query = ref<MentionQuery | null>(null)
/** The "@" the list was closed for (a pick, or Esc): it stays closed until another one is typed. */
let closedAt: number | null = null
const matches = computed<Member[]>(() => (query.value ? matchMentions(people.value, query.value.query) : []))
const open = computed(() => matches.value.length > 0)
const active = ref(0)
const activeId = computed(() => (open.value ? `${listId}-${active.value}` : undefined))

watch(
  () => matches.value.map((m) => m.userId).join(' '),
  () => {
    active.value = 0
  },
)

watch(open, (now, before) => {
  if (now && !before) announce(`${pluralize(matches.value.length, 'person', 'people')} to mention. Up and Down to choose, Enter to mention.`)
})

function textarea(): HTMLTextAreaElement | null {
  return field.value?.el ?? null
}

function close(): void {
  query.value = null
}

/** Reads the caret and opens, updates or closes the list. */
function sync(): void {
  const el = textarea()
  if (!el || document.activeElement !== el || el.selectionStart !== el.selectionEnd) return close()
  const q = mentionQueryAt(el.value, el.selectionStart)
  if (!q) {
    closedAt = null
    return close()
  }
  if (q.start === closedAt) return close()
  closedAt = null
  query.value = q
}

// The text changed from outside (sent, another task, an edit cancelled): nothing is being typed.
watch(text, () => void nextTick(sync))

function pick(person: Member): void {
  const q = query.value
  const el = textarea()
  if (!q || !el) return
  const name = `@${person.displayName}`
  const before = text.value.slice(0, q.start)
  const after = text.value.slice(q.start + 1 + q.query.length)
  const gap = /^\s/.test(after) ? '' : ' '
  text.value = before + name + gap + after
  picks.value = { ...picks.value, [name]: person.userId }
  closedAt = q.start
  close()
  const caret = before.length + name.length + 1
  void nextTick(() => {
    el.focus()
    el.setSelectionRange(caret, caret)
  })
}

// Before the textarea's own keys (Enter sends, Esc cancels): while the list shows, they are the list's.
function onKeydown(e: KeyboardEvent): void {
  if (!open.value || e.isComposing || e.keyCode === 229 || e.altKey || e.ctrlKey || e.metaKey) return
  const count = matches.value.length
  if (e.key === 'ArrowDown') active.value = (active.value + 1) % count
  else if (e.key === 'ArrowUp') active.value = (active.value - 1 + count) % count
  else if ((e.key === 'Enter' || e.key === 'Tab') && !e.shiftKey) pick(matches.value[active.value] ?? matches.value[0])
  else if (e.key === 'Escape') {
    closedAt = query.value?.start ?? null
    close()
  } else return
  e.preventDefault()
  e.stopPropagation()
}

function onKeyup(e: KeyboardEvent): void {
  // The caret moved without the text changing.
  if (e.key === 'ArrowLeft' || e.key === 'ArrowRight' || e.key === 'Home' || e.key === 'End') sync()
}

function onFocusOut(e: FocusEvent): void {
  if (e.relatedTarget instanceof Node && list.value?.contains(e.relatedTarget)) return
  close()
}

watch(active, () => {
  void nextTick(() => list.value?.querySelector('.is-active')?.scrollIntoView({ block: 'nearest' }))
})

const { floatingStyles } = useFloating(wrap, list, {
  placement: () => (props.placement === 'top' ? 'top-start' : 'bottom-start'),
  strategy: 'fixed',
  transform: false,
  middleware: [
    offset(4),
    flip({ padding: 8 }),
    shift({ padding: 8 }),
    size({
      padding: 8,
      apply({ availableHeight, elements }) {
        elements.floating.style.setProperty('--mentions-max-h', `${Math.max(0, Math.floor(availableHeight))}px`)
      },
    }),
  ],
  whileElementsMounted: autoUpdate,
})

function focus(): void {
  field.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <div ref="wrap" class="mention-textarea" @keydown.capture="onKeydown" @keyup="onKeyup" @input="sync" @click="sync" @focusout="onFocusOut">
    <UiTextarea
      ref="field"
      v-model="text"
      :rows="rows"
      :max-rows="maxRows"
      submit-on="enter"
      :placeholder="placeholder"
      :maxlength="maxlength"
      :aria-label="label"
      aria-autocomplete="list"
      aria-haspopup="listbox"
      :aria-controls="open ? listId : undefined"
      :aria-activedescendant="activeId"
      @submit="emit('submit', $event)"
      @escape="emit('escape', $event)"
    />
    <Teleport to="body">
      <ul
        v-if="open"
        :id="listId"
        ref="list"
        class="mention-list"
        role="listbox"
        aria-label="People to mention"
        data-layer-allow
        :style="floatingStyles"
        @pointerdown.prevent
      >
        <li
          v-for="(m, i) in matches"
          :id="`${listId}-${i}`"
          :key="m.userId"
          class="mention-option"
          :class="{ 'is-active': i === active }"
          role="option"
          :aria-selected="i === active ? 'true' : 'false'"
          @pointermove="active = i"
          @click="pick(m)"
        >
          <UiAvatar :user="{ id: m.userId, displayName: m.displayName, photoUrl: m.photoUrl }" :size="20" :pending="m.pending" decorative />
          <span class="mention-name">{{ m.displayName }}</span>
          <span class="mention-email">{{ m.email }}</span>
        </li>
      </ul>
    </Teleport>
  </div>
</template>

<style scoped>
.mention-textarea {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.mention-list {
  z-index: var(--z-popover);
  width: max-content;
  min-width: 220px;
  max-width: min(360px, calc(100vw - 16px));
  max-height: min(var(--mentions-max-h, 280px), 280px);
  margin: 0;
  padding: var(--space-1);
  overflow-y: auto;
  overscroll-behavior: contain;
  list-style: none;
  border: 1px solid var(--border-floating);
  border-radius: var(--radius-md);
  background: var(--bg-raised);
  box-shadow: var(--shadow-3);
  color: var(--text-primary);
}

.mention-option {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-menu);
  padding: var(--space-1) var(--space-2);
  border-radius: var(--radius-sm);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  cursor: pointer;
  user-select: none;
  -webkit-tap-highlight-color: transparent;
}

.mention-option.is-active {
  background: var(--bg-hover);
}

.mention-name {
  flex-shrink: 0;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mention-email {
  min-width: 0;
  margin-left: auto;
  padding-left: var(--space-2);
  overflow: hidden;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (pointer: coarse) {
  .mention-option {
    min-height: var(--touch-min);
  }
}
</style>

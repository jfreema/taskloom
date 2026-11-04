<script setup lang="ts">
// Home's quick add (8.5, Member+): "Add a task…" with one chip, the list (ListPicker), then a muted
// "due today". Enter creates { title, dueDate: today, assigneeIds: [me] under "Assigned to me" }
// in the list's default status, so the task always lands in Today; the input keeps focus, there is
// no toast and no paste handling (the single-line input joins pasted lines). Esc clears it.
// Below 640px of page width the chip moves under the input and shows only while quick add has focus.
import { computed, ref, watch } from 'vue'
import { Plus } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { newId } from '../../api'
import { useRecents } from '../../stores/recents'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import ListPicker from '../fields/ListPicker.vue'
import UiInput from '../ui/UiInput.vue'
import UiKbd from '../ui/UiKbd.vue'

const props = withDefaults(
  defineProps<{
    /** "Assigned to me" is the scope: new tasks are assigned to me so they stay in view. */
    assignToMe?: boolean
    /** The first-run hint under the input. */
    hint?: boolean
  }>(),
  { assignToMe: false, hint: false },
)

const emit = defineEmits<{ created: [id: Uuid] }>()

const tasks = useTasks()
const workspaces = useWorkspaces()
const recents = useRecents()
const session = useSession()
const today = useToday()

const title = ref('')
const chosen = ref<Uuid | null>(null)
const pickerOpen = ref(false)
const focusWithin = ref(false)
const root = ref<HTMLElement | null>(null)
const input = ref<InstanceType<typeof UiInput> | null>(null)

const listIds = computed(() => workspaces.allLists.map((x) => x.list.id))

/** The last list used, else the first list in sidebar order. */
const fallbackList = computed<Uuid | null>(() => {
  const last = recents.lastListId
  if (last && listIds.value.includes(last)) return last
  return listIds.value[0] ?? null
})

const listId = computed<Uuid | null>({
  get: () => (chosen.value && listIds.value.includes(chosen.value) ? chosen.value : fallbackList.value),
  set: (v) => {
    chosen.value = v
  },
})

// Another workspace: start again from its last list.
watch(() => workspaces.currentId, () => {
  chosen.value = null
})

const open = computed(() => focusWithin.value || pickerOpen.value)

function submit(): void {
  const text = title.value.trim()
  const target = listId.value
  if (!text || !target) return
  const me = session.user?.id
  const id = newId()
  void tasks.create(target, {
    id,
    title: text,
    dueDate: today.value,
    assigneeIds: props.assignToMe && me ? [me] : [],
  })
  recents.setLastList(target)
  title.value = ''
  emit('created', id)
}

function clear(e: KeyboardEvent): void {
  if (!title.value) return
  // Esc empties the input; it doesn't close anything else.
  e.preventDefault()
  title.value = ''
}

function onFocusOut(e: FocusEvent): void {
  const next = e.relatedTarget
  if (!(next instanceof Node) || !root.value?.contains(next)) focusWithin.value = false
}

function focus(): void {
  input.value?.focus()
}

defineExpose({ focus })
</script>

<template>
  <div ref="root" class="home-quick-add" :class="{ 'is-open': open }" @focusin="focusWithin = true" @focusout="onFocusOut">
    <UiInput
      ref="input"
      v-model="title"
      class="box"
      size="lg"
      :icon="Plus"
      placeholder="Add a task…"
      aria-label="Add a task"
      :maxlength="500"
      autocomplete="off"
      enterkeyhint="done"
      @enter="submit"
      @escape="clear"
    >
      <template #suffix>
        <span class="extras">
          <ListPicker v-model="listId" v-model:open="pickerOpen" variant="chip" />
          <span class="due-hint">due today</span>
        </span>
        <span class="kbd" aria-hidden="true"><UiKbd keys="Enter" /></span>
      </template>
    </UiInput>
    <p v-if="hint" class="hint">Enter to add · <UiKbd keys="N" /> from anywhere</p>
  </div>
</template>

<style scoped>
.home-quick-add {
  position: relative;
}

.box {
  padding-right: var(--space-2);
}

.extras {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}

.due-hint {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  white-space: nowrap;
}

.kbd {
  display: inline-flex;
  margin-left: var(--space-2);
}

.hint {
  margin-top: var(--space-2);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

/* Narrow pages: one input; the list chip sits under it while quick add has focus. */
@container page (max-width: 639px) {
  .is-open .box {
    margin-bottom: calc(var(--control-sm) + var(--space-2));
  }

  .extras {
    display: none;
    position: absolute;
    top: calc(100% + var(--space-2));
    left: 0;
    max-width: 100%;
  }

  .is-open .extras {
    display: inline-flex;
  }

  .kbd {
    display: none;
  }
}
</style>

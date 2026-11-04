<script setup lang="ts">
// "Move N tasks" (7.11): mounted once by the shell and opened by ui.openMoveTasks(ids) with
// top-level task ids. A ListPicker field (leaving out the tasks' own list when they share one)
// with the hint about subtasks and statuses; "Move" calls tasks.moveToList, which shows the undo
// toast and removes the moved ids from the selection. Picking a list moves focus to "Move".
import { computed, nextTick, ref, shallowRef, watch, type ComponentPublicInstance } from 'vue'
import { pluralize } from '../../lib/format'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import type { Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import ListPicker from './ListPicker.vue'

const ui = useUi()
const tasks = useTasks()

const open = ref(false)
/** A copy taken on open, so the title doesn't change while the dialog fades out. */
const ids = ref<Uuid[]>([])
const listId = ref<Uuid | null>(null)
const moveButton = shallowRef<ComponentPublicInstance | null>(null)

watch(
  () => ui.moveTasks,
  (m) => {
    if (m) {
      ids.value = [...m.taskIds]
      listId.value = null
      open.value = true
    } else {
      open.value = false
    }
  },
  { immediate: true },
)

watch(open, (on) => {
  if (!on && ui.moveTasks) ui.moveTasks = null
})

const title = computed(() => `Move ${pluralize(ids.value.length, 'task')}`)

/** The tasks' list when they all share one: there is no point offering it. */
const sharedListId = computed<Uuid | null>(() => {
  const lists = new Set<Uuid>()
  for (const id of ids.value) {
    const t = tasks.get(id)
    if (t) lists.add(t.listId)
  }
  return lists.size === 1 ? [...lists][0] : null
})

function pick(id: Uuid | null): void {
  listId.value = id
  if (!id) return
  // The picker returns focus to its trigger as it closes; then focus moves on to "Move".
  void nextTick(() => {
    requestAnimationFrame(() => {
      const el: unknown = moveButton.value?.$el
      if (el instanceof HTMLElement) el.focus()
    })
  })
}

function move(): void {
  const target = listId.value
  if (!target || !ids.value.length) return
  void tasks.moveToList([...ids.value], target).catch(() => undefined)
  open.value = false
}
</script>

<template>
  <UiDialog v-model:open="open" :title="title" size="sm">
    <UiField label="List" hint="Subtasks move with their parent. Statuses are matched by name.">
      <template #default="{ id, describedBy }">
        <ListPicker
          :id="id"
          :aria-describedby="describedBy"
          :model-value="listId"
          variant="field"
          :exclude-list-id="sharedListId"
          @update:model-value="pick"
        />
      </template>
    </UiField>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton ref="moveButton" variant="primary" :disabled="!listId" @click="move">Move</UiButton>
    </template>
  </UiDialog>
</template>

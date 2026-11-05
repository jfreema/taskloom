<script setup lang="ts">
// Quick create (8.12): UiDialog md "New task" (a full-height sheet on phones) with the title, a
// plain-text description and the chips List, Status, Assignees, Due date, Priority and Tags, then
// Create more, Cancel and "Create task". Enter in the title creates; Mod+Enter anywhere in the
// dialog creates and opens the task; Enter in the description is a line break; Esc discards.
// Pasting two or more lines into the title offers to create one task per line.
import { computed, ref, watch } from 'vue'
import { useShortcut } from '../../composables/useShortcut'
import { useUi } from '../../stores/ui'
import AssigneePicker from '../fields/AssigneePicker.vue'
import DateField from '../fields/DateField.vue'
import ListPicker from '../fields/ListPicker.vue'
import PriorityPicker from '../fields/PriorityPicker.vue'
import StatusPicker from '../fields/StatusPicker.vue'
import TagPicker from '../fields/TagPicker.vue'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiInput from '../ui/UiInput.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { useQuickCreate } from './useQuickCreate'

const ui = useUi()

const open = computed({
  get: () => ui.quickCreate.open,
  set: (v: boolean) => {
    if (!v) ui.closeQuickCreate()
  },
})

const titleInput = ref<InstanceType<typeof UiInput> | null>(null)
const content = ref<HTMLElement | null>(null)
/** The dialog panel: Mod+Enter works anywhere in it, footer included, but not in its pickers' popovers. */
const panelEl = computed(() => content.value?.closest<HTMLElement>('[role="dialog"]') ?? null)

const qc = useQuickCreate({ focusTitle: () => titleInput.value?.focus() })
const { title, description, listId, statusId, assigneeIds, tagIds, priority, dueDate, startDate, more, canCreate } = qc

// Every opening starts from the defaults (no saved draft).
watch(open, (on) => {
  if (on) qc.reset()
}, { immediate: true })

function onTitleEnter(e: KeyboardEvent): void {
  // Mod+Enter is "create and open" (the shortcut below); other modified Enters do nothing here.
  if (e.metaKey || e.ctrlKey || e.altKey || e.shiftKey) return
  e.preventDefault()
  qc.create({ open: false })
}

function onPaste(e: ClipboardEvent): void {
  const text = e.clipboardData?.getData('text/plain') ?? ''
  if (qc.pasteLines(text)) e.preventDefault()
}

function onOtherDate(v: string | null): void {
  startDate.value = v
}

useShortcut(
  'Mod+Enter',
  () => {
    if (!open.value || !canCreate.value) return false
    qc.create({ open: true })
  },
  { scope: 'layer', el: panelEl, allowInInputs: true },
)
</script>

<template>
  <UiDialog v-model:open="open" title="New task" size="md" sheet="full" initial-focus="[data-quick-create-title]">
    <div ref="content" class="quick-create">
      <UiInput
        ref="titleInput"
        v-model="title"
        class="title"
        size="lg"
        placeholder="Task title"
        aria-label="Task title"
        data-quick-create-title
        :maxlength="500"
        autocomplete="off"
        enterkeyhint="done"
        @enter="onTitleEnter"
        @paste="onPaste"
      />
      <UiTextarea v-model="description" class="description" :rows="2" :max-rows="8" placeholder="Add a description" aria-label="Description" />
      <div class="chips">
        <ListPicker v-model="listId" variant="chip" />
        <StatusPicker v-if="listId && statusId" v-model="statusId" :list-id="listId" variant="chip" />
        <AssigneePicker v-model="assigneeIds" variant="chip" />
        <DateField v-model="dueDate" kind="due" variant="chip" :other-date="startDate" @update:other-date="onOtherDate" />
        <PriorityPicker v-model="priority" variant="chip" />
        <TagPicker v-model="tagIds" variant="chip" />
      </div>
    </div>
    <template #footer>
      <UiSwitch v-model="more" class="more" label="Create more" />
      <UiButton @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" kbd="Enter" :disabled="!canCreate" @click="qc.create({ open: false })">Create task</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.quick-create {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.chips {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
}

.more {
  align-self: center;
  margin-right: auto;
}

/* Touch (7.5): wrapped chip rows sit 16px apart, so the 28px chips keep separate 44px hit areas. */
@media (pointer: coarse) {
  .chips {
    row-gap: var(--space-4);
  }
}

/* Phones: the switch takes its own line above the two buttons. */
@media (max-width: 639px) {
  .more {
    flex: 1 0 100%;
    margin: 0 0 var(--space-1);
  }
}

/* Touch phones: 20px between the switch and the buttons, so their 44px hit areas do not overlap. */
@media (max-width: 639px) and (pointer: coarse) {
  .more {
    margin-bottom: var(--space-3);
  }
}
</style>

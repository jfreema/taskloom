<script setup lang="ts">
// Delete status (8.13), an alertdialog opened by ui.openDeleteStatus(statusId) from the status
// editor and the board's column menu. When the status has tasks, or the count is unknown because
// the list's tasks aren't loaded, it asks where they go: the other statuses, grouped by type, the
// first in display order preselected. "Delete status" calls workspaces.deleteStatus(id, moveTo),
// which is optimistic: the status goes, the loaded tasks move to the target (tasks.reassignStatus,
// called by the store) and everything comes back with a toast if the server refuses. The last Done
// status and the last status that isn't Done can't be deleted: the dialog says why instead.
import { computed, ref, watch, watchEffect } from 'vue'
import { pluralize } from '../../lib/format'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status, Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiSelect from '../ui/UiSelect.vue'
import type { ListboxOption } from '../ui/listbox'
import { deleteBlocked, sectionLabel } from './statusRules'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const tasks = useTasks()

const statusId = ref<Uuid | null>(null)
const listId = ref<Uuid | null>(null)
/** Kept while the dialog fades out, after the status is gone. */
const name = ref('')
const moveTo = ref<string | null>(null)

const status = computed<Status | undefined>(() => (statusId.value ? workspaces.statusById(statusId.value) : undefined))
const all = computed<readonly Status[]>(() => (listId.value ? workspaces.statusesFor(listId.value) : []))
const others = computed(() => all.value.filter((s) => s.id !== statusId.value))

const open = useRequestedDialog(
  () => ui.deleteStatus,
  () => {
    ui.deleteStatus = null
  },
  (r) => {
    const s = workspaces.statusById(r.statusId)
    statusId.value = r.statusId
    listId.value = s?.listId ?? null
    name.value = s?.name ?? ''
    moveTo.value = others.value[0]?.id ?? null
  },
)

// The status went away (deleted elsewhere), or the role was lost: close.
watch(
  () => {
    const s = status.value
    return !!s && workspaces.canEdit(workspaces.findList(s.listId)?.workspace.id)
  },
  (ok) => {
    if (!ok) open.value = false
  },
)

/** Tasks in the status, from the loaded tasks; null while the list isn't loaded. */
const count = computed<number | null>(() => {
  const id = listId.value
  if (!id || (tasks.listMeta[id]?.loadedAt ?? 0) === 0) return null
  return tasks.inList(id).filter((t) => t.statusId === statusId.value).length
})

const blocked = computed(() => (status.value ? deleteBlocked(status.value, all.value) : null))
const asksTarget = computed(() => !blocked.value && count.value !== 0 && others.value.length > 0)

const options = computed<ListboxOption[]>(() =>
  others.value.map((s) => ({ value: s.id, label: s.name, group: sectionLabel(s.category) })),
)

const fieldLabel = computed(() => (count.value === null ? 'Move its tasks to' : `Move its ${pluralize(count.value, 'task')} to`))

const description = computed(() => {
  if (blocked.value) return undefined
  return count.value === 0 ? "No tasks use this status. This can't be undone." : "This can't be undone."
})

/** What the dialog shows, frozen while it fades out (the status is gone by then). */
const view = ref<{ blocked: string | null; asksTarget: boolean; fieldLabel: string; description: string | undefined }>({
  blocked: null,
  asksTarget: false,
  fieldLabel: '',
  description: undefined,
})

watchEffect(() => {
  if (!open.value) return
  view.value = { blocked: blocked.value, asksTarget: asksTarget.value, fieldLabel: fieldLabel.value, description: description.value }
})

function confirmDelete(): void {
  const s = status.value
  if (!s || blocked.value) return
  const target = asksTarget.value ? (moveTo.value ?? undefined) : undefined
  // Optimistic; the store toasts and restores the status if the server refuses.
  void workspaces.deleteStatus(s.id, target).catch(() => undefined)
  open.value = false
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="`Delete “${name}”?`"
    :description="view.description"
    size="sm"
    role="alertdialog"
    :close-button="false"
    initial-focus="[data-delete-cancel]"
  >
    <template v-if="view.blocked || view.asksTarget" #default>
      <UiInlineAlert v-if="view.blocked" tone="warning">{{ view.blocked }}</UiInlineAlert>
      <UiField v-else :label="view.fieldLabel">
        <template #default="{ id, describedBy }">
          <UiSelect :id="id" v-model="moveTo" :options="options" :aria-describedby="describedBy" />
        </template>
      </UiField>
    </template>
    <template #footer>
      <UiButton variant="secondary" data-delete-cancel @click="open = false">Cancel</UiButton>
      <UiButton variant="danger" :disabled="!!view.blocked" @click="confirmDelete">Delete status</UiButton>
    </template>
  </UiDialog>
</template>

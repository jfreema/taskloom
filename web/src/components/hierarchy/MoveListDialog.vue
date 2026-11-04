<script setup lang="ts">
// Move list (8.13), opened by ui.openMoveList(listId) from the list menus ("Move to space…"):
// a Space select without the list's own space, the first other space preselected. "Move" calls
// updateList(id, { spaceId }), which is optimistic (the list goes last in that space, and the store
// rolls back and toasts on failure), toasts "Moved to <space>" and expands that space.
import { computed, ref, useId, watch } from 'vue'
import { useToast } from '../../composables/useToast'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import { useSidebarState } from '../shell/sidebarState'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiSelect from '../ui/UiSelect.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const toast = useToast()
const sidebar = useSidebarState()
const formId = `${useId()}-form`

const listId = ref<Uuid | null>(null)
const listName = ref('')
const target = ref<Uuid | null>(null)

const found = computed(() => (listId.value ? workspaces.findList(listId.value) : null))

const options = computed(() => {
  const f = found.value
  if (!f) return []
  return [...f.workspace.spaces]
    .filter((s) => s.id !== f.space.id)
    .sort((a, b) => a.position - b.position)
    .map((s) => ({ value: s.id, label: s.name }))
})

const open = useRequestedDialog(
  () => ui.moveList,
  () => {
    ui.moveList = null
  },
  (r) => {
    listId.value = r.listId
    listName.value = workspaces.findList(r.listId)?.list.name ?? 'list'
    target.value = options.value[0]?.value ?? null
  },
)

// The list was deleted, or the role was lost: nothing to move.
watch(
  () => [!!found.value, workspaces.canEdit()] as const,
  ([exists, canEdit]) => {
    if (open.value && (!exists || !canEdit)) open.value = false
  },
)

const title = computed(() => `Move “${listName.value}”`)

function move(): void {
  const f = found.value
  const space = f && target.value ? f.workspace.spaces.find((s) => s.id === target.value) : undefined
  if (!f || !space) return
  const id = f.list.id
  const shown = toast.success(`Moved to ${space.name}`)
  sidebar.setCollapsed(space.id, false)
  void workspaces.updateList(id, { spaceId: space.id }).catch(() => toast.dismiss(shown))
  open.value = false
}
</script>

<template>
  <UiDialog v-model:open="open" :title="title" description="The list keeps its tasks, statuses and views." size="sm">
    <form :id="formId" novalidate @submit.prevent="move">
      <UiField v-if="options.length" label="Space">
        <template #default="{ id, describedBy }">
          <UiSelect :id="id" v-model="target" :aria-describedby="describedBy" :options="options" />
        </template>
      </UiField>
      <p v-else class="empty">There's no other space in this workspace. Create one first.</p>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId" :disabled="!target">Move</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.empty {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-secondary);
}
</style>

<script setup lang="ts">
// New space (8.13), opened by ui.openNewSpace(): a name and a colour (Teal by default).
// createSpace is optimistic, so the space is already in the sidebar behind the dialog; the dialog
// waits for the server so it can show a refusal inline. The new space is expanded in the sidebar.
// When the workspace had no list yet (or quick create sent the user here), New list follows for
// the new space, so an empty workspace is two names away from typing tasks.
import { ref, useId, watch } from 'vue'
import { errorMessage } from '../../composables/useToast'
import { DEFAULT_COLORS } from '../../lib/colors'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import { useSidebarState } from '../shell/sidebarState'
import UiButton from '../ui/UiButton.vue'
import UiColorSwatches from '../ui/UiColorSwatches.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiInput from '../ui/UiInput.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const sidebar = useSidebarState()
const uid = useId()
const formId = `${uid}-form`
const colorLabelId = `${uid}-color`

const name = ref('')
const color = ref<string>(DEFAULT_COLORS.space)
const error = ref('')
const busy = ref(false)
/** Quick create sent the user here: there is no list to put a task in yet. */
const noListHint = ref(false)
const input = ref<InstanceType<typeof UiInput> | null>(null)
/** The space New list should open for once this dialog has faded out. */
let thenNewListIn: { spaceId: Uuid; hint: boolean } | null = null

const open = useRequestedDialog(
  () => ui.newSpace,
  () => {
    ui.newSpace = false
  },
  () => {
    name.value = ''
    color.value = DEFAULT_COLORS.space
    error.value = ''
    busy.value = false
    noListHint.value = ui.noListHint
    thenNewListIn = null
  },
)

// Guests never get it (ui.openNewSpace checks); losing the role or the workspace closes it.
watch(
  () => [workspaces.canEdit(), workspaces.currentId] as const,
  ([canEdit, id], [, wasId]) => {
    if (!canEdit || id !== wasId) open.value = false
  },
)

async function submit(): Promise<void> {
  if (busy.value) return
  const trimmed = name.value.trim()
  if (!trimmed) {
    error.value = 'Enter a space name.'
    input.value?.focus()
    return
  }
  const ws = workspaces.current
  if (!ws) return
  const hadList = ws.spaces.some((s) => s.lists.length > 0)
  busy.value = true
  error.value = ''
  try {
    const space = await workspaces.createSpace({ name: trimmed, color: color.value })
    sidebar.setCollapsed(space.id, false)
    if (!hadList || noListHint.value) thenNewListIn = { spaceId: space.id, hint: noListHint.value }
    open.value = false
  } catch (err) {
    error.value = errorMessage(err).message || "Couldn't create the space."
    input.value?.focus()
  } finally {
    busy.value = false
  }
}

function afterLeave(): void {
  const next = thenNewListIn
  thenNewListIn = null
  if (!next || ui.newSpace) return
  ui.openNewList(next.spaceId)
  if (next.hint) ui.noListHint = true
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    title="New space"
    description="Spaces group related lists, such as a team or an area of work."
    size="sm"
    @after-leave="afterLeave"
  >
    <form :id="formId" class="form" novalidate @submit.prevent="submit">
      <UiInlineAlert v-if="noListHint" tone="info">Create a list to hold your tasks first. Lists live in spaces, so start with a space.</UiInlineAlert>
      <UiField label="Name" :error="error || undefined">
        <template #default="{ id, describedBy, invalid }">
          <UiInput
            :id="id"
            ref="input"
            v-model="name"
            :aria-describedby="describedBy"
            :invalid="invalid"
            :maxlength="200"
            placeholder="For example, Marketing"
            autocomplete="off"
            @update:model-value="error = ''"
          />
        </template>
      </UiField>
      <div class="colour">
        <span :id="colorLabelId" class="label">Colour</span>
        <UiColorSwatches v-model="color" :aria-labelledby="colorLabelId" />
      </div>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId" :loading="busy">Create space</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.colour {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.colour :deep(.ui-color-swatches) {
  margin-left: calc(var(--space-1) * -1);
}
</style>

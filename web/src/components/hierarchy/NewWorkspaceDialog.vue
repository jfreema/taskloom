<script setup lang="ts">
// New workspace (8.13), opened by ui.openNewWorkspace() from the workspace menu and from the
// shell's "no workspace" state. Anyone signed in may create one. createWorkspace is optimistic and
// rejects without a toast, so the dialog waits for it, shows a refusal inline, and on success
// switches to the new workspace through useWorkspaceSwitch (8.2), which lands on Home.
import { ref, useId } from 'vue'
import { errorMessage } from '../../composables/useToast'
import { useWorkspaceSwitch } from '../../composables/useWorkspaceSwitch'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiInput from '../ui/UiInput.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const switcher = useWorkspaceSwitch()
const formId = `${useId()}-form`

const name = ref('')
const error = ref('')
const busy = ref(false)
const input = ref<InstanceType<typeof UiInput> | null>(null)

const open = useRequestedDialog(
  () => ui.newWorkspace,
  () => {
    ui.newWorkspace = false
  },
  () => {
    name.value = ''
    error.value = ''
    busy.value = false
  },
)

async function submit(): Promise<void> {
  if (busy.value) return
  const trimmed = name.value.trim()
  if (!trimmed) {
    error.value = 'Enter a workspace name.'
    input.value?.focus()
    return
  }
  busy.value = true
  error.value = ''
  try {
    const ws = await workspaces.createWorkspace(trimmed)
    open.value = false
    await switcher.switchTo(ws.id, { navigate: true })
  } catch (err) {
    error.value = errorMessage(err).message || "Couldn't create the workspace."
    input.value?.focus()
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    title="New workspace"
    description="A workspace has its own spaces, lists, tags and people. You'll be its owner."
    size="sm"
  >
    <form :id="formId" novalidate @submit.prevent="submit">
      <UiField label="Workspace name" :error="error || undefined">
        <template #default="{ id, describedBy, invalid }">
          <UiInput
            :id="id"
            ref="input"
            v-model="name"
            :aria-describedby="describedBy"
            :invalid="invalid"
            :maxlength="200"
            placeholder="For example, Doe family"
            autocomplete="off"
            @update:model-value="error = ''"
          />
        </template>
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId" :loading="busy">Create workspace</UiButton>
    </template>
  </UiDialog>
</template>

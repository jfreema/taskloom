<script setup lang="ts">
// New list (8.13), opened by ui.openNewList(spaceId?): a name, the space (prefilled with the
// requested space, else the space of the list on screen, else the first) and the statuses to
// start with: three presets whose cards list their statuses as text, or a copy of another list's.
// createList waits for the server (its answer carries the statuses) while the button shows
// loading; then the dialog closes, the list opens at /l/<id>/list and its add row takes focus
// (ui.requestInlineAdd), so the next keystrokes type the first task.
import { computed, ref, useId, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Plus } from '@lucide/vue'
import { errorMessage } from '../../composables/useToast'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import ListPicker from '../fields/ListPicker.vue'
import { useSidebarState } from '../shell/sidebarState'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiInput from '../ui/UiInput.vue'
import UiRadioCards from '../ui/UiRadioCards.vue'
import UiSelect from '../ui/UiSelect.vue'
import { PRESET_CARDS, statusesText, type StatusChoice } from './presets'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const route = useRoute()
const router = useRouter()
const sidebar = useSidebarState()
const uid = useId()
const formId = `${uid}-form`
const statusesLabelId = `${uid}-statuses`

const name = ref('')
const spaceId = ref<Uuid | null>(null)
const choice = ref<StatusChoice>('Simple')
const copyFrom = ref<Uuid | null>(null)
const nameError = ref('')
const copyError = ref('')
const busy = ref(false)
const noListHint = ref(false)
const input = ref<InstanceType<typeof UiInput> | null>(null)

const spaces = computed(() => [...(workspaces.current?.spaces ?? [])].sort((a, b) => a.position - b.position))
const spaceOptions = computed(() => spaces.value.map((s) => ({ value: s.id, label: s.name })))

/** The list on screen, when it belongs to the current workspace. */
function listOnScreen(): Uuid | null {
  const id = typeof route.params.listId === 'string' ? route.params.listId : null
  const found = id ? workspaces.findList(id) : null
  return found && found.workspace.id === workspaces.currentId ? found.list.id : null
}

function initialSpace(requested: Uuid | null): Uuid | null {
  if (requested && spaces.value.some((s) => s.id === requested)) return requested
  const here = listOnScreen()
  const found = here ? workspaces.findList(here) : null
  return found?.space.id ?? spaces.value[0]?.id ?? null
}

const open = useRequestedDialog(
  () => ui.newList,
  () => {
    ui.newList = null
  },
  (r) => {
    name.value = ''
    spaceId.value = initialSpace(r.spaceId)
    choice.value = 'Simple'
    copyFrom.value = listOnScreen()
    nameError.value = ''
    copyError.value = ''
    busy.value = false
    noListHint.value = ui.noListHint
  },
)

watch(
  () => [workspaces.canEdit(), workspaces.currentId] as const,
  ([canEdit, id], [, wasId]) => {
    if (!canEdit || id !== wasId) open.value = false
  },
)

const description = computed(
  () => `A list holds the tasks of one project or plan. Everyone in ${workspaces.current?.name ?? 'this workspace'} can see it.`,
)

const copySource = computed(() => (copyFrom.value ? workspaces.findList(copyFrom.value) : null))

const cards = computed(() => {
  const out: { value: string; label: string; description: string }[] = PRESET_CARDS.map((c) => ({ ...c }))
  if (workspaces.allLists.length > 0) {
    out.push({
      value: 'copy',
      label: 'Copy from another list',
      description: copySource.value
        ? statusesText(workspaces.statusesFor(copySource.value.list.id))
        : 'Use the statuses of a list you already have.',
    })
  }
  return out
})

function setChoice(v: string): void {
  choice.value = v as StatusChoice
  copyError.value = ''
}

function setCopyFrom(id: Uuid | null): void {
  copyFrom.value = id
  copyError.value = ''
}

function toNewSpace(): void {
  open.value = false
  ui.openNewSpace()
}

async function submit(): Promise<void> {
  if (busy.value) return
  const trimmed = name.value.trim()
  const sid = spaceId.value && workspaces.findSpace(spaceId.value) ? spaceId.value : null
  nameError.value = trimmed ? '' : 'Enter a list name.'
  copyError.value = choice.value === 'copy' && !copySource.value ? 'Choose a list to copy from.' : ''
  if (nameError.value || copyError.value || !sid) {
    if (nameError.value) input.value?.focus()
    return
  }
  busy.value = true
  try {
    const list = await workspaces.createList(
      sid,
      choice.value === 'copy' && copySource.value
        ? { name: trimmed, statusesFromListId: copySource.value.list.id }
        : { name: trimmed, statusPreset: choice.value === 'copy' ? 'Simple' : choice.value },
    )
    sidebar.setCollapsed(sid, false)
    open.value = false
    await router.push(`/l/${list.id}/list`).catch(() => undefined)
    ui.requestInlineAdd(list.id)
  } catch (err) {
    nameError.value = errorMessage(err).message || "Couldn't create the list."
    input.value?.focus()
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <UiDialog v-model:open="open" title="New list" :description="description">
    <form :id="formId" class="form" novalidate @submit.prevent="submit">
      <UiInlineAlert v-if="noListHint" tone="info">Create a list to hold your tasks first.</UiInlineAlert>
      <UiField label="Name" :error="nameError || undefined">
        <template #default="{ id, describedBy, invalid }">
          <UiInput
            :id="id"
            ref="input"
            v-model="name"
            :aria-describedby="describedBy"
            :invalid="invalid"
            :maxlength="200"
            placeholder="For example, Q4 campaign"
            autocomplete="off"
            @update:model-value="nameError = ''"
          />
        </template>
      </UiField>

      <UiField v-if="spaceOptions.length" label="Space">
        <template #default="{ id, describedBy }">
          <UiSelect :id="id" v-model="spaceId" :aria-describedby="describedBy" :options="spaceOptions" />
        </template>
      </UiField>
      <UiInlineAlert v-else tone="warning" title="Lists live in spaces">
        Create a space first, then add the list to it.
        <template #actions>
          <UiButton size="sm" :icon="Plus" @click="toNewSpace">New space</UiButton>
        </template>
      </UiInlineAlert>

      <div class="group">
        <span :id="statusesLabelId" class="label">Statuses</span>
        <UiRadioCards
          :model-value="choice"
          :options="cards"
          :name="`${uid}-preset`"
          :columns="2"
          :aria-labelledby="statusesLabelId"
          @update:model-value="setChoice"
        />
      </div>

      <UiField v-if="choice === 'copy'" label="Copy statuses from" :error="copyError || undefined">
        <template #default="{ id, describedBy }">
          <ListPicker
            :id="id"
            :aria-describedby="describedBy"
            :model-value="copyFrom"
            variant="field"
            @update:model-value="setCopyFrom"
          />
        </template>
      </UiField>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId" :loading="busy" :disabled="!spaceOptions.length">Create list</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.group {
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
</style>

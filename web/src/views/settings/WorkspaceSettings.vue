<script setup lang="ts">
// Settings › General (8.13, route /settings/workspace) for the current workspace.
// - "Workspace name" is editable by Admin+: Save and Cancel appear once the value changes, Enter
//   saves and Esc goes back. renameWorkspace is optimistic (the sidebar changes at once) and
//   rejects without a toast, so a refusal shows under the field. Others see the name as text.
// - "Your role" says what the caller can do here.
// - The Owner's "Danger zone" deletes the workspace after a confirm that asks for its name; the
//   store then switches to another workspace, or the shell shows its empty state.
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Check } from '@lucide/vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiField from '../../components/ui/UiField.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { useConfirm } from '../../composables/useConfirm'
import { errorMessage } from '../../composables/useToast'
import { useWorkspaces } from '../../stores/workspaces'
import { ROLE_DESCRIPTION, ROLE_LABEL } from '../../types'

const workspaces = useWorkspaces()
const confirm = useConfirm()
const announce = useAnnouncer()

const ws = computed(() => workspaces.current)
const canRename = computed(() => workspaces.isAdmin())
const isOwner = computed(() => workspaces.isOwner())

const draft = ref('')
const error = ref('')
const busy = ref(false)
const saved = ref(false)
const input = ref<InstanceType<typeof UiInput> | null>(null)
let savedTimer: ReturnType<typeof setTimeout> | undefined

const dirty = computed(() => !!ws.value && draft.value.trim() !== ws.value.name)

// Another workspace: start over. A new name from elsewhere replaces the draft unless it was edited.
watch(
  () => ws.value?.id,
  () => {
    draft.value = ws.value?.name ?? ''
    error.value = ''
    saved.value = false
  },
  { immediate: true },
)

watch(
  () => ws.value?.name,
  (name, was) => {
    if (name !== undefined && !busy.value && draft.value.trim() === (was ?? '')) draft.value = name
  },
)

function flashSaved(): void {
  saved.value = true
  clearTimeout(savedTimer)
  savedTimer = setTimeout(() => {
    saved.value = false
  }, 2400)
}

onBeforeUnmount(() => clearTimeout(savedTimer))

function onInput(): void {
  error.value = ''
  saved.value = false
}

async function save(): Promise<void> {
  const w = ws.value
  if (!w || busy.value || !canRename.value) return
  const name = draft.value.trim()
  if (!name) {
    error.value = 'Enter a workspace name.'
    input.value?.focus()
    return
  }
  if (name === w.name) {
    draft.value = w.name
    return
  }
  busy.value = true
  error.value = ''
  try {
    await workspaces.renameWorkspace(w.id, name)
    draft.value = name
    flashSaved()
    announce('Workspace name saved')
  } catch (err) {
    error.value = errorMessage(err).message || "Couldn't rename the workspace."
    input.value?.focus()
  } finally {
    busy.value = false
  }
}

function revert(): void {
  draft.value = ws.value?.name ?? ''
  error.value = ''
}

function onEscape(e: KeyboardEvent): void {
  if (!dirty.value && !error.value) return
  e.preventDefault()
  revert()
}

async function deleteWorkspace(): Promise<void> {
  const w = ws.value
  if (!w || !isOwner.value) return
  const ok = await confirm({
    title: `Delete “${w.name}”?`,
    message: `Deletes all spaces, lists and tasks in ${w.name}. This can't be undone.`,
    confirmLabel: 'Delete workspace',
    danger: true,
    requireText: w.name,
  })
  if (!ok) return
  try {
    await workspaces.deleteWorkspace(w.id)
  } catch {
    // The store toasts and brings the workspace back.
  }
}
</script>

<template>
  <SettingsPage
    title="General"
    :description="canRename ? 'Rename this workspace, or delete it with everything in it.' : 'About this workspace.'"
  >
    <template v-if="ws">
      <section class="block">
        <form v-if="canRename" class="name-form" novalidate @submit.prevent="save">
          <UiField label="Workspace name" hint="Everyone in this workspace sees this name." :error="error || undefined">
            <template #default="{ id, describedBy, invalid }">
              <div class="name-row">
                <UiInput
                  :id="id"
                  ref="input"
                  v-model="draft"
                  class="name-input"
                  :aria-describedby="describedBy"
                  :invalid="invalid"
                  :maxlength="200"
                  autocomplete="off"
                  @update:model-value="onInput"
                  @escape="onEscape"
                />
                <template v-if="dirty || busy">
                  <UiButton variant="primary" type="submit" :loading="busy">Save</UiButton>
                  <UiButton variant="ghost" :disabled="busy" @click="revert">Cancel</UiButton>
                </template>
                <span v-else-if="saved" class="saved">
                  <UiIcon :icon="Check" :size="14" />
                  Saved
                </span>
              </div>
            </template>
          </UiField>
        </form>
        <dl v-else class="facts">
          <dt>Workspace name</dt>
          <dd class="fact-value">{{ ws.name }}</dd>
        </dl>
        <dl class="facts">
          <dt>Your role</dt>
          <dd class="fact-value">
            <span class="role">{{ ROLE_LABEL[ws.role] }}</span>
            <span class="role-description">{{ ROLE_DESCRIPTION[ws.role] }}</span>
          </dd>
        </dl>
      </section>

      <section v-if="isOwner" class="block" aria-labelledby="general-danger">
        <h3 id="general-danger" class="block-title">Danger zone</h3>
        <div class="danger">
          <div class="danger-text">
            <p class="danger-title">Delete workspace</p>
            <p class="danger-body">Deletes all spaces, lists and tasks in {{ ws.name }}. This can't be undone.</p>
          </div>
          <UiButton variant="danger" @click="deleteWorkspace">Delete workspace</UiButton>
        </div>
      </section>
    </template>
  </SettingsPage>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.block-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

/* The field keeps its width while Save, Cancel or "Saved" come and go beside it. */
.name-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
  min-height: var(--control-md);
}

.name-input {
  flex: 0 1 400px;
  min-width: 0;
}

@media (max-width: 639px) {
  .name-input {
    flex-basis: 100%;
  }
}

.saved {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--success-text);
  white-space: nowrap;
}

.facts {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: 0;
}

.facts dt {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.fact-value {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: var(--space-1) var(--space-2);
  margin: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.role {
  font-weight: var(--fw-medium);
}

.role-description {
  color: var(--text-secondary);
}

.danger {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--space-3) var(--space-4);
  padding: var(--space-4);
  border: 1px solid color-mix(in srgb, var(--danger) 35%, var(--border-subtle));
  border-radius: var(--radius-lg);
}

.danger-text {
  flex: 1 1 280px;
  min-width: 0;
}

.danger-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.danger-body {
  margin-top: var(--space-0-5);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
  overflow-wrap: anywhere;
}
</style>

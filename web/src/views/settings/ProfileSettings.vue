<script setup lang="ts">
// Settings › Profile (spec 8.13).
// - UiAvatar 40 beside the name and email, then the "Display name" field: Save and Cancel appear
//   once the value changes, Enter saves and Esc goes back. session.updateMe is optimistic (the
//   sidebar's "Account: <name>" changes at once) and rejects without a toast, so a refusal shows
//   under the field and the old name returns.
// - The email is read-only text under the name: it comes from the sign-in provider.
// - "Sign out" (secondary) calls session.logout().
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { Check, LogOut } from '@lucide/vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiAvatar from '../../components/ui/UiAvatar.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiField from '../../components/ui/UiField.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiInput from '../../components/ui/UiInput.vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { errorMessage } from '../../composables/useToast'
import { useSession } from '../../stores/session'

const MAX_NAME = 200

const session = useSession()
const announce = useAnnouncer()

const user = computed(() => session.user)

const draft = ref(user.value?.displayName ?? '')
const error = ref('')
const busy = ref(false)
const saved = ref(false)
const signingOut = ref(false)
const input = ref<InstanceType<typeof UiInput> | null>(null)
let savedTimer: ReturnType<typeof setTimeout> | undefined

const dirty = computed(() => !!user.value && draft.value.trim() !== user.value.displayName)

// A new name from elsewhere (another tab's refresh) replaces the draft unless it was edited.
watch(
  () => user.value?.displayName,
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
  const me = user.value
  if (!me || busy.value) return
  const name = draft.value.trim()
  if (!name) {
    error.value = 'Enter a display name.'
    input.value?.focus()
    return
  }
  if (name === me.displayName) {
    draft.value = me.displayName
    return
  }
  busy.value = true
  error.value = ''
  try {
    await session.updateMe({ displayName: name })
    draft.value = session.user?.displayName ?? name
    flashSaved()
    announce('Display name saved')
  } catch (err) {
    draft.value = name
    error.value = errorMessage(err).message || "Couldn't save your display name."
    input.value?.focus()
  } finally {
    busy.value = false
  }
}

function revert(): void {
  draft.value = user.value?.displayName ?? ''
  error.value = ''
}

function onEscape(e: KeyboardEvent): void {
  if (!dirty.value && !error.value) return
  e.preventDefault()
  e.stopPropagation()
  revert()
}

async function signOut(): Promise<void> {
  if (signingOut.value) return
  signingOut.value = true
  try {
    await session.logout()
  } finally {
    signingOut.value = false
  }
}
</script>

<template>
  <SettingsPage title="Profile" description="How you appear to the people you work with.">
    <template v-if="user">
      <section class="block">
        <div class="identity">
          <UiAvatar :user="user" :size="40" decorative />
          <div class="identity-text">
            <p class="identity-name">{{ user.displayName }}</p>
            <p class="identity-email">
              <span class="sr-only">Email: </span>{{ user.email }}
            </p>
          </div>
        </div>

        <form class="name-form" novalidate @submit.prevent="save">
          <UiField label="Display name" required hint="Shown on your tasks, comments and in the sidebar." :error="error || undefined">
            <template #default="{ id, describedBy, invalid }">
              <div class="name-row">
                <UiInput
                  :id="id"
                  ref="input"
                  v-model="draft"
                  class="name-input"
                  :aria-describedby="describedBy"
                  :invalid="invalid"
                  :maxlength="MAX_NAME"
                  autocomplete="name"
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

      </section>

      <section class="block block--tight" aria-labelledby="profile-sign-out">
        <h3 id="profile-sign-out" class="block-title">Sign out</h3>
        <div class="sign-out">
          <p class="sign-out-text">Sign out of Taskloom in this browser. Your work stays as it is.</p>
          <UiButton :icon="LogOut" :loading="signingOut" @click="signOut">Sign out</UiButton>
        </div>
      </section>
    </template>
  </SettingsPage>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.block--tight {
  gap: var(--space-3);
}

.block + .block {
  padding-top: var(--space-6);
  border-top: 1px solid var(--border-subtle);
}

.block-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.identity {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
}

.identity-text {
  min-width: 0;
}

.identity-name {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.identity-email {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
  overflow-wrap: anywhere;
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

.saved {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--success-text);
  white-space: nowrap;
}

.sign-out {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--space-3) var(--space-4);
}

.sign-out-text {
  flex: 1 1 260px;
  min-width: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

@container page (width < 640px) {
  .name-input {
    flex-basis: 100%;
  }
}
</style>

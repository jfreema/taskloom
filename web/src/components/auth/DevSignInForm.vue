<script setup lang="ts">
// The dev sign-in form of the sign-in page (spec 8.14), shown only when the server enables dev
// login: a "Dev only" badge, Email (required) and Display name (optional), and the primary
// "Sign in". Errors appear in a danger inline alert above the button and the field it concerns is
// marked invalid and focused. After signing in it goes to `returnUrl` (already made safe).
import { ref, useId } from 'vue'
import { useRouter } from 'vue-router'
import { useSession } from '../../stores/session'
import { isValidEmail } from '../../lib/format'
import { errorStatus } from '../../lib/async'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiField from '../ui/UiField.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiInput from '../ui/UiInput.vue'

const props = defineProps<{ returnUrl: string; autofocus?: boolean }>()

const session = useSession()
const router = useRouter()

const email = ref('')
const displayName = ref('')
const busy = ref(false)
const error = ref('')
const invalidField = ref<'email' | 'name' | null>(null)
const errorId = `${useId()}-error`

const emailInput = ref<InstanceType<typeof UiInput> | null>(null)
const nameInput = ref<InstanceType<typeof UiInput> | null>(null)

function messageFor(err: unknown): string {
  const status = errorStatus(err)
  if (typeof err === 'object' && err !== null && (err as { isNetwork?: unknown }).isNetwork === true) {
    return "Couldn't reach Taskloom. Check your connection and try again."
  }
  if (status === 404) return "Dev sign-in isn't available on this server."
  if (status !== null && status >= 500) return 'Something went wrong on the server. Try again in a moment.'
  const message = err instanceof Error ? err.message.trim() : ''
  return message || "Couldn't sign you in. Try again."
}

function fail(message: string, field: 'email' | 'name' | null): void {
  error.value = message
  invalidField.value = field
  const target = field === 'name' ? nameInput.value : emailInput.value
  target?.focus()
}

function onEdit(field: 'email' | 'name'): void {
  if (invalidField.value === field) {
    invalidField.value = null
    error.value = ''
  }
}

async function submit(): Promise<void> {
  if (busy.value) return
  error.value = ''
  invalidField.value = null
  const address = email.value.trim()
  if (!address) return fail('Enter your email address.', 'email')
  if (!isValidEmail(address)) return fail('Enter a valid email address.', 'email')

  busy.value = true
  try {
    await session.devLogin({ email: address, displayName: displayName.value.trim() || undefined })
  } catch (err) {
    busy.value = false
    const message = messageFor(err)
    const field = errorStatus(err) === 400 ? (/display name/i.test(message) ? 'name' : 'email') : null
    fail(message, field)
    return
  }
  // The button keeps its spinner while the app loads behind the new route.
  await router.replace(props.returnUrl).catch(() => undefined)
  busy.value = false
}
</script>

<template>
  <form class="dev-form" novalidate aria-label="Dev sign-in" @submit.prevent="submit">
    <UiBadge class="badge" tone="warning">Dev only</UiBadge>
    <UiField label="Email" required>
      <template #default="{ id }">
        <UiInput
          :id="id"
          ref="emailInput"
          v-model="email"
          size="lg"
          type="email"
          autocomplete="email"
          inputmode="email"
          autocapitalize="none"
          spellcheck="false"
          placeholder="you@example.com"
          :autofocus="autofocus"
          :invalid="invalidField === 'email'"
          :aria-describedby="invalidField === 'email' && error ? errorId : undefined"
          aria-required="true"
          @update:model-value="onEdit('email')"
        />
      </template>
    </UiField>
    <UiField label="Display name" hint="Optional. Only used the first time this email signs in.">
      <template #default="{ id, describedBy }">
        <UiInput
          :id="id"
          ref="nameInput"
          v-model="displayName"
          size="lg"
          autocomplete="name"
          :maxlength="200"
          :invalid="invalidField === 'name'"
          :aria-describedby="[describedBy, invalidField === 'name' && error ? errorId : ''].filter(Boolean).join(' ') || undefined"
          @update:model-value="onEdit('name')"
        />
      </template>
    </UiField>
    <UiInlineAlert v-if="error" :id="errorId" tone="danger">{{ error }}</UiInlineAlert>
    <UiButton class="submit" type="submit" variant="primary" size="lg" block :loading="busy">Sign in</UiButton>
  </form>
</template>

<style scoped>
.dev-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.badge {
  align-self: flex-start;
}

.submit {
  margin-top: var(--space-1);
}
</style>

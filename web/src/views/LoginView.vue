<script setup lang="ts">
// Sign-in (spec 8.14). Placeholder until B3 builds the full page: a working dev sign-in (email,
// display name, "Sign in"), provider links, and the ?error= alert. After signing in it goes to
// safeReturnUrl(returnUrl).
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSession } from '../stores/session'
import { safeReturnUrl } from '../router'
import { isValidEmail } from '../lib/format'
import type { AuthProviders } from '../types'
import AppLogo from '../components/shell/AppLogo.vue'
import UiBadge from '../components/ui/UiBadge.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiField from '../components/ui/UiField.vue'
import UiInlineAlert from '../components/ui/UiInlineAlert.vue'
import UiInput from '../components/ui/UiInput.vue'

const session = useSession()
const route = useRoute()
const router = useRouter()

const email = ref('')
const displayName = ref('')
const busy = ref(false)
const formError = ref('')
const providers = ref<AuthProviders | null>(null)

const returnUrl = computed(() => safeReturnUrl(route.query.returnUrl))
const signInError = computed(() => {
  const e = route.query.error
  const v = Array.isArray(e) ? e[0] : e
  return typeof v === 'string' && v.trim() ? v : ''
})

onMounted(async () => {
  try {
    providers.value = await session.providers()
  } catch {
    providers.value = { devLogin: true, providers: [] }
  }
})

function providerHref(key: string): string {
  return `/auth/login/${encodeURIComponent(key)}?returnUrl=${encodeURIComponent(returnUrl.value)}`
}

async function submit(): Promise<void> {
  formError.value = ''
  if (!isValidEmail(email.value)) {
    formError.value = 'Enter a valid email address.'
    return
  }
  busy.value = true
  try {
    await session.devLogin({ email: email.value.trim(), displayName: displayName.value.trim() || undefined })
    await router.replace(returnUrl.value)
  } catch (err) {
    formError.value = err instanceof Error && err.message ? err.message : "Couldn't sign you in."
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login">
    <main class="column">
      <AppLogo class="logo" :size="32" wordmark />
      <h1 class="heading">Sign in to Taskloom</h1>
      <UiInlineAlert v-if="signInError" tone="danger" title="Couldn't sign you in">{{ signInError }}</UiInlineAlert>

      <div v-if="providers?.providers.length" class="providers">
        <UiButton
          v-for="p in providers.providers"
          :key="p.key"
          size="lg"
          block
          :href="providerHref(p.key)"
        >
          Continue with {{ p.displayName }}
        </UiButton>
      </div>

      <form v-if="providers === null || providers.devLogin" class="dev-form" novalidate @submit.prevent="submit">
        <UiBadge tone="warning">Dev only</UiBadge>
        <UiField label="Email" required>
          <template #default="{ id }">
            <UiInput :id="id" v-model="email" type="email" autocomplete="email" autofocus />
          </template>
        </UiField>
        <UiField label="Display name">
          <template #default="{ id }">
            <UiInput :id="id" v-model="displayName" autocomplete="name" />
          </template>
        </UiField>
        <UiInlineAlert v-if="formError" tone="danger">{{ formError }}</UiInlineAlert>
        <UiButton type="submit" variant="primary" size="lg" block :loading="busy">Sign in</UiButton>
      </form>

      <UiInlineAlert v-else-if="!providers.providers.length" tone="info">
        Sign-in isn't set up yet. Ask your administrator to configure a provider.
      </UiInlineAlert>
    </main>
  </div>
</template>

<style scoped>
.login {
  display: flex;
  justify-content: center;
  min-height: 100dvh;
  padding: var(--space-16) var(--space-4) var(--space-8);
  background: var(--bg-surface);
}

.column {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  width: 100%;
  max-width: 360px;
}

.logo {
  margin-bottom: var(--space-4);
}

.heading {
  font-size: var(--fs-2xl);
  line-height: var(--lh-2xl);
  font-weight: var(--fw-semibold);
}

.providers,
.dev-form {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.dev-form > :first-child {
  align-self: flex-start;
}
</style>

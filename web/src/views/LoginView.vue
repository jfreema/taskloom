<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSession } from '../stores/session'

const session = useSession()
const route = useRoute()
const router = useRouter()

const providers = ref<{ key: string; displayName: string }[]>([])
const devLogin = ref(false)
const email = ref('')
const name = ref('')
const error = ref((route.query.error as string) ?? '')
const busy = ref(false)
const returnUrl = (route.query.returnUrl as string) || '/'

onMounted(async () => {
  const p = await session.providers()
  providers.value = p.providers
  devLogin.value = p.devLogin
})

async function submitDev() {
  busy.value = true
  error.value = ''
  try {
    await session.devLogin(email.value, name.value)
    router.replace(returnUrl)
  } catch (e) {
    error.value = (e as Error).message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login">
    <div class="card">
      <div class="brand"><span class="logo">✓</span> Taskloom</div>
      <p class="muted">Sign in to your workspace</p>

      <a
        v-for="p in providers"
        :key="p.key"
        class="provider"
        :href="`/auth/login/${p.key}?returnUrl=${encodeURIComponent(returnUrl)}`"
      >
        Continue with {{ p.displayName }}
      </a>

      <form v-if="devLogin" class="dev" @submit.prevent="submitDev">
        <div class="divider"><span>Local development sign-in</span></div>
        <input v-model="email" type="email" placeholder="you@example.com" required autofocus />
        <input v-model="name" placeholder="Display name (optional)" />
        <button class="primary" :disabled="busy">Sign in</button>
      </form>

      <p v-if="!devLogin && providers.length === 0" class="muted">
        No sign-in method is configured. Set <code>Auth__DevLogin__Enabled=true</code> or add an OpenID Connect provider.
      </p>
      <p v-if="error" class="error">{{ error }}</p>
    </div>
  </div>
</template>

<style scoped>
.login { min-height: 100%; display: grid; place-items: center; background: var(--bg-soft); padding: 16px; }
.card { width: 100%; max-width: 360px; background: var(--bg); border: 1px solid var(--border); border-radius: 12px; padding: 32px; box-shadow: var(--shadow); display: flex; flex-direction: column; gap: 12px; }
.brand { font-size: 22px; font-weight: 700; display: flex; align-items: center; gap: 10px; }
.logo { width: 32px; height: 32px; border-radius: 8px; background: var(--accent); color: #fff; display: grid; place-items: center; font-size: 18px; }
.provider { display: block; text-align: center; padding: 9px; border: 1px solid var(--border); border-radius: 8px; color: var(--text); text-decoration: none; font-weight: 600; }
.provider:hover { background: var(--bg-hover); }
.dev { display: flex; flex-direction: column; gap: 8px; }
.divider { text-align: center; font-size: 12px; color: var(--text-faint); margin: 8px 0 4px; }
.error { color: var(--danger); margin: 0; }
p { margin: 0; }
</style>

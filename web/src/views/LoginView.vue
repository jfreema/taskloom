<script setup lang="ts">
// Sign-in (spec 8.14). A teal brand panel (5/12 wide from 960px, a 96px band above that) beside the
// form column on --bg-surface. The form lists the providers of GET /auth/providers, an "or"
// divider when there are providers and dev login, and the dev form when the server enables it.
// A non-empty ?error= (a failed provider sign-in, 5.8) shows "Couldn't sign you in" above the
// providers; the page then drops `error` from the URL with router.replace, keeping returnUrl.
// Hex colours appear here only for the brand panel, which is the same in both themes.
import { computed, onMounted, ref, useId, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { RotateCcw } from '@lucide/vue'
import { useSession } from '../stores/session'
import { safeReturnUrl } from '../router'
import { useBreakpoint } from '../composables/useBreakpoint'
import { useDelayedFlag } from '../composables/useDelayedFlag'
import type { AuthProviders } from '../types'
import AppLogo from '../components/shell/AppLogo.vue'
import DevSignInForm from '../components/auth/DevSignInForm.vue'
import ProviderButtons from '../components/auth/ProviderButtons.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiInlineAlert from '../components/ui/UiInlineAlert.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'

const session = useSession()
const route = useRoute()
const router = useRouter()
const { coarse } = useBreakpoint()

const weaveId = `${useId()}-weave`

const returnUrl = computed(() => safeReturnUrl(route.query.returnUrl))

// ?error= -> the alert. The message is server text passed through the URL: control characters are
// dropped and it is capped so that a crafted link can't fill the page.
const signInError = ref('')

function firstString(v: unknown): string {
  const s = Array.isArray(v) ? v[0] : v
  return typeof s === 'string' ? s : ''
}

function cleanMessage(s: string): string {
  const flat = s.replace(/[\u0000-\u001f\u007f-\u009f]+/g, ' ').replace(/\s+/g, ' ').trim()
  return flat.length > 300 ? `${flat.slice(0, 299).trimEnd()}…` : flat
}

watch(
  () => route.query.error,
  (raw) => {
    if (raw === undefined) return
    const message = cleanMessage(firstString(raw))
    if (message) signInError.value = message
    const query = { ...route.query }
    delete query.error
    void router.replace({ path: route.path, query, hash: route.hash }).catch(() => undefined)
  },
  { immediate: true },
)

// Providers.
const loadState = ref<'loading' | 'error' | 'ready'>('loading')
const config = ref<AuthProviders | null>(null)
const showSkeletons = useDelayedFlag(computed(() => loadState.value === 'loading'))

async function loadProviders(): Promise<void> {
  loadState.value = 'loading'
  try {
    config.value = await session.providers()
    loadState.value = 'ready'
  } catch {
    loadState.value = 'error'
  }
}

onMounted(() => {
  void loadProviders()
})

const providers = computed(() => config.value?.providers ?? [])
const devLogin = computed(() => config.value?.devLogin === true)
</script>

<template>
  <div class="login">
    <div class="brand">
      <svg class="weave" aria-hidden="true" focusable="false">
        <defs>
          <!-- A plain weave: two threads each way, each passing over one crossing and under the next. -->
          <pattern :id="weaveId" width="48" height="48" patternUnits="userSpaceOnUse">
            <path
              d="M0 8H32M40 8H48M0 16H32M40 16H48M0 32H8M16 32H48M0 40H8M16 40H48M8 0V8M8 16V48M16 0V8M16 16V48M32 0V32M32 40V48M40 0V32M40 40V48"
              fill="none"
              stroke="#FFFFFF"
              stroke-width="1.5"
            />
          </pattern>
        </defs>
        <rect width="100%" height="100%" :fill="`url(#${weaveId})`" />
      </svg>
      <AppLogo class="brand-logo" :size="24" wordmark />
      <div class="brand-copy">
        <p class="headline">
          <span class="line">All your work,</span>
          <span class="line">woven together.</span>
        </p>
        <p class="subline">Lists, boards and calendars for your team.</p>
      </div>
    </div>

    <main id="main" class="column" tabindex="-1">
      <div class="form" :aria-busy="loadState === 'loading' ? 'true' : undefined">
        <h1 class="heading">Sign in to Taskloom</h1>

        <UiInlineAlert v-if="signInError" tone="danger" title="Couldn't sign you in" dismissible @dismiss="signInError = ''">
          {{ signInError }}
        </UiInlineAlert>

        <div v-if="loadState === 'loading'" class="skeletons">
          <template v-if="showSkeletons">
            <UiSkeleton variant="rect" height="40px" class="skeleton" />
            <UiSkeleton variant="rect" height="40px" class="skeleton" />
          </template>
        </div>

        <UiInlineAlert v-else-if="loadState === 'error'" tone="danger" title="Couldn't load the sign-in options">
          Check your connection, then try again.
          <template #actions>
            <UiButton size="sm" :icon="RotateCcw" @click="loadProviders">Try again</UiButton>
          </template>
        </UiInlineAlert>

        <template v-else>
          <ProviderButtons v-if="providers.length" :providers="providers" :return-url="returnUrl" />
          <p v-if="providers.length && devLogin" class="divider"><span>or</span></p>
          <DevSignInForm v-if="devLogin" :return-url="returnUrl" :autofocus="!coarse && !providers.length" />
          <UiInlineAlert v-if="!providers.length && !devLogin" tone="info">
            Sign-in isn't set up yet. Ask your administrator to configure a provider.
          </UiInlineAlert>
        </template>
      </div>
    </main>
  </div>
</template>

<style scoped>
.login {
  display: flex;
  min-height: 100dvh;
  background: var(--bg-surface);
}

/* Brand panel: the same teal in both themes. */
.brand {
  position: relative;
  isolation: isolate;
  display: flex;
  flex-direction: column;
  flex: 0 0 calc(100% * 5 / 12);
  min-height: 100dvh;
  padding: var(--space-10) var(--space-12);
  overflow: hidden;
  background: linear-gradient(160deg, #0D7469, #0A4F49);
  color: #FFFFFF;
}

.weave {
  position: absolute;
  inset: 0;
  z-index: -1;
  width: 100%;
  height: 100%;
  opacity: 0.06;
  /* Strongest at the top right, fading out behind the copy. */
  mask-image: linear-gradient(200deg, black 15%, transparent 90%);
}

.brand-logo {
  align-self: flex-start;
}

/* The mark's own teal tile would melt into the panel: a faint ring keeps its shape. */
.brand-logo :deep(.mark) {
  border-radius: 27%;
  box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.28), 0 1px 3px rgba(0, 0, 0, 0.18);
}

.brand-copy {
  margin-block: auto;
  padding-bottom: var(--space-16);
  max-width: 420px;
}

.headline {
  font-size: var(--fs-3xl);
  line-height: var(--lh-3xl);
  font-weight: var(--fw-bold);
  letter-spacing: -0.01em;
  text-wrap: balance;
}

.line {
  display: block;
}

.subline {
  margin-top: var(--space-3);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  color: rgba(255, 255, 255, 0.8);
}

/* Form column */
.column {
  flex: 1 1 auto;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  min-width: 0;
  padding: clamp(var(--space-12), 18vh, 180px) var(--space-6) var(--space-12);
  background: var(--bg-surface);
  outline: none;
}

.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  width: 100%;
  max-width: 360px;
}

.heading {
  margin-bottom: var(--space-2);
  font-size: var(--fs-2xl);
  line-height: var(--lh-2xl);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.skeletons {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-height: calc(var(--control-lg) * 2 + var(--space-3));
}

.skeleton {
  display: block;
  border-radius: var(--radius-md);
}

.divider {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.divider::before,
.divider::after {
  content: "";
  flex: 1;
  height: 1px;
  background: var(--border-subtle);
}

/* Below 960px the brand panel becomes a 96px band with the logo and wordmark. */
@media (width < 960px) {
  .login {
    flex-direction: column;
  }

  .brand {
    flex: 0 0 96px;
    min-height: 96px;
    height: 96px;
    padding: 0 var(--space-4);
    align-items: center;
    justify-content: center;
  }

  .brand-logo {
    align-self: center;
  }

  .brand-copy {
    display: none;
  }

  .column {
    padding: var(--space-10) var(--space-4) var(--space-12);
  }
}

@media (width < 640px) {
  .column {
    padding-top: var(--space-8);
  }
}
</style>

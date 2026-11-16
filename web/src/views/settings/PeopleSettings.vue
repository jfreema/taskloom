<script setup lang="ts">
// Settings › People (spec E3): every account on this Taskloom, for site admins only (the route
// sends anyone else to Profile, and the API answers them 404).
// - The list "People", by name (PersonRow): status, last activity, workspaces and a menu to turn
//   the account off or on and to sign the person out everywhere.
// - "Turn off account" asks first. The store waits for the server, so the success toasts say what
//   really happened; failures toast from the store.
// - Signing yourself out everywhere ends this session too, so it carries on as a normal sign-out.
// - The list reloads on every visit, because last activity goes stale; rows already loaded stay
//   up meanwhile. A 404 means you're no longer a site admin: the session reloads, and losing the
//   flag sends you to Profile.
// - States: skeleton rows while loading (after 200ms) and an error with "Try again".
import { computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import PersonRow from '../../components/settings/PersonRow.vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiErrorState from '../../components/ui/UiErrorState.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import { skeletonDelay } from '../../components/shell/skeletonHandover'
import { useConfirm } from '../../composables/useConfirm'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useToast } from '../../composables/useToast'
import { errorStatus } from '../../lib/async'
import { useAdminUsers } from '../../stores/adminUsers'
import { useSession } from '../../stores/session'
import type { AdminUser } from '../../types'

const people = useAdminUsers()
const session = useSession()
const router = useRouter()
const confirm = useConfirm()
const toast = useToast()

const meId = computed(() => session.user?.id ?? null)

void people.reload()

watch(
  () => people.loadError,
  (err) => {
    if (errorStatus(err) === 404) void session.load()
  },
)

watch(
  () => session.user?.isSiteAdmin,
  (isAdmin) => {
    if (isAdmin === false) void router.replace({ name: 'settings-profile' })
  },
)

const rows = computed<AdminUser[]>(() => people.all)

const state = computed<'loading' | 'error' | 'list'>(() => {
  if (rows.value.length) return 'list'
  return people.loadError && !people.loading ? 'error' : 'loading'
})

const showSkeleton = useDelayedFlag(computed(() => state.value === 'loading'), skeletonDelay())

async function turnOff(u: AdminUser): Promise<void> {
  const ok = await confirm({
    title: `Turn off ${u.displayName}'s account?`,
    message: "They'll be signed out everywhere and can't sign in until you turn their account back on. Their tasks and comments stay.",
    confirmLabel: 'Turn off',
    danger: true,
  })
  if (!ok) return
  try {
    await people.disable(u.id)
  } catch {
    return // The store toasted the failure.
  }
  toast.success(`Turned off ${u.displayName}'s account.`)
}

async function turnOn(u: AdminUser): Promise<void> {
  try {
    await people.enable(u.id)
  } catch {
    return
  }
  toast.success(`Turned on ${u.displayName}'s account.`)
}

async function signOutEverywhere(u: AdminUser): Promise<void> {
  try {
    await people.signOutEverywhere(u.id)
  } catch {
    return
  }
  // This browser's session ended with the others: finish signing out here (the sign-in page says the rest).
  if (u.id === meId.value) await session.logout()
  else toast.success(`Signed ${u.displayName} out everywhere.`)
}
</script>

<template>
  <SettingsPage
    title="People"
    description="Everyone who has signed in or been invited to Taskloom. Turning an account off signs the person out everywhere and stops them signing in."
    :busy="state === 'loading'"
  >
    <div v-if="state === 'loading'" aria-hidden="true">
      <div v-if="showSkeleton" class="frame">
        <div v-for="i in 4" :key="i" class="skeleton-row">
          <UiSkeleton variant="circle" width="32px" />
          <div class="skeleton-text">
            <UiSkeleton width="140px" />
            <UiSkeleton width="200px" />
          </div>
        </div>
      </div>
    </div>

    <UiErrorState
      v-else-if="state === 'error'"
      title="Couldn't load the people"
      message="Check your connection and try again."
      :retry="() => void people.reload()"
    />

    <div v-else class="frame">
      <ul class="people" role="list" aria-label="People">
        <PersonRow
          v-for="u in rows"
          :key="u.id"
          :user="u"
          :is-me="u.id === meId"
          :busy="people.busy.has(u.id)"
          @turn-off="turnOff(u)"
          @turn-on="turnOn(u)"
          @sign-out="signOutEverywhere(u)"
        />
      </ul>
    </div>
  </SettingsPage>
</template>

<style scoped>
/* PersonRow lays out against this width (the activity moves under the email below 560px). */
.frame {
  container: people / inline-size;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  overflow: hidden;
}

.people {
  margin: 0;
  padding: 0;
  list-style: none;
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: 60px;
  padding: 0 var(--space-4);
}

.skeleton-row + .skeleton-row {
  border-top: 1px solid var(--border-subtle);
}

.skeleton-text {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}
</style>

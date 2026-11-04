// The signed-in user (10.2). `load()` probes GET /api/me without triggering the unauthorized
// handler, so the router guard decides where to go.
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '../api'
import { appRouter } from '../composables/appRouter'
import { useDelayedDelete } from '../composables/useDelayedDelete'
import { errorStatus, handled } from '../lib/async'
import type { AuthProviders, DevLoginRequest, UpdateMeRequest, User } from '../types'
import { useMembers } from './members'
import { useRecents } from './recents'
import { useSelection } from './selection'
import { useTags } from './tags'
import { useTasks } from './tasks'
import { useUi } from './ui'
import { useWorkspaces } from './workspaces'

export const useSession = defineStore('session', () => {
  const user = ref<User | null>(null)
  const loaded = ref(false)
  /** The last failure of `load()` other than a 401 (offline, 5xx); null after a successful load. */
  const error = ref<unknown>(null)
  let loading: Promise<void> | null = null
  let providersCache: Promise<AuthProviders> | null = null

  /** Clears every user-scoped store (sign-out, or signing in as someone else). */
  function resetStores(): void {
    useSelection().reset()
    useTasks().reset()
    useMembers().reset()
    useTags().reset()
    useWorkspaces().reset()
    useRecents().reset()
    useUi().reset()
  }

  /** GET /api/me. A 401 leaves `user` null; never rejects. */
  function load(): Promise<void> {
    if (loading) return loading
    loading = (async () => {
      try {
        const me = await api.get<User>('/api/me', { ignoreUnauthorized: true })
        if (user.value && user.value.id !== me.id) resetStores()
        user.value = me
        error.value = null
      } catch (err) {
        if (errorStatus(err) === 401) {
          if (user.value) resetStores()
          user.value = null
          error.value = null
        } else {
          error.value = err
        }
      } finally {
        loaded.value = true
        loading = null
      }
    })()
    return loading
  }

  /** POST /auth/dev-login. Rejects with the ApiError (the sign-in form shows `detail` inline). */
  function devLogin(r: DevLoginRequest): Promise<void> {
    return handled(
      api.post<User>('/auth/dev-login', r).then((me) => {
        if (user.value && user.value.id !== me.id) resetStores()
        user.value = me
        loaded.value = true
        error.value = null
      }),
    )
  }

  /** Commits pending deletes, signs out on the server, clears every store and goes to /login. */
  async function logout(): Promise<void> {
    try {
      await useDelayedDelete().flushAll()
    } catch {
      // flushAll reports its own failures.
    }
    try {
      await api.post<void>('/auth/logout')
    } catch {
      // Signing out locally still works when the server can't be reached.
    }
    resetStores()
    user.value = null
    loaded.value = true
    const router = appRouter()
    if (router) await router.push({ name: 'login' }).catch(() => undefined)
  }

  /** PATCH /api/me. Rejects with the ApiError (the profile form shows it inline). */
  function updateMe(r: UpdateMeRequest): Promise<void> {
    const previous = user.value
    const displayName = r.displayName.trim()
    // The members lists carry display names too.
    const members = useMembers()
    if (previous) {
      user.value = { ...previous, displayName }
      members.renameLocal(previous.id, displayName)
    }
    return handled(
      api.patch<User>('/api/me', { displayName }).then(
        (me) => {
          user.value = me
          members.renameLocal(me.id, me.displayName)
        },
        (err: unknown) => {
          if (previous && user.value?.id === previous.id) {
            user.value = previous
            members.renameLocal(previous.id, previous.displayName)
          }
          throw err
        },
      ),
    )
  }

  /** GET /auth/providers, cached for the page's lifetime (a failure is not cached). */
  function providers(): Promise<AuthProviders> {
    if (!providersCache) {
      providersCache = api.get<AuthProviders>('/auth/providers', { ignoreUnauthorized: true })
      providersCache.catch(() => {
        providersCache = null
      })
    }
    return handled(providersCache)
  }

  return { user, loaded, error, load, devLogin, logout, updateMe, providers }
})

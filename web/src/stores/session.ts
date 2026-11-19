// The signed-in user (10.2). `load()` probes GET /api/me without triggering the unauthorized
// handler, so the router guard decides where to go. `user.isSiteAdmin` shows Settings › People.
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { ApiError, api } from '../api'
import { appRouter } from '../composables/appRouter'
import { useDelayedDelete } from '../composables/useDelayedDelete'
import { useToast } from '../composables/useToast'
import { errorStatus, handled } from '../lib/async'
import type { AuthProviders, DevLoginRequest, Me, UpdateMeRequest, User } from '../types'
import { useAdminUsers } from './adminUsers'
import { useMembers } from './members'
import { useRecents } from './recents'
import { useSelection } from './selection'
import { useTags } from './tags'
import { useTasks } from './tasks'
import { useUi } from './ui'
import { useWorkspaces } from './workspaces'

export const useSession = defineStore('session', () => {
  const toast = useToast()
  const user = ref<Me | null>(null)
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
    useAdminUsers().reset()
  }

  /** GET /api/me. A 401 leaves `user` null; never rejects. */
  function load(): Promise<void> {
    if (loading) return loading
    loading = (async () => {
      try {
        const me = await api.get<Me>('/api/me', { ignoreUnauthorized: true })
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

  /**
   * POST /auth/dev-login. Rejects with the ApiError (the sign-in form shows `detail` inline).
   * Dev login answers a plain User, so the site-admin flag comes from GET /api/me afterwards.
   */
  function devLogin(r: DevLoginRequest): Promise<void> {
    return handled(
      api.post<User | Me>('/auth/dev-login', r).then(async (signedIn) => {
        const me = await meAfterSignIn(signedIn)
        if (user.value && user.value.id !== me.id) resetStores()
        user.value = me
        loaded.value = true
        error.value = null
      }),
    )
  }

  async function meAfterSignIn(signedIn: User | Me): Promise<Me> {
    if ('isSiteAdmin' in signedIn && typeof signedIn.isSiteAdmin === 'boolean') return signedIn
    try {
      return await api.get<Me>('/api/me', { ignoreUnauthorized: true })
    } catch {
      // Signed in all the same. Without the flag the Admin settings stay hidden until the next load().
      return { ...signedIn, isSiteAdmin: false, photoChosen: false }
    }
  }

  /**
   * Commits pending deletes, signs out on the server, clears every store and goes to /login. When
   * the server refuses (a 429, a 5xx), you stay signed in and a toast says why: the session and its
   * HttpOnly cookie still work, so showing the sign-in page would only look like a sign-out, and the
   * next person at this browser would be signed in as you. Never rejects.
   */
  async function logout(): Promise<void> {
    try {
      await useDelayedDelete().flushAll()
    } catch {
      // flushAll reports its own failures.
    }
    try {
      await api.post<void>('/auth/logout')
    } catch (err) {
      // A 401 means the session had already ended. A server that can't be reached can't be told
      // anyway, so signing out locally at least clears the screen.
      if (err instanceof ApiError && !err.isNetwork && err.status !== 401) {
        toast.error(err, { retry: () => void logout() })
        return
      }
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
      api.patch<Me>('/api/me', { displayName }).then(
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

  /** A new photo, or none, for the signed-in user, mirrored into the members lists. */
  function applyPhoto(me: Me): void {
    if (user.value && user.value.id !== me.id) return
    user.value = me
    useMembers().setPhotoLocal(me.id, me.photoUrl)
  }

  /** POST /api/me/photo with a photo made by lib/photo.ts squarePhoto. Rejects with the ApiError (the profile page shows it). */
  function uploadPhoto(photo: Blob): Promise<void> {
    const form = new FormData()
    form.append('file', photo, 'photo.jpg')
    return handled(api.upload<Me>('/api/me/photo', form).then(applyPhoto))
  }

  /** DELETE /api/me/photo: initials from now on, also after signing in again. Rejects with the ApiError. */
  function removePhoto(): Promise<void> {
    return handled(api.del<Me>('/api/me/photo').then(applyPhoto))
  }

  /**
   * POST /api/me/sign-out-others: ends this account's sessions in every other browser and keeps
   * this one. Rejects without a toast (the profile page words both outcomes).
   */
  function signOutOthers(): Promise<void> {
    return handled(api.post<void>('/api/me/sign-out-others'))
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

  return { user, loaded, error, load, devLogin, logout, updateMe, uploadPhoto, removePhoto, signOutOthers, providers }
})

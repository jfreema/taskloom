// Every account on this Taskloom, for Settings › People (site admins only, spec D and E). One list
// for the whole instance, not per workspace. Turning an account off or on and signing someone out
// wait for the server rather than being optimistic: they end other people's sessions, so the page
// only says they happened once they have. Failures toast here; the page words the successes.
import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'
import { api } from '../api'
import { useToast } from '../composables/useToast'
import { handled } from '../lib/async'
import type { AdminUser, Uuid } from '../types'
import { useMembers } from './members'

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

/** The server's order: display name ignoring case, then id. */
function byName(a: AdminUser, b: AdminUser): number {
  return collator.compare(a.displayName, b.displayName) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}

export const useAdminUsers = defineStore('adminUsers', () => {
  const toast = useToast()

  const rows = ref<AdminUser[]>([])
  const loading = ref(false)
  /** The last load failure, or null. A 404 means the caller isn't a site admin (any more). */
  const loadError = ref<unknown>(null)
  /** Accounts with an action on its way; their row menus wait for it. */
  const busy = reactive(new Set<Uuid>())
  let inflight: Promise<void> | null = null
  let generation = 0

  const all = computed<AdminUser[]>(() => [...rows.value].sort(byName))

  /**
   * GET /api/admin/users. Rows already loaded stay on screen meanwhile (the page reloads on every
   * visit, because last activity goes stale). Never rejects (see `loadError`).
   */
  function reload(): Promise<void> {
    if (inflight) return inflight
    const gen = generation
    loading.value = true
    const p = api
      .get<AdminUser[]>('/api/admin/users')
      .then(
        (users) => {
          if (gen !== generation) return
          rows.value = users
          loadError.value = null
        },
        (err: unknown) => {
          if (gen !== generation) return
          loadError.value = err
        },
      )
      .finally(() => {
        if (inflight === p) inflight = null
        if (gen === generation) loading.value = false
      })
    inflight = p
    return p
  }

  function upsertLocal(user: AdminUser): void {
    const i = rows.value.findIndex((u) => u.id === user.id)
    rows.value = i === -1 ? [...rows.value, user] : rows.value.map((u, j) => (j === i ? user : u))
  }

  /** The new state of an account turned off or on, here and in the Members lists already loaded (their "Turned off" chip). */
  function applyDisabled(user: AdminUser): void {
    upsertLocal(user)
    useMembers().setDisabledLocal(user.id, user.disabled)
  }

  /** Runs one action for an account: marks it busy, and toasts and rethrows a failure. */
  function act<T>(id: Uuid, run: () => Promise<T>): Promise<T> {
    const gen = generation
    busy.add(id)
    return handled(
      run()
        .catch((err: unknown) => {
          toast.error(err)
          throw err
        })
        .finally(() => {
          if (gen === generation) busy.delete(id)
        }),
    )
  }

  /** POST /api/admin/users/{id}/disable: turns the account off and signs the person out everywhere. */
  function disable(id: Uuid): Promise<AdminUser> {
    const gen = generation
    return act(id, () =>
      api.post<AdminUser>(`/api/admin/users/${id}/disable`).then((user) => {
        if (gen === generation) applyDisabled(user)
        return user
      }),
    )
  }

  /** POST /api/admin/users/{id}/enable: lets the person sign in again. */
  function enable(id: Uuid): Promise<AdminUser> {
    const gen = generation
    return act(id, () =>
      api.post<AdminUser>(`/api/admin/users/${id}/enable`).then((user) => {
        if (gen === generation) applyDisabled(user)
        return user
      }),
    )
  }

  /** POST /api/admin/users/{id}/sign-out: ends every session of the account (answers 204, so the row is patched here). */
  function signOutEverywhere(id: Uuid): Promise<void> {
    const gen = generation
    return act(id, () =>
      api.post<void>(`/api/admin/users/${id}/sign-out`).then(() => {
        if (gen !== generation) return
        const user = rows.value.find((u) => u.id === id)
        if (user) upsertLocal({ ...user, activeSessions: 0 })
      }),
    )
  }

  function reset(): void {
    generation++
    rows.value = []
    loading.value = false
    loadError.value = null
    busy.clear()
    inflight = null
  }

  return { all, loading, loadError, busy, reload, disable, enable, signOutEverywhere, reset }
})

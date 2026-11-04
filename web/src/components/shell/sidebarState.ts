// Sidebar state shared by the docked sidebar and the navigation drawer (3.4): which spaces are
// collapsed, per user, in localStorage["taskloom.sidebar.collapsed.<userId>"] as an array of space ids.
import { effectScope, shallowRef, watch } from 'vue'
import { readJson, userKey, writeJson } from '../../lib/storage'
import { useSession } from '../../stores/session'
import type { Uuid } from '../../types'

const collapsed = shallowRef<ReadonlySet<Uuid>>(new Set())
let userId: string | null = null
let started = false

function storageKey(id: string): string {
  return userKey('taskloom.sidebar.collapsed', id)
}

function load(id: string | null): void {
  userId = id
  const raw = id ? readJson<unknown>(storageKey(id), []) : []
  collapsed.value = new Set(Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string') : [])
}

function save(next: Set<Uuid>): void {
  collapsed.value = next
  if (userId) writeJson(storageKey(userId), [...next])
}

function start(): void {
  if (started) return
  started = true
  const session = useSession()
  // Module-wide: detached from whichever component asked first.
  effectScope(true).run(() => {
    watch(() => session.user?.id ?? null, load, { immediate: true })
  })
}

export interface SidebarState {
  isCollapsed(spaceId: Uuid): boolean
  setCollapsed(spaceId: Uuid, on: boolean): void
  toggle(spaceId: Uuid): void
  /** Forgets spaces that no longer exist anywhere, so storage doesn't grow forever. */
  prune(existing: ReadonlySet<Uuid>): void
}

function setCollapsed(spaceId: Uuid, on: boolean): void {
  if (collapsed.value.has(spaceId) === on) return
  const next = new Set(collapsed.value)
  if (on) next.add(spaceId)
  else next.delete(spaceId)
  save(next)
}

export function useSidebarState(): SidebarState {
  start()
  return {
    isCollapsed: (spaceId) => collapsed.value.has(spaceId),
    setCollapsed,
    toggle: (spaceId) => setCollapsed(spaceId, !collapsed.value.has(spaceId)),
    prune(existing) {
      const kept = [...collapsed.value].filter((id) => existing.has(id))
      if (kept.length !== collapsed.value.size) save(new Set(kept))
    },
  }
}

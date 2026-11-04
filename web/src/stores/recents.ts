// Recently opened tasks and lists, per user and workspace (10.2): the palette's empty state and
// the default list of quick create and Home's quick add. Stored in
// `taskloom.recents.v1.<userId>.<workspaceId>`; at most 10 tasks and 10 lists.
import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { readJson, writeJson } from '../lib/storage'
import type { Task, Uuid } from '../types'
import { useSession } from './session'
import { useWorkspaces } from './workspaces'

export interface RecentTask {
  id: Uuid
  title: string
  listId: Uuid
}

interface Stored {
  tasks: RecentTask[]
  lists: Uuid[]
  lastListId: Uuid | null
}

const MAX = 10

function isRecentTask(v: unknown): v is RecentTask {
  if (!v || typeof v !== 'object') return false
  const t = v as Record<string, unknown>
  return typeof t.id === 'string' && typeof t.title === 'string' && typeof t.listId === 'string'
}

function parse(raw: unknown): Stored {
  const empty: Stored = { tasks: [], lists: [], lastListId: null }
  if (!raw || typeof raw !== 'object') return empty
  const r = raw as Record<string, unknown>
  const tasks = Array.isArray(r.tasks)
    ? r.tasks.filter(isRecentTask).map((t) => ({ id: t.id, title: t.title, listId: t.listId })).slice(0, MAX)
    : []
  const lists = Array.isArray(r.lists) ? r.lists.filter((x): x is string => typeof x === 'string').slice(0, MAX) : []
  const lastListId = typeof r.lastListId === 'string' ? r.lastListId : null
  return { tasks, lists, lastListId }
}

export const useRecents = defineStore('recents', () => {
  const session = useSession()
  const workspaces = useWorkspaces()

  const tasks = ref<RecentTask[]>([])
  const lists = ref<Uuid[]>([])
  const lastListId = ref<Uuid | null>(null)
  let key: string | null = null

  function storageKey(): string | null {
    const userId = session.user?.id
    const wsId = workspaces.currentId
    return userId && wsId ? `taskloom.recents.v1.${userId}.${wsId}` : null
  }

  function reload(): void {
    key = storageKey()
    const stored = key ? parse(readJson<unknown>(key, null)) : parse(null)
    tasks.value = stored.tasks
    lists.value = stored.lists
    lastListId.value = stored.lastListId
  }

  function persist(): void {
    if (!key) return
    writeJson(key, { tasks: tasks.value, lists: lists.value, lastListId: lastListId.value } satisfies Stored)
  }

  watch(() => [session.user?.id ?? null, workspaces.currentId] as const, reload, { immediate: true })

  function touchTask(t: Pick<Task, 'id' | 'title' | 'listId'>): void {
    const first = tasks.value[0]
    if (first && first.id === t.id && first.title === t.title && first.listId === t.listId) return
    tasks.value = [{ id: t.id, title: t.title, listId: t.listId }, ...tasks.value.filter((x) => x.id !== t.id)].slice(0, MAX)
    persist()
  }

  function touchList(listId: Uuid): void {
    if (lists.value[0] === listId) return
    lists.value = [listId, ...lists.value.filter((x) => x !== listId)].slice(0, MAX)
    persist()
  }

  function setLastList(listId: Uuid): void {
    if (lastListId.value === listId) return
    lastListId.value = listId
    persist()
  }

  /** Forgets a task or a list (deleted, or gone from the workspace). */
  function forget(id: Uuid): void {
    const nextTasks = tasks.value.filter((t) => t.id !== id && t.listId !== id)
    const nextLists = lists.value.filter((x) => x !== id)
    const clearLast = lastListId.value === id
    if (nextTasks.length === tasks.value.length && nextLists.length === lists.value.length && !clearLast) return
    tasks.value = nextTasks
    lists.value = nextLists
    if (clearLast) lastListId.value = null
    persist()
  }

  /** Empties the in-memory copy (sign-out); storage is kept for the next sign-in. */
  function reset(): void {
    key = null
    tasks.value = []
    lists.value = []
    lastListId.value = null
  }

  return { tasks, lists, lastListId, touchTask, touchList, setLastList, forget, reset }
})

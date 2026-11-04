// State of the task detail that outlives one TaskDetail instance (J and K, a mode change, closing
// and reopening the panel): the last detail per task, so a task seen before renders at once;
// comments being sent, edited or deleted; uploads in progress; and unsent comment drafts. Pending
// deletes stay hidden until their undo toast commits them (9.2).
import { reactive } from 'vue'
import type { Attachment, Comment, TaskDetail, Uuid } from '../../types'

const MAX_CACHED = 30

interface CacheEntry {
  detail: TaskDetail
  /** Date.now() when the detail arrived. */
  at: number
}

/** Keyed by "<userId>:<taskId>", so a different user in the same tab never sees another's cache. */
const cache = new Map<string, CacheEntry>()

export function cachedDetail(userId: Uuid | undefined, taskId: Uuid): CacheEntry | undefined {
  return userId ? cache.get(`${userId}:${taskId}`) : undefined
}

export function cacheDetail(userId: Uuid | undefined, d: TaskDetail): void {
  if (!userId) return
  const key = `${userId}:${d.task.id}`
  cache.delete(key)
  cache.set(key, { detail: d, at: Date.now() })
  while (cache.size > MAX_CACHED) {
    const oldest = cache.keys().next().value
    if (oldest === undefined) break
    cache.delete(oldest)
  }
}

export function forgetDetail(userId: Uuid | undefined, taskId: Uuid): void {
  if (userId) cache.delete(`${userId}:${taskId}`)
}

/** A comment the server doesn't have yet (or whose POST answer hasn't been merged yet). */
export interface LocalComment extends Comment {
  sending: boolean
}

/** A file on its way to the server; `id` is local. */
export interface Upload {
  id: string
  taskId: Uuid
  file: File
  state: 'queued' | 'uploading'
}

export const detailState = reactive({
  /** Comments and attachments with a pending delete, hidden until committed (then for good) or undone. */
  hiddenComments: new Set<Uuid>(),
  hiddenAttachments: new Set<Uuid>(),
  /** Per task: comments sent from this tab that a refetch hasn't returned yet. */
  localComments: new Map<Uuid, LocalComment[]>(),
  /** Per comment: an edit the server has accepted or is saving, until a refetch shows it. */
  commentEdits: new Map<Uuid, { body: string; editedAt: string }>(),
  /** Per task: attachments uploaded from this tab that a refetch hasn't returned yet. */
  localAttachments: new Map<Uuid, Attachment[]>(),
  uploads: [] as Upload[],
  /** Per task: the comment composer's unsent text. */
  drafts: new Map<Uuid, string>(),
})

function byCreatedAt<T extends { createdAt: string; id: string }>(a: T, b: T): number {
  return Date.parse(a.createdAt) - Date.parse(b.createdAt) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}

/** The server's comments with this tab's sends, edits and pending deletes applied, oldest first. */
export function mergeComments(taskId: Uuid, server: readonly Comment[]): Comment[] {
  const seen = new Set<Uuid>()
  const out: Comment[] = []
  for (const c of server) {
    seen.add(c.id)
    if (detailState.hiddenComments.has(c.id)) continue
    const edit = detailState.commentEdits.get(c.id)
    out.push(edit ? { ...c, body: edit.body, editedAt: edit.editedAt } : c)
  }
  for (const c of detailState.localComments.get(taskId) ?? []) {
    if (seen.has(c.id) || detailState.hiddenComments.has(c.id)) continue
    const edit = detailState.commentEdits.get(c.id)
    out.push(edit ? { ...c, body: edit.body, editedAt: edit.editedAt } : c)
  }
  return out.sort(byCreatedAt)
}

/** The server's attachments with this tab's uploads and pending deletes applied, oldest first. */
export function mergeAttachments(taskId: Uuid, server: readonly Attachment[]): Attachment[] {
  const seen = new Set<Uuid>()
  const out: Attachment[] = []
  for (const a of server) {
    seen.add(a.id)
    if (!detailState.hiddenAttachments.has(a.id)) out.push(a)
  }
  for (const a of detailState.localAttachments.get(taskId) ?? []) {
    if (!seen.has(a.id) && !detailState.hiddenAttachments.has(a.id)) out.push(a)
  }
  return out.sort(byCreatedAt)
}

/** Drops local copies a fresh detail now holds. */
export function settleLocal(d: TaskDetail): void {
  const id = d.task.id
  const comments = new Map(d.comments.map((c) => [c.id, c]))
  const local = detailState.localComments.get(id)
  if (local) {
    const rest = local.filter((c) => c.sending || !comments.has(c.id))
    if (rest.length) detailState.localComments.set(id, rest)
    else detailState.localComments.delete(id)
  }
  for (const [commentId, edit] of detailState.commentEdits) {
    const c = comments.get(commentId)
    if (c && c.body === edit.body && c.editedAt) detailState.commentEdits.delete(commentId)
  }
  const files = new Set(d.attachments.map((a) => a.id))
  const added = detailState.localAttachments.get(id)
  if (added) {
    const rest = added.filter((a) => !files.has(a.id))
    if (rest.length) detailState.localAttachments.set(id, rest)
    else detailState.localAttachments.delete(id)
  }
}

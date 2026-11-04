// Comments (8.10): optimistic send with a client id, edit in place, and delete with Undo. The
// changes live in detailState until a refetch of the task shows them (settleLocal).
import { api, newId } from '../../api'
import { useDelayedDelete } from '../../composables/useDelayedDelete'
import { useToast } from '../../composables/useToast'
import { errorStatus } from '../../lib/async'
import { useSession } from '../../stores/session'
import { useTasks } from '../../stores/tasks'
import type { Comment, Uuid } from '../../types'
import { detailState, type LocalComment } from './taskDetailState'

/** The server's limit on a comment body (5.4). */
export const MAX_COMMENT_LENGTH = 10_000

function setLocal(taskId: Uuid, list: LocalComment[]): void {
  if (list.length) detailState.localComments.set(taskId, list)
  else detailState.localComments.delete(taskId)
}

export function useComments(): {
  send(taskId: Uuid, body: string, o?: { id?: Uuid }): void
  edit(c: Comment, body: string): void
  remove(c: Comment): void
  isSending(id: Uuid): boolean
} {
  const tasks = useTasks()
  const toast = useToast()
  const session = useSession()
  const delayed = useDelayedDelete()

  function isSending(id: Uuid): boolean {
    for (const list of detailState.localComments.values()) {
      const c = list.find((x) => x.id === id)
      if (c) return c.sending
    }
    return false
  }

  function send(taskId: Uuid, body: string, o: { id?: Uuid } = {}): void {
    const text = body.trim()
    const user = session.user
    if (!text || !user) return
    const id = o.id ?? newId()
    const local: LocalComment = {
      id,
      taskId,
      author: { id: user.id, email: user.email, displayName: user.displayName },
      body: text,
      createdAt: new Date().toISOString(),
      editedAt: null,
      sending: true,
    }
    setLocal(taskId, [...(detailState.localComments.get(taskId) ?? []).filter((c) => c.id !== id), local])
    tasks.adjustCounts(taskId, { commentCount: 1 })
    api.post<Comment>(`/api/tasks/${taskId}/comments`, { id, body: text }).then(
      (server) => {
        const list = detailState.localComments.get(taskId) ?? []
        setLocal(
          taskId,
          list.map((c) => (c.id === id ? { ...server, sending: false } : c)),
        )
        // A refetch picks the comment up from the server (settleLocal then drops the local copy).
        tasks.adjustCounts(taskId, {})
      },
      (err: unknown) => {
        const list = detailState.localComments.get(taskId) ?? []
        if (errorStatus(err) === 409) {
          // A Retry whose first attempt got through after all: the comment exists.
          setLocal(taskId, list.map((c) => (c.id === id ? { ...c, sending: false } : c)))
          return
        }
        setLocal(taskId, list.filter((c) => c.id !== id))
        tasks.adjustCounts(taskId, { commentCount: -1 })
        if (errorStatus(err) === 401) return
        // The text goes back into the composer, unless the user has started another comment.
        if (!detailState.drafts.get(taskId)?.trim()) detailState.drafts.set(taskId, text)
        toast.error(err, {
          retry: () => {
            if (detailState.drafts.get(taskId)?.trim() === text) detailState.drafts.delete(taskId)
            send(taskId, text, { id })
          },
        })
      },
    )
  }

  function edit(c: Comment, body: string): void {
    const text = body.trim()
    if (!text || text === c.body) return
    const previous = detailState.commentEdits.get(c.id)
    detailState.commentEdits.set(c.id, { body: text, editedAt: new Date().toISOString() })
    api.patch<Comment>(`/api/comments/${c.id}`, { body: text }).then(
      (server) => {
        detailState.commentEdits.set(c.id, { body: server.body, editedAt: server.editedAt ?? new Date().toISOString() })
        tasks.adjustCounts(c.taskId, {})
      },
      (err: unknown) => {
        if (previous) detailState.commentEdits.set(c.id, previous)
        else detailState.commentEdits.delete(c.id)
        toast.error(err, { retry: () => edit(c, text) })
      },
    )
  }

  /** Hides the comment and toasts "Comment deleted" with Undo; the DELETE goes when the toast closes. */
  function remove(c: Comment): void {
    delayed.schedule({
      key: `comment:${c.id}`,
      message: 'Comment deleted',
      hide: () => {
        detailState.hiddenComments.add(c.id)
        tasks.adjustCounts(c.taskId, { commentCount: -1 })
      },
      restore: () => {
        detailState.hiddenComments.delete(c.id)
        tasks.adjustCounts(c.taskId, { commentCount: 1 })
      },
      commit: async () => {
        await api.del(`/api/comments/${c.id}`)
      },
      beacon: { method: 'DELETE', url: `/api/comments/${c.id}` },
    })
  }

  return { send, edit, remove, isSending }
}

// Attachment uploads and deletes (8.10). Uploads outlive the TaskDetail that started them (J and
// K, closing the panel): they live in detailState and run three at a time. Each shows as a tile with
// a spinner until it finishes; a failure removes the tile and toasts with Retry.
import { ref } from 'vue'
import { api, newId } from '../../api'
import { useDelayedDelete } from '../../composables/useDelayedDelete'
import { errorMessage, useToast } from '../../composables/useToast'
import { isRasterImage, MAX_ATTACHMENT_BYTES, MAX_THUMBNAIL_BYTES } from '../../lib/files'
import { useTasks } from '../../stores/tasks'
import type { Attachment, Uuid } from '../../types'
import { detailState, type Upload } from './taskDetailState'

const MAX_CONCURRENT = 3

let running = 0

/** "Show" in the "Added … to attachments" toast: the task's attachments scroll into view. */
export const showAttachmentsRequest = ref<{ taskId: Uuid; at: number } | null>(null)

function removeUpload(id: string): void {
  const i = detailState.uploads.findIndex((u) => u.id === id)
  if (i !== -1) detailState.uploads.splice(i, 1)
}

/** Raster images up to 5 MB show as thumbnails; everything else as a file tile. */
export function isThumbnail(a: Attachment): boolean {
  return isRasterImage(a.contentType) && a.sizeBytes <= MAX_THUMBNAIL_BYTES
}

export function uploadsFor(taskId: Uuid): Upload[] {
  return detailState.uploads.filter((u) => u.taskId === taskId)
}

export function useAttachmentUploads(): {
  add(taskId: Uuid, files: File[], o?: { announce?: boolean }): void
  remove(a: Attachment): void
} {
  const tasks = useTasks()
  const toast = useToast()
  const delayed = useDelayedDelete()

  async function send(u: Upload): Promise<void> {
    const form = new FormData()
    form.append('file', u.file, u.file.name)
    try {
      const a = await api.upload<Attachment>(`/api/tasks/${u.taskId}/attachments`, form)
      removeUpload(u.id)
      detailState.localAttachments.set(u.taskId, [...(detailState.localAttachments.get(u.taskId) ?? []), a])
      tasks.adjustCounts(u.taskId, { attachmentCount: 1 })
    } catch (err) {
      removeUpload(u.id)
      // The toast names the file, so a failure among several uploads is clear (a 413 names it already, 9.8).
      if (err && typeof err === 'object') Object.assign(err, { fileName: u.file.name })
      const info = errorMessage(err)
      if (info.silent) return
      const name = `“${u.file.name}”`
      const message = info.message.includes(name)
        ? info.message
        : info.retryable
          ? `Couldn't upload ${name}. ${info.message.startsWith('Something went wrong') ? 'Try again.' : info.message}`
          : `Couldn't upload ${name}: ${info.message}`
      const retry = () => add(u.taskId, [u.file])
      toast.show({ message, tone: 'danger', action: info.retryable ? { label: 'Retry', run: retry } : undefined })
    }
  }

  function pump(): void {
    while (running < MAX_CONCURRENT) {
      const next = detailState.uploads.find((u) => u.state === 'queued')
      if (!next) return
      next.state = 'uploading'
      running++
      void send(next).finally(() => {
        running--
        pump()
      })
    }
  }

  function add(taskId: Uuid, files: File[], o: { announce?: boolean } = {}): void {
    const accepted: File[] = []
    for (const file of files) {
      if (file.size > MAX_ATTACHMENT_BYTES) {
        toast.show({ message: `“${file.name}” is larger than 25 MB.`, tone: 'danger' })
        continue
      }
      accepted.push(file)
    }
    if (!accepted.length) return
    for (const file of accepted) detailState.uploads.push({ id: newId(), taskId, file, state: 'queued' })
    if (o.announce) {
      const message =
        accepted.length === 1 ? `Added ${accepted[0].name} to attachments` : `Added ${accepted.length} files to attachments`
      toast.show({
        message,
        action: { label: 'Show', run: () => (showAttachmentsRequest.value = { taskId, at: Date.now() }) },
      })
    }
    pump()
  }

  /** Hides the attachment and toasts "Deleted “name”" with Undo; the DELETE goes when the toast closes. */
  function remove(a: Attachment): void {
    delayed.schedule({
      key: `attachment:${a.id}`,
      message: `Deleted “${a.fileName}”`,
      hide: () => {
        detailState.hiddenAttachments.add(a.id)
        tasks.adjustCounts(a.taskId, { attachmentCount: -1 })
      },
      restore: () => {
        detailState.hiddenAttachments.delete(a.id)
        tasks.adjustCounts(a.taskId, { attachmentCount: 1 })
      },
      commit: async () => {
        await api.del(`/api/attachments/${a.id}`)
        // The activity gains "removed <name>".
        tasks.adjustCounts(a.taskId, {})
      },
      beacon: { method: 'DELETE', url: `/api/attachments/${a.id}` },
    })
  }

  return { add, remove }
}

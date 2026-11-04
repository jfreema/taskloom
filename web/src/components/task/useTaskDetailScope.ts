// Where the task detail lives in the DOM (8.10).
// - scopeEl: where the panel keys and pastes work, the host's focus root (or the detail's own root
//   on the page). Files pasted there become attachments; the editor takes its own pastes first.
// - The drop zone: the host's aside or dialog (or the task page). `dropActive` shows the overlay.
import { computed, onBeforeUnmount, watch, type Ref } from 'vue'
import { pastedFiles, useFileDrop } from './useFileDrop'

export function useTaskDetailScope(
  root: Ref<HTMLElement | null>,
  o: {
    titleId: () => string
    /** Whether files may be added now (an editor of a live task). */
    enabled: () => boolean
    onFiles: (files: File[]) => void
  },
): { scopeEl: Ref<HTMLElement | null>; dropActive: Ref<boolean> } {
  const scopeEl = computed(() => root.value?.closest<HTMLElement>('[data-task-panel-root]') ?? root.value)
  const zone = computed(() => {
    const el = root.value
    if (!el) return null
    return (
      el.closest<HTMLElement>(`[aria-labelledby="${CSS.escape(o.titleId())}"]`) ??
      el.closest<HTMLElement>('[data-task-drop-zone]') ??
      el
    )
  })

  const { active: dropActive } = useFileDrop(zone, o)

  function onPaste(e: ClipboardEvent): void {
    if (e.defaultPrevented || !o.enabled()) return
    const files = pastedFiles(e.clipboardData)
    if (!files.length) return
    e.preventDefault()
    o.onFiles(files)
  }

  watch(
    scopeEl,
    (el, prev) => {
      prev?.removeEventListener('paste', onPaste)
      el?.addEventListener('paste', onPaste)
    },
    { immediate: true, flush: 'post' },
  )
  onBeforeUnmount(() => scopeEl.value?.removeEventListener('paste', onPaste))

  return { scopeEl, dropActive }
}

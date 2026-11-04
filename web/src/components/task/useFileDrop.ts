// Files dropped on and pasted into the task detail (8.10).
// - useFileDrop: the drop zone (the whole panel, or the task page). Files dragged over it show the
//   overlay; dropped, they go to `onFiles`. Drops the editor already took (defaultPrevented) only
//   hide the overlay.
// - A window guard calls preventDefault() on file drags, so a file dropped outside a zone never
//   makes the browser open it in place of the app. It is installed once, when this module loads with
//   the app shell (TaskPanelHost imports the task detail), so it covers every screen.
// - pastedFiles: the files of a paste, unless it also carries real rich text (a table copied from a
//   spreadsheet comes with a picture of itself; the text is what the user meant).
import { onBeforeUnmount, onMounted, ref, watch, type Ref } from 'vue'

function hasFiles(dt: DataTransfer | null): boolean {
  return !!dt && Array.from(dt.types).includes('Files')
}

export function filesOf(dt: DataTransfer | null): File[] {
  return dt ? Array.from(dt.files) : []
}

/** The files a paste should upload: none when it also holds rich text with visible text. */
export function pastedFiles(dt: DataTransfer | null): File[] {
  const files = filesOf(dt)
  if (!files.length || !dt) return []
  const html = dt.getData('text/html')
  if (html && html.replace(/<[^>]*>/g, '').replace(/&nbsp;|\s/g, '').length > 0) return []
  return files
}

// ---- Window guard ----

function onWindowDragOver(e: DragEvent): void {
  if (e.defaultPrevented || !hasFiles(e.dataTransfer)) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'none'
}

function onWindowDrop(e: DragEvent): void {
  if (hasFiles(e.dataTransfer)) e.preventDefault()
}

let guarded = false

/** Installs the window guard (once). */
export function installDropGuard(): void {
  if (guarded || typeof window === 'undefined') return
  guarded = true
  window.addEventListener('dragover', onWindowDragOver)
  window.addEventListener('drop', onWindowDrop)
}

installDropGuard()

// ---- Drop zone ----

export function useFileDrop(
  zone: Ref<HTMLElement | null>,
  o: { enabled: () => boolean; onFiles: (files: File[]) => void },
): { active: Ref<boolean> } {
  const active = ref(false)
  let depth = 0

  function reset(): void {
    depth = 0
    active.value = false
  }

  function onDragEnter(e: DragEvent): void {
    if (!hasFiles(e.dataTransfer) || !o.enabled()) return
    e.preventDefault()
    depth++
    active.value = true
  }

  function onDragOver(e: DragEvent): void {
    if (!hasFiles(e.dataTransfer) || !o.enabled()) return
    e.preventDefault()
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy'
    // A drag that entered before the zone existed (or was enabled) still shows the overlay.
    if (!active.value) {
      depth = Math.max(depth, 1)
      active.value = true
    }
  }

  function onDragLeave(e: DragEvent): void {
    if (!active.value) return
    depth = Math.max(0, depth - 1)
    // relatedTarget is null when the drag leaves the window.
    if (depth === 0 || e.relatedTarget === null) reset()
  }

  function onDrop(e: DragEvent): void {
    // The editor takes files dropped on it itself (handleDrop) and marks the event handled. While
    // the overlay shows it covers the editor, so that only happens without it.
    const handled = e.defaultPrevented
    reset()
    if (!hasFiles(e.dataTransfer)) return
    e.preventDefault()
    if (handled || !o.enabled()) return
    const files = filesOf(e.dataTransfer)
    if (files.length) o.onFiles(files)
  }

  function bind(el: HTMLElement): void {
    el.addEventListener('dragenter', onDragEnter)
    el.addEventListener('dragover', onDragOver)
    el.addEventListener('dragleave', onDragLeave)
    el.addEventListener('drop', onDrop)
  }

  function unbind(el: HTMLElement): void {
    el.removeEventListener('dragenter', onDragEnter)
    el.removeEventListener('dragover', onDragOver)
    el.removeEventListener('dragleave', onDragLeave)
    el.removeEventListener('drop', onDrop)
  }

  watch(
    zone,
    (el, prev) => {
      if (prev) unbind(prev)
      if (el) bind(el)
      reset()
    },
    { immediate: true, flush: 'post' },
  )

  function onWindowDragEnd(): void {
    reset()
  }

  onMounted(() => {
    window.addEventListener('dragend', onWindowDragEnd)
    window.addEventListener('drop', onWindowDragEnd)
  })
  onBeforeUnmount(() => {
    if (zone.value) unbind(zone.value)
    window.removeEventListener('dragend', onWindowDragEnd)
    window.removeEventListener('drop', onWindowDragEnd)
  })

  return { active }
}

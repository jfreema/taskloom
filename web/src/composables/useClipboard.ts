// Copy to the clipboard: navigator.clipboard when it is available (secure contexts), else a
// hidden textarea and execCommand('copy'). Resolves false when neither works; never rejects.

async function copy(text: string): Promise<boolean> {
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard && globalThis.isSecureContext !== false) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // Not focused, no permission, or not allowed here: fall back.
  }
  return copyWithTextarea(text)
}

function copyWithTextarea(text: string): boolean {
  if (typeof document === 'undefined') return false
  const active = document.activeElement instanceof HTMLElement ? document.activeElement : null
  const ta = document.createElement('textarea')
  ta.value = text
  ta.setAttribute('readonly', '')
  ta.setAttribute('aria-hidden', 'true')
  ta.tabIndex = -1
  Object.assign(ta.style, { position: 'fixed', top: '0', left: '0', width: '1px', height: '1px', opacity: '0', pointerEvents: 'none' })
  // #app may be inert while a dialog is open; the overlay root never is.
  const host = document.getElementById('overlay-root') ?? document.body
  host.appendChild(ta)
  let ok = false
  try {
    ta.select()
    ta.setSelectionRange(0, text.length)
    ok = document.execCommand('copy')
  } catch {
    ok = false
  } finally {
    ta.remove()
    active?.focus({ preventScroll: true })
  }
  return ok
}

export function useClipboard(): { copy(text: string): Promise<boolean> } {
  return { copy }
}

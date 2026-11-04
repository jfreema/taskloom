// Platform facts read once at start-up.

interface NavigatorWithUaData {
  userAgentData?: { platform?: string }
}

function detectApple(): boolean {
  if (typeof navigator === 'undefined') return false
  const uaPlatform = (navigator as Navigator & NavigatorWithUaData).userAgentData?.platform
  const platform = uaPlatform || navigator.platform || ''
  if (/mac|iphone|ipad|ipod/i.test(platform)) return true
  return /Mac OS X|iPhone|iPad|iPod/.test(navigator.userAgent || '')
}

function detectTouchOnly(): boolean {
  try {
    return typeof matchMedia === 'function' && matchMedia('(hover: none) and (pointer: coarse)').matches
  } catch {
    return false
  }
}

/** macOS and iOS: `Mod` is Cmd (Meta) there and Ctrl elsewhere (9.5). */
export const isApple: boolean = detectApple()

/** A device whose primary input is touch with no hover (phones, most tablets). */
export const isTouchOnly: boolean = detectTouchOnly()

/** Whether the primary pointer is coarse right now (re-evaluated on each call; a tablet may gain a mouse). */
export function isCoarsePointer(): boolean {
  try {
    return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches
  } catch {
    return false
  }
}

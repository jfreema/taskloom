// Style (Look 3): "loom" (the default) or "pelican", which dresses Taskloom in Pelican, the State of
// Louisiana's design system (styles/pelican.css). Saved in this browser like the theme and density:
// public/theme-init.js applies a stored "pelican" before first paint, and this keeps
// localStorage["taskloom.look"], <html data-look> and the browser's theme-color in step.
// The style is separate from the theme: each style has light and dark colours.
import { ref, type Ref } from 'vue'
import { readString, writeString } from '../lib/storage'

export type Look = 'loom' | 'pelican'

const STORAGE_KEY = 'taskloom.look'

/** The browser's toolbar tint: Pelican's black top bar in both themes; Loom's own canvas otherwise (index.html). */
const PELICAN_THEME_COLOR = '#000000'

function readLook(): Look {
  return readString(STORAGE_KEY) === 'pelican' ? 'pelican' : 'loom'
}

/** index.html's theme-color values, kept to put back when leaving Pelican. */
const loomThemeColors = new Map<HTMLMetaElement, string>()

function applyAttribute(l: Look): void {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  if (l === 'pelican') root.setAttribute('data-look', 'pelican')
  else root.removeAttribute('data-look')
  for (const meta of Array.from(document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]'))) {
    if (!loomThemeColors.has(meta)) loomThemeColors.set(meta, meta.content)
    meta.content = l === 'pelican' ? PELICAN_THEME_COLOR : (loomThemeColors.get(meta) ?? meta.content)
  }
}

// One shared state for the whole app.
const look = ref<Look>(readLook())
applyAttribute(look.value)

function setLook(l: Look): void {
  look.value = l
  writeString(STORAGE_KEY, l === 'loom' ? null : l)
  applyAttribute(l)
}

export function useLook(): { look: Ref<Look>; setLook(l: Look): void } {
  return { look, setLook }
}

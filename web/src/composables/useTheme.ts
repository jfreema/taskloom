// Theme preference (spec 7.3, 7.13). public/theme-init.js applies a stored explicit choice before
// first paint; this composable keeps localStorage["taskloom.theme"] and <html data-theme> in step.
// "System" removes the attribute (the CSS then follows prefers-color-scheme) and stores 'system'.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import type { ThemePreference } from '../types'
import { readString, writeString } from '../lib/storage'

const STORAGE_KEY = 'taskloom.theme'

function readPreference(): ThemePreference {
  const v = readString(STORAGE_KEY)
  return v === 'light' || v === 'dark' || v === 'system' ? v : 'system'
}

function applyAttribute(p: ThemePreference): void {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  if (p === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', p)
}

const darkQuery =
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

// One shared state for the whole app.
const preference = ref<ThemePreference>(readPreference())
const systemDark = ref<boolean>(darkQuery?.matches ?? false)
darkQuery?.addEventListener('change', (e) => {
  systemDark.value = e.matches
})
// The init script and this module must agree, even if storage was edited by another tab or by hand.
applyAttribute(preference.value)

const resolved = computed<'light' | 'dark'>(() =>
  preference.value === 'system' ? (systemDark.value ? 'dark' : 'light') : preference.value,
)

function setPreference(p: ThemePreference): void {
  preference.value = p
  writeString(STORAGE_KEY, p)
  applyAttribute(p)
}

export function useTheme(): {
  preference: Ref<ThemePreference>
  resolved: ComputedRef<'light' | 'dark'>
  setPreference(p: ThemePreference): void
} {
  return { preference, resolved, setPreference }
}

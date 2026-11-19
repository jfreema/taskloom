// Density (Look 2): "comfortable" (the default) or "compact", which tightens list rows, sidebar
// rows, Home rows and Board cards (styles/tokens.css). Saved in this browser like the theme:
// public/theme-init.js applies a stored "compact" before first paint, and this keeps
// localStorage["taskloom.density"] and <html data-density> in step.
import { ref, type Ref } from 'vue'
import { readString, writeString } from '../lib/storage'

export type Density = 'comfortable' | 'compact'

const STORAGE_KEY = 'taskloom.density'

function readDensity(): Density {
  return readString(STORAGE_KEY) === 'compact' ? 'compact' : 'comfortable'
}

function applyAttribute(d: Density): void {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  if (d === 'compact') root.setAttribute('data-density', 'compact')
  else root.removeAttribute('data-density')
}

const density = ref<Density>(readDensity())
applyAttribute(density.value)

function setDensity(d: Density): void {
  density.value = d
  writeString(STORAGE_KEY, d)
  applyAttribute(d)
}

export function useDensity(): { density: Ref<Density>; setDensity(d: Density): void } {
  return { density, setDensity }
}

// Keys that work only while a picker is open (9.5, scope "layer"): the status picker's 1–9 and the
// priority menu's 1–4 and 0. The shortcut is registered when the picker opens and removed when it
// closes, so the many closed pickers of a long list cost nothing. They are widget keys: they keep
// working when single-key shortcuts are turned off (9.5 lists the keys that setting turns off).
import { onBeforeUnmount, watch, type Ref } from 'vue'
import { useShortcut } from '../../composables/useShortcut'

export function useKeysWhileOpen(open: Ref<boolean>, keys: string[], handler: (e: KeyboardEvent) => void | boolean): void {
  let off: (() => void) | null = null
  function stop(): void {
    off?.()
    off = null
  }
  watch(
    open,
    (on) => {
      stop()
      if (on) off = useShortcut(keys, handler, { scope: 'layer', widget: true })
    },
    { immediate: true },
  )
  onBeforeUnmount(stop)
}

/** The digit of a number key, or null ("1" → 1). */
export function digitOf(e: KeyboardEvent): number | null {
  return /^[0-9]$/.test(e.key) ? Number(e.key) : null
}

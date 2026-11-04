// A flag that turns on only after `source` has stayed true for `delayMs` (default 200), and off at
// once: skeletons and loading bars that never flash for fast loads (8.1).
import { getCurrentScope, onScopeDispose, ref, watch, type Ref } from 'vue'

export function useDelayedFlag(source: Ref<boolean>, delayMs = 200): Ref<boolean> {
  const flag = ref(false)
  let timer: ReturnType<typeof setTimeout> | undefined

  function clear(): void {
    if (timer !== undefined) clearTimeout(timer)
    timer = undefined
  }

  watch(
    source,
    (on) => {
      clear()
      if (!on) {
        flag.value = false
        return
      }
      if (delayMs <= 0) {
        flag.value = true
        return
      }
      timer = setTimeout(() => {
        timer = undefined
        flag.value = true
      }, delayMs)
    },
    { immediate: true },
  )

  if (getCurrentScope()) onScopeDispose(clear)
  return flag
}

// A debounced function with cancel() and flush(). A pending call is cancelled when the owning
// component (effect scope) is disposed; call flush() in onBeforeUnmount to run it instead.
import { getCurrentScope, onScopeDispose } from 'vue'

// `any[]` is the generic bound of the 10.2 signature, so any function type fits.
export function useDebouncedFn<T extends (...a: any[]) => void>(fn: T, ms: number): T & { cancel(): void; flush(): void } {
  let timer: ReturnType<typeof setTimeout> | undefined
  let pendingArgs: Parameters<T> | null = null

  function run(): void {
    timer = undefined
    const args = pendingArgs
    pendingArgs = null
    if (args) fn(...args)
  }

  const debounced = ((...args: Parameters<T>) => {
    pendingArgs = args
    if (timer !== undefined) clearTimeout(timer)
    timer = setTimeout(run, ms)
  }) as T & { cancel(): void; flush(): void }

  debounced.cancel = () => {
    if (timer !== undefined) clearTimeout(timer)
    timer = undefined
    pendingArgs = null
  }

  debounced.flush = () => {
    if (timer === undefined) return
    clearTimeout(timer)
    run()
  }

  if (getCurrentScope()) onScopeDispose(() => debounced.cancel())
  return debounced
}

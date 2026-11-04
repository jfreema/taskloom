// A clock for relative times ("just now", "5m"): one 30s interval shared by every subscriber,
// running only while at least one component uses it.
import { onScopeDispose, readonly, ref, type Ref } from 'vue'

const TICK_MS = 30_000

const now = ref(new Date())
let users = 0
let timer: ReturnType<typeof setInterval> | undefined

export function useNow(): Readonly<Ref<Date>> {
  users++
  if (users === 1) {
    now.value = new Date()
    timer = setInterval(() => (now.value = new Date()), TICK_MS)
  }
  onScopeDispose(() => {
    users--
    if (users === 0 && timer !== undefined) {
      clearInterval(timer)
      timer = undefined
    }
  })
  return readonly(now) as Readonly<Ref<Date>>
}

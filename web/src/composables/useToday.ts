// Today's local date, shared app-wide. It moves at local midnight, and is re-checked when the
// window regains focus or the tab becomes visible (timers sleep while a laptop is closed).
import { ref, type Ref } from 'vue'
import { todayIso } from '../lib/dates'
import type { IsoDate } from '../types'

const today = ref<IsoDate>(todayIso())
let started = false
let timer: ReturnType<typeof setTimeout> | undefined

function check(): void {
  const now = todayIso()
  if (now !== today.value) today.value = now
}

function scheduleMidnight(): void {
  if (timer !== undefined) clearTimeout(timer)
  const now = new Date()
  const next = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 0, 0, 1)
  // Re-arm at least hourly, so a clock or time-zone change is picked up too.
  const wait = Math.min(Math.max(next.getTime() - now.getTime(), 1000), 3_600_000)
  timer = setTimeout(() => {
    check()
    scheduleMidnight()
  }, wait)
}

function start(): void {
  if (started || typeof window === 'undefined') return
  started = true
  scheduleMidnight()
  window.addEventListener('focus', () => {
    check()
    scheduleMidnight()
  })
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
      check()
      scheduleMidnight()
    }
  })
}

export function useToday(): Ref<IsoDate> {
  start()
  check()
  return today
}

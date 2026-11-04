// Viewport breakpoints (spec 7.5): phone < 640px, tablet 640-1023px, desktop >= 1024px.
// The task panel's docking is measured by TaskPanelHost, not decided here (8.10).
import { computed, ref, type ComputedRef, type Ref } from 'vue'

export const PHONE_MAX = 639
export const DESKTOP_MIN = 1024

const hasWindow = typeof window !== 'undefined'
const width = ref<number>(hasWindow ? window.innerWidth : DESKTOP_MIN)
const coarseQuery = hasWindow && typeof window.matchMedia === 'function' ? window.matchMedia('(pointer: coarse)') : null
const coarse = ref<boolean>(coarseQuery?.matches ?? false)

if (hasWindow) {
  let frame = 0
  window.addEventListener(
    'resize',
    () => {
      if (frame) return
      frame = requestAnimationFrame(() => {
        frame = 0
        width.value = window.innerWidth
      })
    },
    { passive: true },
  )
  coarseQuery?.addEventListener('change', (e) => {
    coarse.value = e.matches
  })
}

const isPhone = computed(() => width.value <= PHONE_MAX)
const isTablet = computed(() => width.value > PHONE_MAX && width.value < DESKTOP_MIN)
const isDesktop = computed(() => width.value >= DESKTOP_MIN)

export function useBreakpoint(): {
  width: Ref<number>
  isPhone: ComputedRef<boolean>
  isTablet: ComputedRef<boolean>
  isDesktop: ComputedRef<boolean>
  coarse: Ref<boolean>
} {
  return { width, isPhone, isTablet, isDesktop, coarse }
}

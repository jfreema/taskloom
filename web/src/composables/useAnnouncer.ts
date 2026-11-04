// Screen-reader announcements (spec 7.10 UiAnnouncer). UiAnnouncer, mounted once in App.vue,
// renders a polite and an assertive visually hidden live region; announce() writes to one of them.
// Repeating the same text still announces it: the region is cleared, then refilled a moment later.
// Both readings of the contract work: `useAnnouncer()('Moved')` and `useAnnouncer().announce('Moved')`.
import { readonly, ref, type Ref } from 'vue'

const polite = ref('')
const assertive = ref('')
const timers: { polite?: ReturnType<typeof setTimeout>; assertive?: ReturnType<typeof setTimeout> } = {}
const REFILL_MS = 60

function announce(text: string, o?: { assertive?: boolean }): void {
  const kind = o?.assertive ? 'assertive' : 'polite'
  const region = kind === 'assertive' ? assertive : polite
  clearTimeout(timers[kind])
  region.value = ''
  timers[kind] = setTimeout(() => {
    region.value = text
  }, REFILL_MS)
}

export type AnnounceFn = ((text: string, o?: { assertive?: boolean }) => void) & {
  announce: (text: string, o?: { assertive?: boolean }) => void
}

const callable: AnnounceFn = Object.assign((text: string, o?: { assertive?: boolean }) => announce(text, o), { announce })

export function useAnnouncer(): AnnounceFn {
  return callable
}

/** For UiAnnouncer only. */
export function useAnnouncerRegions(): { polite: Readonly<Ref<string>>; assertive: Readonly<Ref<string>> } {
  return { polite: readonly(polite), assertive: readonly(assertive) }
}

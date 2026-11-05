// Skeletons that follow one another (8.1, 8.2): App.vue's boot frame gives way to the shell's
// skeletons, and the shell's page skeleton to the page's own. A skeleton that mounts in the same
// render that removes another shows at once instead of after 200ms, so the screen never blinks
// empty in between. The window closes right after that render.
let open = false

/** Called as a skeleton leaves the screen. */
export function skeletonHandover(): void {
  open = true
  setTimeout(() => {
    open = false
  }, 0)
}

/** The delay for a skeleton that is set up now: none during a hand-over, else 200ms. */
export function skeletonDelay(): number {
  return open ? 0 : 200
}

// Progressive mounting on the Board (S10): how many of a column's cards render. A few more cards than
// last time (a new card, a move) render at once; when many appear together (opening a long list,
// clearing a search, "Show all N"), the column mounts one batch more per frame, side by side with the
// other columns, so the top of the board fills first: the first render mounts a screenful. Later
// batches are the column's share of lib/mountBudget's, which follows what a card costs on this device
// (each frame's step is timed).
import { computed, nextTick, onScopeDispose, ref, watch, type Ref } from 'vue'
import { boardCardBudget } from '../../lib/mountBudget'

/** Cards a column's first render may mount, about a screenful, so the first paint is complete. */
const FIRST_CARDS = 6

/**
 * `total` is how many cards the column shows; `columns` how many columns are open (they share a
 * batch).
 */
export function useMountedCards(total: () => number, columns: () => number): Readonly<Ref<number>> {
  const tick = ref(0)
  /** Cards the last evaluation let render, and how many more than before. Not reactive. */
  let rendered = 0
  let added = 0
  let frame = 0

  const batch = () => Math.max(1, Math.ceil(boardCardBudget.batch() / Math.max(1, columns())))

  const count = computed(() => {
    void tick.value
    const n = Math.min(total(), rendered + (rendered === 0 ? Math.max(FIRST_CARDS, batch()) : batch()))
    added = n - rendered
    rendered = n
    return n
  })

  /** One frame's step: one batch more, timed until its render is done. */
  function step(): void {
    frame = 0
    const at = performance.now()
    added = 0
    tick.value++
    void nextTick(() => boardCardBudget.record(added, performance.now() - at))
  }

  watch(
    count,
    (n) => {
      if (n < total() && !frame) frame = requestAnimationFrame(step)
    },
    { flush: 'post', immediate: true },
  )
  onScopeDispose(() => {
    if (frame) cancelAnimationFrame(frame)
  })

  return count
}

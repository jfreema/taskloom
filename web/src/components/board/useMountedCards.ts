// Progressive mounting on the Board (S10): how many of a column's cards render. A few more cards than
// last time (a new card, a move) render at once; when many appear together (opening a long list,
// clearing a search, "Show all N"), the column mounts one batch more at a time, each in a task of its
// own once the column has rendered the one before, side by side with the other columns, so the top of
// the board fills first: the first render mounts the top of each column. lib/mountBudget sizes a batch
// from what a card costs on this device (each batch is timed).
import { computed, onScopeDispose, ref, type Ref } from 'vue'
import { boardCardBudget, createMountStepper } from '../../lib/mountBudget'

/**
 * Cards a column's first render may mount: the top of the column, which the next batch completes.
 * Never more than the column's share of a batch: that render also builds the board (and takes down
 * the page before).
 */
const FIRST_CARDS = 3

export interface MountedCards {
  /** How many of the column's cards render now. */
  count: Readonly<Ref<number>>
  /** The column calls this after each render (mounted, updated): the next batch follows. */
  rendered(): void
}

/**
 * `total` is how many cards the column shows; `columns` how many columns are open. The column's own
 * batch runs in a task of its own and takes a whole batch; growth that reaches every column in the
 * same render (the first render, a cleared search) takes the column's share of one.
 */
export function useMountedCards(total: () => number, columns: () => number): MountedCards {
  const tick = ref(0)
  /** Cards the column last rendered, and the cards the last evaluation lets render. Not reactive. */
  let mounted = 0
  let cut = 0
  /** The next evaluation is the column's own batch. Not reactive. */
  let own = false

  const count = computed(() => {
    void tick.value
    const batch = boardCardBudget.batch()
    const share = Math.max(1, Math.ceil(batch / Math.max(1, columns())))
    const step = own ? batch : mounted === 0 ? Math.min(FIRST_CARDS, share) : share
    own = false
    cut = Math.min(total(), mounted + step)
    return cut
  })

  const stepper = createMountStepper(boardCardBudget, () => {
    own = true
    tick.value++
  })
  onScopeDispose(() => stepper.cancel())

  return {
    count,
    rendered() {
      const added = cut - mounted
      mounted = cut
      stepper.rendered(added, cut < total())
    },
  }
}

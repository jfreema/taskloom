// Progressive mounting (S10): when many rows or cards appear at once, long views mount them in
// batches, each in a task of its own, so that no single task builds hundreds of them and the page
// keeps answering input (no long task over 50ms). Frames render between batches whenever one is
// due, but batches don't wait for frames, so even a long list is complete well within a second.
// A batch is sized from what one item costs on this device, measured as items mount: it aims at
// TARGET_MS of script, which leaves room for a garbage collection and the frame's style and layout
// within the 50ms limit, even on a slower device than the one measured. The estimate lasts for the
// session, so a view opened again starts with a batch that fits.

/** Script time one batch aims at, in ms. */
const TARGET_MS = 12

/** Batches smaller than this say more about a render's fixed cost than about an item's. */
const MIN_SAMPLE = 4

export interface MountBudget {
  /** How many items the next batch may mount. */
  batch(): number
  /** Records a batch that mounted `count` items in `ms` of script. */
  record(count: number, ms: number): void
}

/** `first` is the batch before anything was measured; later batches stay within `min`..`max`. */
export function createMountBudget(first: number, min: number, max: number): MountBudget {
  /** Smoothed script time of one item; 0 until a batch has been measured. */
  let perItem = 0
  /** The last batch's time per item: a slow batch (a collection, richer items) shrinks the next one at once. */
  let last = 0
  return {
    batch() {
      const cost = Math.max(perItem, last)
      if (cost <= 0) return first
      return Math.max(min, Math.min(max, Math.floor(TARGET_MS / cost)))
    },
    record(count, ms) {
      if (count < MIN_SAMPLE || !(ms > 0)) return
      last = ms / count
      perItem = perItem > 0 ? perItem * 0.7 + last * 0.3 : last
    },
  }
}

type PostTask = (cb: () => void, options: { priority: 'user-visible'; signal: AbortSignal }) => Promise<unknown>

/**
 * Runs `cb` in a task of its own as soon as the main thread is free (input and due frames go
 * first). Returns a function that cancels it.
 */
export function postSlice(cb: () => void): () => void {
  const scheduler = (globalThis as unknown as { scheduler?: { postTask?: PostTask } }).scheduler
  if (scheduler?.postTask) {
    const controller = new AbortController()
    scheduler.postTask(cb, { priority: 'user-visible', signal: controller.signal }).catch(() => {})
    return () => controller.abort()
  }
  const id = setTimeout(cb, 0)
  return () => clearTimeout(id)
}

export interface MountStepper {
  /**
   * The view calls this after each render of its items (mounted, updated): `added` is how many more
   * items that render showed than the one before, `behind` whether more wait. It times the batch the
   * render completed, for the budget, and asks for the next one in a task of its own. Nothing
   * advances while nothing renders the items (a view still loading), so no batch is ever skipped.
   */
  rendered(added: number, behind: boolean): void
  /** Drops the waiting batch (the view unmounts, or starts over). */
  cancel(): void
}

/** Drives one progressive mount: `advance()` lets one batch more render (it bumps what the cut reads). */
export function createMountStepper(budget: MountBudget, advance: () => void): MountStepper {
  let pending: (() => void) | null = null
  /** When the batch now rendering started; 0 when the last render wasn't a batch. */
  let startedAt = 0
  function run(): void {
    pending = null
    startedAt = performance.now()
    advance()
  }
  return {
    rendered(added, behind) {
      if (startedAt) {
        budget.record(added, performance.now() - startedAt)
        startedAt = 0
      }
      if (behind) pending ??= postSlice(run)
    },
    cancel() {
      pending?.()
      pending = null
      startedAt = 0
    },
  }
}

// Before anything is measured (a first visit, whose code isn't compiled yet either), batches are small.

/** The List's task rows (useListModel). */
export const listRowBudget = createMountBudget(6, 4, 60)

/** The Board's cards (useMountedCards). */
export const boardCardBudget = createMountBudget(12, 4, 120)

/** Home's task rows (useHomeMounting). */
export const homeRowBudget = createMountBudget(6, 4, 80)

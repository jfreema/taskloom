// Progressive mounting (S10): when many rows or cards appear at once, long views mount them in
// batches, one batch per frame, so that no single task builds hundreds of them. A batch is sized from
// what one item costs on this device, measured as items mount: it aims at TARGET_MS of script per
// frame, which leaves room for style, layout and paint within the 50ms long-task limit. The estimate
// lasts for the session, so a view opened again starts with a batch that fits.

/** Script time one batch aims at, in ms. */
const TARGET_MS = 24

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
  return {
    batch() {
      if (perItem <= 0) return first
      return Math.max(min, Math.min(max, Math.floor(TARGET_MS / perItem)))
    },
    record(count, ms) {
      if (count < MIN_SAMPLE || !(ms > 0)) return
      const sample = ms / count
      perItem = perItem > 0 ? (perItem + sample) / 2 : sample
    },
  }
}

/** The List's task rows (useListModel). */
export const listRowBudget = createMountBudget(20, 4, 60)

/** The Board's cards, counted across all columns (useMountedCards). */
export const boardCardBudget = createMountBudget(24, 4, 120)

/** Home's task rows (useHomeMounting). */
export const homeRowBudget = createMountBudget(16, 4, 80)

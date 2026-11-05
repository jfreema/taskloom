// Progressive mounting on Home (S10): when many rows appear at once (the first load, expanding a long
// section, "Show N more"), the rows mount in batches in display order, each in a task of its own once
// the sections have rendered the one before, so no single long task builds hundreds of rows.
// lib/mountBudget sizes a batch from what a row costs on this device; each batch is timed. The first
// render mounts most of a screenful, and a few new rows (quick add, an edit) render at once.
import { computed, onScopeDispose, ref, watch, type Ref, type WatchSource } from 'vue'
import { createMountStepper, homeRowBudget } from '../../lib/mountBudget'
import type { HomeSectionKey } from './homeSections'

/**
 * Rows the first render may mount: most of a screenful, which the first batch completes. Never more
 * than a batch: that render also builds the page.
 */
const FIRST_ROWS = 12

export type MountedCounts = Partial<Record<HomeSectionKey, number>>

export interface HomeMounting {
  /** How many rows of each expanded section render now. */
  counts: Readonly<Ref<MountedCounts>>
  /** Rows are still mounting: the page is aria-busy until they all have. */
  mounting: Readonly<Ref<boolean>>
  /** The sections call this after each render of their rows (mounted, updated): the next batch follows. */
  rendered(): void
  /** The sections left the page (a state replaced them): they mount from the first batch again. */
  detached(): void
}

/**
 * `keys` are the expanded sections in display order and `rows(key)` how many rows each renders once
 * mounting is done. A change of `restart` (another scope or workspace: other rows altogether) mounts
 * from the first batch again.
 */
export function useHomeMounting(
  keys: Ref<HomeSectionKey[]>,
  rows: (key: HomeSectionKey) => number,
  restart: WatchSource,
): HomeMounting {
  const tick = ref(0)
  /** Rows the sections last rendered, the rows the last evaluation lets render, and whether more wait. Not reactive. */
  let mounted = 0
  let cut = 0
  let behind = false

  const stepper = createMountStepper(homeRowBudget, () => tick.value++)
  onScopeDispose(() => stepper.cancel())

  function reset(): void {
    mounted = 0
    cut = 0
    stepper.cancel()
    tick.value++
  }

  watch(restart, reset)

  const counts = computed<MountedCounts>(() => {
    void tick.value
    const batch = homeRowBudget.batch()
    const limit = mounted + (mounted === 0 ? Math.min(FIRST_ROWS, batch) : batch)
    const out: MountedCounts = {}
    let n = 0
    let total = 0
    for (const key of keys.value) {
      const len = rows(key)
      const take = Math.max(0, Math.min(len, limit - n))
      out[key] = take
      n += take
      total += len
    }
    cut = n
    behind = n < total
    return out
  })

  // `behind` changes only when `counts` is evaluated again.
  const mounting = computed(() => {
    void counts.value
    return behind
  })

  return {
    counts,
    mounting,
    rendered() {
      const added = cut - mounted
      mounted = cut
      stepper.rendered(added, behind)
    },
    detached: reset,
  }
}

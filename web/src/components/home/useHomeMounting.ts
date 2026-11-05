// Progressive mounting on Home (S10): when many rows appear at once (the first load, expanding a long
// section, "Show N more"), one batch of rows more renders per frame, in display order, so no single
// long task builds hundreds of rows. lib/mountBudget sizes the batch from what a row costs on this
// device; each frame's step is timed. The first render mounts a screenful, and a few new rows (quick
// add, an edit) render at once.
import { computed, nextTick, onScopeDispose, ref, watch, type Ref, type WatchSource } from 'vue'
import { homeRowBudget } from '../../lib/mountBudget'
import type { HomeSectionKey } from './homeSections'

/** Rows the first render may mount, about a screenful, so the first paint is complete. */
const FIRST_ROWS = 16

export type MountedCounts = Partial<Record<HomeSectionKey, number>>

/**
 * `keys` are the expanded sections in display order and `rows(key)` how many rows each renders once
 * mounting is done. Returns how many of them render now. A change of `restart` (another scope or
 * workspace: other rows altogether) mounts from the first batch again.
 */
export function useHomeMounting(
  keys: Ref<HomeSectionKey[]>,
  rows: (key: HomeSectionKey) => number,
  restart: WatchSource,
): Readonly<Ref<MountedCounts>> {
  const tick = ref(0)
  /** Rows the last evaluation let render, how many more than before, and whether some wait. Not reactive. */
  let rendered = 0
  let added = 0
  let behind = false
  let frame = 0

  watch(restart, () => {
    rendered = 0
    tick.value++
  })

  const counts = computed<MountedCounts>(() => {
    void tick.value
    const batch = homeRowBudget.batch()
    const limit = rendered + (rendered === 0 ? Math.max(FIRST_ROWS, batch) : batch)
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
    added = n - rendered
    rendered = n
    behind = n < total
    return out
  })

  /** One frame's step: one batch more, timed until its render is done. */
  function step(): void {
    frame = 0
    const at = performance.now()
    added = 0
    tick.value++
    void nextTick(() => homeRowBudget.record(added, performance.now() - at))
  }

  watch(
    counts,
    () => {
      if (behind && !frame) frame = requestAnimationFrame(step)
    },
    { flush: 'post', immediate: true },
  )
  onScopeDispose(() => {
    if (frame) cancelAnimationFrame(frame)
  })

  return counts
}

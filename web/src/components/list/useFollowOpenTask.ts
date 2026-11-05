// The open task's row comes into view (J/K in the panel, the palette) without taking focus (8.7). A
// row that hasn't mounted yet (progressive mounting, S10) comes into view once it has: the table calls
// rendered() after each render.
import { nextTick, watch, type Ref } from 'vue'
import type { Uuid } from '../../types'

export function useFollowOpenTask(
  root: Ref<HTMLElement | null>,
  openId: () => Uuid | null,
  mounting: Readonly<Ref<boolean>>,
): { rendered(): void } {
  /** The open task whose row is still to mount. */
  let pending: Uuid | null = null

  function reveal(id: Uuid): boolean {
    const row = root.value?.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(id)}"]`)
    row?.scrollIntoView?.({ block: 'nearest' })
    return !!row
  }

  watch(
    openId,
    (id) => {
      pending = null
      if (!id) return
      void nextTick(() => {
        if (!reveal(id) && mounting.value) pending = id
      })
    },
    { immediate: true },
  )

  return {
    rendered() {
      if (pending && (reveal(pending) || !mounting.value)) pending = null
    },
  }
}

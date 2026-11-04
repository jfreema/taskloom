// The submenu state of one UiMenuLevel: which submenu item is open, the row it is anchored to,
// where its focus starts, and hover intent. Resting 150ms on a submenu item opens its submenu;
// moving onto any other row of the level closes the open one at once.
import { computed, getCurrentInstance, onBeforeUnmount, ref, shallowRef, type ComputedRef, type Ref } from 'vue'
import type { MenuItem, SubmenuItem } from './menu'

export type FocusStart = 'first' | 'last' | 'container' | 'none'

const HOVER_MS = 150

export function useSubmenu(o: {
  byId: ComputedRef<ReadonlyMap<string, MenuItem>>
  /** The level's list element, where the submenu items' rows are. */
  list: Ref<HTMLElement | null>
  /** Phones: submenus are shown in place of the list (`enter`), not beside it. */
  inline: () => boolean
  enter: (item: SubmenuItem) => void
  /** Moves focus to the open submenu's first item. */
  focusOpen: () => void
}) {
  const subId = ref<string | null>(null)
  const anchor = shallowRef<HTMLElement | null>(null)
  const start = ref<FocusStart>('none')
  let timer: ReturnType<typeof setTimeout> | undefined
  let pendingId: string | null = null

  const item = computed(() => {
    const found = subId.value ? o.byId.value.get(subId.value) : undefined
    return found?.kind === 'submenu' ? found : null
  })

  /** For the submenu's v-model:open: it closing clears the open item. */
  const open = computed({
    get: () => !!item.value,
    set: (v: boolean) => {
      if (!v) subId.value = null
    },
  })

  function cancelHover(): void {
    clearTimeout(timer)
    timer = undefined
    pendingId = null
  }

  function show(next: SubmenuItem, how: 'keyboard' | 'pointer' | 'hover'): void {
    cancelHover()
    if (o.inline()) return o.enter(next)
    if (subId.value === next.id) {
      if (how === 'keyboard') o.focusOpen()
      return
    }
    anchor.value = o.list.value?.querySelector<HTMLElement>(`[data-menu-id="${CSS.escape(next.id)}"]`) ?? null
    start.value = how === 'keyboard' ? 'first' : 'none'
    subId.value = next.id
  }

  /** Closes the open submenu and drops a pending hover. */
  function leave(): void {
    cancelHover()
    subId.value = null
  }

  /** The pointer moved over a row of the level. */
  function hover(over: MenuItem): void {
    if (over.kind !== 'submenu' || over.disabled) return leave()
    if (subId.value === over.id) return cancelHover()
    subId.value = null
    if (pendingId === over.id) return
    cancelHover()
    pendingId = over.id
    timer = setTimeout(() => show(over, 'hover'), HOVER_MS)
  }

  if (getCurrentInstance()) onBeforeUnmount(cancelHover)

  return { subId, anchor, start, item, open, cancelHover, show, leave, hover }
}

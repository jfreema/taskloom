// The palette's active option and keys (8.11). The first result is active (not the "Couldn't search
// tasks" row, so Enter still takes the best local match) until the keyboard or the pointer picks
// another. Up and Down wrap, Home and End apply while the query is empty, and Enter activates
// (Mod+Enter opens a task as a full page). "Show all N results" keeps the active row in its place.
import { computed, nextTick, ref, type ComputedRef, type Ref } from 'vue'
import { isApple } from '../../lib/platform'
import type { PaletteOption } from './usePaletteItems'

export interface PaletteNav {
  active: ComputedRef<PaletteOption | null>
  /** Back to the first result (a new query, or the palette opening). */
  reset(): void
  onKeydown(e: KeyboardEvent): void
  onPointerMove(o: PaletteOption): void
  onClick(o: PaletteOption, e: MouseEvent): void
}

export function usePaletteNav(o: {
  options: Ref<PaletteOption[]>
  query: Ref<string>
  domId(option: PaletteOption): string
}): PaletteNav {
  const activeKey = ref<string | null>(null)
  const active = computed<PaletteOption | null>(
    () =>
      o.options.value.find((x) => x.key === activeKey.value) ??
      o.options.value.find((x) => x.kind !== 'retry') ??
      o.options.value[0] ??
      null,
  )

  function setActive(option: PaletteOption | null | undefined): void {
    if (!option) return
    activeKey.value = option.key
    void nextTick(() => document.getElementById(o.domId(option))?.scrollIntoView?.({ block: 'nearest' }))
  }

  function move(delta: 1 | -1): void {
    const list = o.options.value
    if (!list.length) return
    const i = active.value ? list.indexOf(active.value) : -1
    setActive(list[(i + delta + list.length) % list.length])
  }

  function activate(option: PaletteOption | null, mod: boolean): void {
    if (!option) return
    if (option.kind === 'more') {
      // The first newly shown task takes the row's place.
      const i = o.options.value.indexOf(option)
      option.run({ mod })
      setActive(o.options.value[i])
      return
    }
    option.run({ mod })
  }

  function onKeydown(e: KeyboardEvent): void {
    if (e.isComposing || e.keyCode === 229) return
    const mod = isApple ? e.metaKey : e.ctrlKey
    switch (e.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        e.preventDefault()
        move(e.key === 'ArrowDown' ? 1 : -1)
        break
      case 'Home':
      case 'End': {
        if (o.query.value) return
        e.preventDefault()
        const list = o.options.value
        setActive(e.key === 'Home' ? list[0] : list[list.length - 1])
        break
      }
      case 'Enter':
        e.preventDefault()
        activate(active.value, mod)
        break
    }
  }

  function onPointerMove(option: PaletteOption): void {
    if (activeKey.value !== option.key && active.value?.key !== option.key) activeKey.value = option.key
  }

  function onClick(option: PaletteOption, e: MouseEvent): void {
    activate(option, isApple ? e.metaKey : e.ctrlKey)
  }

  return {
    active,
    reset: () => {
      activeKey.value = null
    },
    onKeydown,
    onPointerMove,
    onClick,
  }
}

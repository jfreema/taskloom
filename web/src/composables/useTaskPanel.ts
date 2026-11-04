// The task panel's URL state (8.10). `?task=<uuid>` on Home and list pages opens the panel;
// TaskPanelHost (A2) renders it and reports its mode here. Opening uses router.replace, except in
// sheet mode (phones), where it pushes an entry marked `taskloomSheet` so the browser's Back closes it.
import { computed, effectScope, nextTick, ref, shallowRef, type ComputedRef, type Ref } from 'vue'
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'
import type { Uuid } from '../types'
import { appRouter } from './appRouter'

export type TaskPanelMode = 'docked' | 'modal' | 'sheet' | null

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const PANEL_ROUTES = new Set(['home', 'list'])
const SHEET_MAX = 639

/** Set by TaskPanelHost whenever it measures. */
const mode = ref<TaskPanelMode>(null)
/** The visible task order of the current view (setOrder). */
const order = shallowRef<Uuid[]>([])
/**
 * Incremented by each `open()` that should move focus into the panel (all of them, except
 * `{ focus: false }`: a mouse click on another row while the docked panel is open, 8.10).
 * TaskPanelHost watches it and focuses the panel root.
 */
export const panelFocusRequest = ref<{ id: Uuid; at: number } | null>(null)

interface PanelState {
  openId: ComputedRef<Uuid | null>
  isOpen: ComputedRef<boolean>
  hasNext: ComputedRef<boolean>
  hasPrev: ComputedRef<boolean>
}

let state: PanelState | null = null

function taskQuery(route: RouteLocationNormalizedLoaded): Uuid | null {
  if (typeof route.name !== 'string' || !PANEL_ROUTES.has(route.name)) return null
  const raw = route.query.task
  const value = Array.isArray(raw) ? raw[0] : raw
  return typeof value === 'string' && UUID.test(value) ? value : null
}

function ensureState(router: Router): PanelState {
  if (state) return state
  // Module-wide computeds, detached from whichever component asked first.
  const scope = effectScope(true)
  state = scope.run(() => {
    const openId = computed(() => taskQuery(router.currentRoute.value))
    const index = computed(() => (openId.value ? order.value.indexOf(openId.value) : -1))
    return {
      openId,
      isOpen: computed(() => openId.value !== null),
      hasNext: computed(() => index.value >= 0 && index.value < order.value.length - 1),
      hasPrev: computed(() => index.value > 0),
    }
  })!
  return state
}

function isSheetEntry(): boolean {
  const s: unknown = typeof history !== 'undefined' ? history.state : null
  return !!s && typeof s === 'object' && (s as { taskloomSheet?: unknown }).taskloomSheet === true
}

function inSheetMode(): boolean {
  if (mode.value) return mode.value === 'sheet'
  return typeof window !== 'undefined' && window.innerWidth <= SHEET_MAX
}

function navigate(router: Router, id: Uuid, o: { focus: boolean; replaceOnly: boolean }): void {
  const route = router.currentRoute.value
  if (o.focus) panelFocusRequest.value = { id, at: Date.now() }
  if (typeof route.name !== 'string' || !PANEL_ROUTES.has(route.name)) {
    void router.push(`/t/${id}`).catch(() => undefined)
    return
  }
  if (taskQuery(route) === id) return
  const location = { query: { ...route.query, task: id } }
  if (!o.replaceOnly && inSheetMode() && !isSheetEntry()) {
    void router.push({ ...location, state: { taskloomSheet: true } }).catch(() => undefined)
  } else {
    void router.replace(location).catch(() => undefined)
  }
}

function focusAfterClose(id: Uuid): void {
  void nextTick(() => {
    const row = document.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(id)}"]`)
    const target = row ?? document.getElementById('main')
    target?.focus({ preventScroll: !!row })
  })
}

export function useTaskPanel(): {
  openId: ComputedRef<Uuid | null>
  isOpen: ComputedRef<boolean>
  mode: Ref<TaskPanelMode>
  open(id: Uuid, o?: { focus?: boolean }): void
  close(): void
  next(): void
  prev(): void
  hasNext: ComputedRef<boolean>
  hasPrev: ComputedRef<boolean>
  setOrder(ids: Uuid[]): void
} {
  const router = appRouter()
  const s: PanelState = router
    ? ensureState(router)
    : {
        openId: computed(() => null),
        isOpen: computed(() => false),
        hasNext: computed(() => false),
        hasPrev: computed(() => false),
      }

  function open(id: Uuid, o?: { focus?: boolean }): void {
    const r = appRouter()
    if (r) navigate(r, id, { focus: o?.focus !== false, replaceOnly: false })
  }

  function close(): void {
    const r = appRouter()
    if (!r) return
    const route = r.currentRoute.value
    const id = taskQuery(route)
    if (!id) return
    if (isSheetEntry()) {
      const stop = r.afterEach(() => {
        stop()
        focusAfterClose(id)
      })
      r.back()
      return
    }
    const query = { ...route.query }
    delete query.task
    void r
      .replace({ query })
      .then(() => focusAfterClose(id))
      .catch(() => undefined)
  }

  function step(delta: 1 | -1): void {
    const r = appRouter()
    const id = s.openId.value
    if (!r || !id) return
    const i = order.value.indexOf(id)
    const nextId = i >= 0 ? order.value[i + delta] : undefined
    // J and K keep focus in the panel and always replace (8.10).
    if (nextId) navigate(r, nextId, { focus: false, replaceOnly: true })
  }

  function setOrder(ids: Uuid[]): void {
    const same = ids.length === order.value.length && ids.every((id, i) => id === order.value[i])
    if (!same) order.value = [...ids]
  }

  return {
    openId: s.openId,
    isOpen: s.isOpen,
    mode,
    open,
    close,
    next: () => step(1),
    prev: () => step(-1),
    hasNext: s.hasNext,
    hasPrev: s.hasPrev,
    setOrder,
  }
}

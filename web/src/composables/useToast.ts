// Toasts (spec 7.10 UiToastHost, 9.2, 9.8). One queue for the whole app; UiToastHost renders it.
// - At most 3 are visible, newest at the bottom; a fourth closes the oldest non-danger toast ('replaced').
// - Defaults: 5s, 8s with an action, 10s for danger; 0 keeps a toast until it is dismissed.
// - Showing an existing id replaces that toast in place. A danger message shown again within 2s
//   reuses the existing toast; its Retry then retries both failures, and both onClose callbacks run.
// - Hover or focus on the region pauses every timer (UiToastHost calls pauseAll / resumeAll).
import { computed, shallowRef, type ComputedRef } from 'vue'

export type ToastTone = 'neutral' | 'success' | 'danger'
export type ToastCloseReason = 'timeout' | 'dismiss' | 'action' | 'replaced'

export interface ToastAction {
  label: string
  run: () => void
}

export interface ToastOptions {
  message: string
  /** 'neutral' by default. */
  tone?: ToastTone
  /** Undo, Retry, Open. */
  action?: ToastAction
  /** ms; 5000, 8000 with an action, 10000 for danger; 0 = until dismissed. */
  duration?: number
  /** Showing an existing id replaces that toast. */
  id?: string
  onClose?: (reason: ToastCloseReason) => void
}

/** What UiToastHost renders. */
export interface ToastItem {
  readonly id: string
  readonly message: string
  readonly tone: ToastTone
  readonly action?: ToastAction
  readonly duration: number
  readonly createdAt: number
  /** Created by undoable(): Mod+Z runs the newest one (undoLatest). */
  readonly undo: boolean
}

interface Entry {
  item: ToastItem
  onClose?: (reason: ToastCloseReason) => void
  timer: ReturnType<typeof setTimeout> | null
  remaining: number
  startedAt: number
}

const MAX_VISIBLE = 3
const DANGER_DEDUPE_MS = 2000
/** After a pause, a toast stays at least this long. */
const MIN_AFTER_PAUSE_MS = 1500

const items = shallowRef<readonly ToastItem[]>([])
const entries = new Map<string, Entry>()
const pausedBy = new Set<string>()
let seq = 0

function startTimer(entry: Entry): void {
  if (entry.timer) clearTimeout(entry.timer)
  entry.timer = null
  if (entry.item.duration <= 0 || pausedBy.size > 0) return
  entry.startedAt = Date.now()
  const id = entry.item.id
  entry.timer = setTimeout(() => close(id, 'timeout'), Math.max(entry.remaining, 0))
}

function stopTimer(entry: Entry): void {
  if (!entry.timer) return
  clearTimeout(entry.timer)
  entry.timer = null
  entry.remaining -= Date.now() - entry.startedAt
}

function close(id: string, reason: ToastCloseReason): void {
  const entry = entries.get(id)
  if (!entry) return
  if (entry.timer) clearTimeout(entry.timer)
  entries.delete(id)
  items.value = items.value.filter((t) => t.id !== id)
  entry.onClose?.(reason)
}

/** One action that runs both: two failures folded into one toast keep both retries. */
function mergeActions(a: ToastAction | undefined, b: ToastAction | undefined): ToastAction | undefined {
  if (!a || !b || a.run === b.run) return a ?? b
  return {
    label: a.label,
    run: () => {
      a.run()
      b.run()
    },
  }
}

function showInternal(o: ToastOptions, undo: boolean): string {
  const tone = o.tone ?? 'neutral'
  if (tone === 'danger' && !o.id) {
    const now = Date.now()
    const twin = items.value.find((t) => t.tone === 'danger' && t.message === o.message && now - t.createdAt < DANGER_DEDUPE_MS)
    const entry = twin ? entries.get(twin.id) : undefined
    if (twin && entry) {
      const item: ToastItem = { ...twin, action: mergeActions(twin.action, o.action), createdAt: now }
      entry.item = item
      const first = entry.onClose
      const second = o.onClose
      if (second) {
        entry.onClose = (reason) => {
          first?.(reason)
          second(reason)
        }
      }
      items.value = items.value.map((t) => (t.id === twin.id ? item : t))
      entry.remaining = twin.duration
      startTimer(entry)
      return twin.id
    }
  }
  const id = o.id ?? `toast-${++seq}`
  const duration = o.duration ?? (tone === 'danger' ? 10000 : o.action ? 8000 : 5000)
  const item: ToastItem = { id, message: o.message, tone, action: o.action, duration, createdAt: Date.now(), undo }
  const entry: Entry = { item, onClose: o.onClose, timer: null, remaining: duration, startedAt: Date.now() }
  const previous = entries.get(id)
  if (previous) {
    // Replace in place: same id, same slot.
    if (previous.timer) clearTimeout(previous.timer)
    entries.set(id, entry)
    items.value = items.value.map((t) => (t.id === id ? item : t))
    startTimer(entry)
    previous.onClose?.('replaced')
    return id
  }
  entries.set(id, entry)
  items.value = [...items.value, item]
  startTimer(entry)
  while (items.value.length > MAX_VISIBLE) {
    const others = items.value.filter((t) => t.id !== id)
    const victim = others.find((t) => t.tone !== 'danger') ?? others[0]
    if (!victim) break
    close(victim.id, 'replaced')
  }
  return id
}

function show(o: ToastOptions): string {
  return showInternal(o, false)
}

function dismiss(id: string): void {
  close(id, 'dismiss')
}

function success(message: string): string {
  return show({ message, tone: 'success' })
}

interface ErrorLike {
  status?: unknown
  isNetwork?: unknown
  problem?: { detail?: unknown } | null
  fileName?: unknown
  name?: unknown
  message?: unknown
}

/**
 * The user-facing sentence for an error (9.8), shared with forms that show errors inline.
 * `retryable` marks the cases that offer Retry; `silent` is a 401, which redirects instead.
 */
export function errorMessage(err: unknown): { message: string; retryable: boolean; silent: boolean } {
  const e = (err && typeof err === 'object' ? err : {}) as ErrorLike
  const status = typeof e.status === 'number' ? e.status : null
  const detail = typeof e.problem?.detail === 'string' && e.problem.detail.trim() ? e.problem.detail : null
  const network = e.isNetwork === true || status === 0 || (err instanceof TypeError && status === null)
  if (network) {
    return { message: "You're offline or the server can't be reached. Your change wasn't saved.", retryable: true, silent: false }
  }
  if (status === 401) return { message: '', retryable: false, silent: true }
  if (status === 413) {
    const name = typeof e.fileName === 'string' && e.fileName ? `“${e.fileName}”` : 'This file'
    return { message: `${name} is larger than 25 MB.`, retryable: false, silent: false }
  }
  if ((status === 400 || status === 409) && detail) return { message: detail, retryable: false, silent: false }
  if (status === 403) return { message: "You don't have permission to change this.", retryable: false, silent: false }
  if (status === 404) return { message: 'This item no longer exists.', retryable: false, silent: false }
  if (status !== null && status >= 400 && status < 500) {
    return { message: detail ?? "That change wasn't accepted.", retryable: false, silent: false }
  }
  return { message: 'Something went wrong on our side. Try again.', retryable: true, silent: false }
}

function error(err: unknown, o?: { retry?: () => void }): string {
  const { message, retryable, silent } = errorMessage(err)
  if (silent) return ''
  const action = retryable && o?.retry ? { label: 'Retry', run: o.retry } : undefined
  return show({ message, tone: 'danger', action })
}

function isThenable(v: unknown): v is PromiseLike<unknown> {
  return !!v && (typeof v === 'object' || typeof v === 'function') && typeof (v as { then?: unknown }).then === 'function'
}

function undoable(
  message: string,
  undo: () => void | Promise<void>,
  o?: { onCommit?: () => void; duration?: number },
): string {
  return showInternal(
    {
      message,
      duration: o?.duration ?? 8000,
      action: {
        label: 'Undo',
        run: () => {
          const result: unknown = undo()
          // Stores report their own failures with an error toast; never leave a rejection unhandled
          // (any thenable, not only a native Promise).
          if (isThenable(result)) result.then(undefined, () => undefined)
        },
      },
      onClose: (reason) => {
        if (reason !== 'action') o?.onCommit?.()
      },
    },
    true,
  )
}

/** Runs a toast's action: the toast closes with reason 'action' first, then the action runs. */
function runAction(id: string): void {
  const item = items.value.find((t) => t.id === id)
  const action = item?.action
  if (!action) return
  close(id, 'action')
  action.run()
}

function undoLatest(): boolean {
  for (let i = items.value.length - 1; i >= 0; i--) {
    const t = items.value[i]
    if (t.undo && t.action) {
      runAction(t.id)
      return true
    }
  }
  return false
}

function pauseAll(source: string): void {
  const wasRunning = pausedBy.size === 0
  pausedBy.add(source)
  if (wasRunning) for (const entry of entries.values()) stopTimer(entry)
}

function resumeAll(source: string): void {
  if (!pausedBy.delete(source) || pausedBy.size > 0) return
  for (const entry of entries.values()) {
    entry.remaining = Math.max(entry.remaining, MIN_AFTER_PAUSE_MS)
    startTimer(entry)
  }
}

export function useToast(): {
  show(o: ToastOptions): string
  dismiss(id: string): void
  success(message: string): string
  error(err: unknown, o?: { retry?: () => void }): string
  undoable(message: string, undo: () => void | Promise<void>, o?: { onCommit?: () => void; duration?: number }): string
  undoLatest(): boolean
} {
  return { show, dismiss, success, error, undoable, undoLatest }
}

/** For UiToastHost only. */
export function useToastHost(): {
  toasts: ComputedRef<readonly ToastItem[]>
  runAction(id: string): void
  dismiss(id: string): void
  pauseAll(source: string): void
  resumeAll(source: string): void
} {
  return { toasts: computed(() => items.value), runAction, dismiss, pauseAll, resumeAll }
}

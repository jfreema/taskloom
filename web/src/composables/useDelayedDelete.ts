// Delayed delete with Undo (9.2). `schedule` hides the item at once and shows an undo toast; when
// the toast closes for any reason other than Undo, `commit` sends the delete. Pending deletes are
// sent with `api.beacon` on `pagehide`, and committed by `flushAll()` on sign-out and workspace switch.
import { api } from '../api'
import { errorStatus } from '../lib/async'
import { useToast } from './useToast'

export interface DelayedDeleteBeacon {
  method: 'DELETE' | 'POST'
  url: string
  body?: unknown
}

export interface DelayedDeleteOptions {
  /** Identifies the item: scheduling the same key again commits the earlier entry first. */
  key: string
  /** The toast text: "Deleted “Write onboarding guide”", "Deleted 3 tasks", "Comment deleted". */
  message: string
  /** Marks the item pending; getters exclude it. Runs at once. */
  hide(): void
  /** Shows the item again (Undo, or a failed commit). */
  restore(): void
  /** Sends the delete. A 404 counts as success; any other failure restores the item and toasts. */
  commit(): Promise<void>
  /** What `pagehide` sends with `fetch` keepalive. */
  beacon: DelayedDeleteBeacon
  /** More beacons for the same item (a bulk delete over 500 ids). */
  beacons?: DelayedDeleteBeacon[]
}

type EntryState = 'pending' | 'committing' | 'done'

interface Entry {
  o: DelayedDeleteOptions
  state: EntryState
  toastId: string
  promise: Promise<void> | null
  /** The beacon went out on pagehide; if the page comes back from the bfcache, commit normally. */
  beaconed: boolean
}

const entries = new Map<string, Entry>()
let listening = false

function commitEntry(entry: Entry): Promise<void> {
  if (entry.state !== 'pending') return entry.promise ?? Promise.resolve()
  entry.state = 'committing'
  // Closing the toast runs its onCommit, which finds the entry already committing.
  if (entry.toastId) useToast().dismiss(entry.toastId)
  entry.promise = (async () => {
    try {
      await entry.o.commit()
    } catch (err) {
      const status = errorStatus(err)
      // 404: already gone. 401: signed out, so the delete can't succeed and is dropped (9.4).
      if (status !== 404 && status !== 401) {
        try {
          entry.o.restore()
        } finally {
          useToast().error(err)
        }
      }
    } finally {
      entry.state = 'done'
      if (entries.get(entry.o.key) === entry) entries.delete(entry.o.key)
    }
  })()
  return entry.promise
}

function undo(entry: Entry): void {
  if (entry.state !== 'pending') return
  entry.state = 'done'
  if (entries.get(entry.o.key) === entry) entries.delete(entry.o.key)
  entry.o.restore()
}

function schedule(o: DelayedDeleteOptions): void {
  ensureListeners()
  const existing = entries.get(o.key)
  if (existing && existing.state === 'pending') void commitEntry(existing)
  const entry: Entry = { o, state: 'pending', toastId: '', promise: null, beaconed: false }
  entries.set(o.key, entry)
  o.hide()
  entry.toastId = useToast().undoable(o.message, () => undo(entry), {
    onCommit: () => {
      void commitEntry(entry)
    },
  })
}

/** Commits every pending delete now and waits for them (sign-out, workspace switch). Never rejects. */
async function flushAll(): Promise<void> {
  const all = [...entries.values()]
  await Promise.all(all.map((e) => commitEntry(e).catch(() => undefined)))
}

/** Sends every pending delete with `api.beacon` (pagehide). The entries stay until the page is gone. */
function flushAllBeacon(): void {
  for (const entry of entries.values()) {
    if (entry.state !== 'pending' || entry.beaconed) continue
    entry.beaconed = true
    for (const b of [entry.o.beacon, ...(entry.o.beacons ?? [])]) api.beacon(b.method, b.url, b.body)
  }
}

function onPageShow(e: PageTransitionEvent): void {
  if (!e.persisted) return
  // Back from the bfcache: the beacons were sent, so finish those entries the normal way
  // (a second DELETE answers 404, which counts as success).
  for (const entry of [...entries.values()]) if (entry.beaconed) void commitEntry(entry)
}

function ensureListeners(): void {
  if (listening || typeof window === 'undefined') return
  listening = true
  window.addEventListener('pagehide', flushAllBeacon)
  window.addEventListener('pageshow', onPageShow)
}

export function useDelayedDelete(): {
  schedule(o: DelayedDeleteOptions): void
  flushAll(): Promise<void>
  flushAllBeacon(): void
} {
  ensureListeners()
  return { schedule, flushAll, flushAllBeacon }
}

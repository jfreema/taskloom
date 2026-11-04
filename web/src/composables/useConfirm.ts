// Confirm dialogs (spec 7.10 UiConfirmHost). One confirm at a time; further calls queue.
// Both readings of the contract work: `const confirm = useConfirm(); await confirm({...})`
// and `const { confirm } = useConfirm(); await confirm({...})`.
import { computed, shallowRef, type ComputedRef } from 'vue'

export interface ConfirmOptions {
  /** "Delete list?" */
  title: string
  /** "Deletes 3 tasks. This can't be undone." */
  message?: string
  /** A verb: "Delete list". */
  confirmLabel: string
  /** "Cancel" by default. */
  cancelLabel?: string
  /** A danger button and role="alertdialog". */
  danger?: boolean
  /** Must be typed exactly (trimmed, case-sensitive) before the confirm button enables. */
  requireText?: string
}

export interface PendingConfirm {
  readonly id: number
  readonly options: ConfirmOptions
}

interface Entry extends PendingConfirm {
  resolve: (ok: boolean) => void
}

export type ConfirmFn = ((o: ConfirmOptions) => Promise<boolean>) & {
  confirm: (o: ConfirmOptions) => Promise<boolean>
}

const queue = shallowRef<readonly Entry[]>([])
let seq = 0

function confirm(o: ConfirmOptions): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    queue.value = [...queue.value, { id: ++seq, options: o, resolve }]
  })
}

function settle(id: number, ok: boolean): void {
  const entry = queue.value.find((e) => e.id === id)
  if (!entry) return
  queue.value = queue.value.filter((e) => e.id !== id)
  entry.resolve(ok)
}

const callable: ConfirmFn = Object.assign((o: ConfirmOptions) => confirm(o), { confirm })

export function useConfirm(): ConfirmFn {
  return callable
}

/** For UiConfirmHost only. */
export function useConfirmHost(): {
  next: ComputedRef<PendingConfirm | null>
  settle(id: number, ok: boolean): void
} {
  return { next: computed(() => queue.value[0] ?? null), settle }
}

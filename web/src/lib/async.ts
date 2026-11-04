// Promise helpers for store actions. Views often call actions without awaiting them; a failed call
// has already been reported with a toast, so its rejection must never surface as an unhandled
// rejection (the e2e suite fails on any `pageerror`, 9.8).

export function noop(): void {}

/**
 * Marks the promise as handled: callers that await it (or use then/catch/finally) still see the
 * rejection, callers that ignore it never trigger `unhandledrejection`.
 *
 * It returns a promise-like view rather than `p` itself because Pinia wraps every action and, when
 * the action returns a real Promise, hands the caller a new chain that rejects again (for
 * `$onAction`): a caller that ignored that chain would get an unhandled rejection. Pinia passes
 * any other value through unchanged.
 */
export function handled<T>(p: Promise<T>): Promise<T> {
  p.catch(noop)
  const view = {
    then: p.then.bind(p),
    catch: p.catch.bind(p),
    finally: p.finally.bind(p),
    [Symbol.toStringTag]: 'Promise',
  }
  return view as unknown as Promise<T>
}

/** True for the AbortError a `fetch` with an aborted signal rejects with. */
export function isAbortError(err: unknown): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === 'AbortError'
}

/** The HTTP status of an ApiError-like value, or null. */
export function errorStatus(err: unknown): number | null {
  if (typeof err !== 'object' || err === null) return null
  const status = (err as { status?: unknown }).status
  return typeof status === 'number' ? status : null
}

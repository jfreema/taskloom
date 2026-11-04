// HTTP client for the Taskloom API (spec section 5). Same origin; the session is the HTTP-only
// cookie `taskloom.auth`. Every request carries `X-Taskloom: 1` (the CSRF guard of 5.1).
import type { ProblemDetails, Uuid } from './types'

/** A failed API call. `status` is 0 for network failures and aborted uploads. */
export class ApiError extends Error {
  status: number
  problem?: ProblemDetails
  /** True when the server could not be reached (offline, DNS, CORS, connection reset). */
  isNetwork: boolean

  constructor(status: number, message: string, o: { problem?: ProblemDetails; isNetwork?: boolean; cause?: unknown } = {}) {
    super(message, o.cause === undefined ? undefined : { cause: o.cause })
    this.name = 'ApiError'
    this.status = status
    this.problem = o.problem
    this.isNetwork = o.isNetwork ?? false
  }
}

let onUnauthorized: () => void = () => {}

/** Called on every 401 (except requests made with `ignoreUnauthorized`); the app redirects to sign-in. */
export function setUnauthorizedHandler(fn: () => void): void {
  onUnauthorized = fn
}

interface RequestOptions {
  signal?: AbortSignal
  /** Skip the unauthorized handler on a 401 (the session probe of `session.load()`). */
  ignoreUnauthorized?: boolean
}

function isAbort(err: unknown): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === 'AbortError'
}

async function readProblem(res: Response): Promise<ProblemDetails | undefined> {
  try {
    const text = await res.text()
    if (!text) return undefined
    const parsed: unknown = JSON.parse(text)
    if (parsed && typeof parsed === 'object') return parsed as ProblemDetails
  } catch {
    // Not JSON: the status line is all we have.
  }
  return undefined
}

async function request<T>(method: string, url: string, body: unknown, o: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { 'X-Taskloom': '1', Accept: 'application/json' }
  let payload: BodyInit | undefined
  if (body instanceof FormData) payload = body
  else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }

  let res: Response
  try {
    res = await fetch(url, { method, headers, body: payload, credentials: 'same-origin', signal: o.signal })
  } catch (err) {
    // An aborted request is not an API failure: callers check `err.name === 'AbortError'`.
    if (isAbort(err)) throw err
    throw new ApiError(0, 'Network request failed.', { isNetwork: true, cause: err })
  }

  if (res.status === 401) {
    if (!o.ignoreUnauthorized) onUnauthorized()
    throw new ApiError(401, 'Signed out.')
  }
  if (!res.ok) {
    const problem = await readProblem(res)
    const message = problem?.detail || problem?.title || res.statusText || `Request failed with status ${res.status}.`
    throw new ApiError(res.status, message, { problem })
  }
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api: {
  get<T>(url: string, o?: { signal?: AbortSignal; ignoreUnauthorized?: boolean }): Promise<T>
  /** Sends `{}` when `body` is undefined. */
  post<T>(url: string, body?: unknown): Promise<T>
  patch<T>(url: string, body: unknown): Promise<T>
  del(url: string): Promise<void>
  upload<T>(url: string, form: FormData, o?: { signal?: AbortSignal }): Promise<T>
  /** A request that survives page unload: `fetch` with `keepalive` and the X-Taskloom header. Never throws. */
  beacon(method: 'DELETE' | 'POST', url: string, body?: unknown): void
} = {
  get: <T>(url: string, o?: RequestOptions) => request<T>('GET', url, undefined, o),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  patch: <T>(url: string, body: unknown) => request<T>('PATCH', url, body),
  del: (url: string) => request<void>('DELETE', url, undefined),
  upload: <T>(url: string, form: FormData, o?: { signal?: AbortSignal }) => request<T>('POST', url, form, o),
  beacon(method: 'DELETE' | 'POST', url: string, body?: unknown): void {
    try {
      const headers: Record<string, string> = { 'X-Taskloom': '1' }
      if (body !== undefined) headers['Content-Type'] = 'application/json'
      void fetch(url, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: 'same-origin',
        keepalive: true,
      }).catch(() => {})
    } catch {
      // keepalive quota exceeded or fetch unavailable: the item survives, which is the safe way to fail (9.2).
    }
  },
}

function uuidFromRandomValues(): Uuid {
  const b = new Uint8Array(16)
  crypto.getRandomValues(b)
  b[6] = (b[6] & 0x0f) | 0x40 // version 4
  b[8] = (b[8] & 0x3f) | 0x80 // RFC 4122 variant
  const h = Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

/**
 * Client-generated ids, so a row exists in the store before the server answers.
 * `crypto.randomUUID` needs a secure context; a dev server opened over plain http on the LAN isn't one.
 */
export const newId: () => Uuid = () =>
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function' && globalThis.isSecureContext !== false
    ? crypto.randomUUID()
    : uuidFromRandomValues()

/** Builds "?a=1&b=x" from the given values, skipping null, undefined and empty strings. "" when nothing is left. */
export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue
    search.append(key, String(value))
  }
  const s = search.toString()
  return s ? `?${s}` : ''
}

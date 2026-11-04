// localStorage access for every `taskloom.*` key (10.1). Storage can be missing, full or blocked
// (private mode, quotas, sandboxed iframes), so every call is wrapped and falls back quietly.

function store(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

export function readString(key: string): string | null {
  try {
    return store()?.getItem(key) ?? null
  } catch {
    return null
  }
}

/** Writes a string; `null` removes the key. */
export function writeString(key: string, v: string | null): void {
  try {
    const s = store()
    if (!s) return
    if (v === null) s.removeItem(key)
    else s.setItem(key, v)
  } catch {
    // Quota exceeded or storage blocked: the setting simply isn't remembered.
  }
}

export function removeKey(key: string): void {
  writeString(key, null)
}

/** Parses a JSON value; returns `fallback` when the key is missing or holds invalid JSON. */
export function readJson<T>(key: string, fallback: T): T {
  const raw = readString(key)
  if (raw === null) return fallback
  try {
    return JSON.parse(raw) as T
  } catch {
    return fallback
  }
}

export function writeJson(key: string, value: unknown): void {
  try {
    writeString(key, JSON.stringify(value))
  } catch {
    // A value that can't be serialised is not stored.
  }
}

/** Builds a per-user key: userKey('taskloom.view', userId, listId, 'board') → "taskloom.view.<userId>.<listId>.board". */
export function userKey(base: string, userId: string, ...parts: string[]): string {
  return [base, userId, ...parts].join('.')
}

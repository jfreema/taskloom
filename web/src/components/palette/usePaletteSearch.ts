// The palette's task search (8.11): GET /api/workspaces/{id}/tasks?scope=all&q=…&includeDone=true
// &limit=20, debounced 150ms. A newer query aborts the request before it (the server stops it too,
// 5.5). The results answer `resultsFor`; until a new answer arrives the old one stays, so the list
// doesn't flash empty between keystrokes.
import { getCurrentScope, onScopeDispose, ref, shallowRef, watch, type Ref, type ShallowRef } from 'vue'
import { api, query as toQuery } from '../../api'
import { isAbortError } from '../../lib/async'
import { useWorkspaces } from '../../stores/workspaces'
import type { Task, WorkspaceTasksResponse } from '../../types'

export const SEARCH_DEBOUNCE_MS = 150
export const SEARCH_LIMIT = 20
/** The API's limit on `q` (5.5). */
const MAX_QUERY = 200

export interface PaletteSearch {
  results: ShallowRef<Task[]>
  /** The (trimmed) query the results answer; '' before the first answer. */
  resultsFor: Ref<string>
  /** Waiting for the debounce or the server. */
  searching: Ref<boolean>
  /** The last search failed. */
  failed: Ref<boolean>
  retry(): void
}

export function usePaletteSearch(o: { query: Ref<string>; active: Ref<boolean> }): PaletteSearch {
  const workspaces = useWorkspaces()
  const results = shallowRef<Task[]>([])
  const resultsFor = ref('')
  const searching = ref(false)
  const failed = ref(false)
  let timer: ReturnType<typeof setTimeout> | undefined
  let controller: AbortController | null = null

  function cancel(): void {
    clearTimeout(timer)
    timer = undefined
    controller?.abort()
    controller = null
  }

  function run(q: string): void {
    const wsId = workspaces.currentId
    if (!wsId) return
    controller?.abort()
    const c = new AbortController()
    controller = c
    searching.value = true
    const url = `/api/workspaces/${wsId}/tasks${toQuery({ scope: 'all', q, includeDone: true, limit: SEARCH_LIMIT })}`
    api
      .get<WorkspaceTasksResponse>(url, { signal: c.signal })
      .then((res) => {
        if (controller !== c) return
        results.value = res.tasks
        resultsFor.value = q
        failed.value = false
      })
      .catch((err: unknown) => {
        if (controller !== c || isAbortError(err)) return
        results.value = []
        resultsFor.value = q
        failed.value = true
      })
      .finally(() => {
        if (controller !== c) return
        controller = null
        searching.value = false
      })
  }

  function reset(): void {
    cancel()
    results.value = []
    resultsFor.value = ''
    searching.value = false
    failed.value = false
  }

  watch(
    [() => o.query.value.trim().slice(0, MAX_QUERY), o.active, () => workspaces.currentId] as const,
    ([q, on], prev) => {
      if (!on || !q) {
        reset()
        return
      }
      clearTimeout(timer)
      searching.value = true
      // Opening with a query (openPalette(q)) searches at once; typing waits for the debounce.
      const justOpened = !prev || !prev[1]
      if (justOpened) {
        run(q)
        return
      }
      timer = setTimeout(() => {
        timer = undefined
        run(q)
      }, SEARCH_DEBOUNCE_MS)
    },
    { immediate: true },
  )

  function retry(): void {
    const q = o.query.value.trim().slice(0, MAX_QUERY)
    if (!q) return
    failed.value = false
    run(q)
  }

  if (getCurrentScope()) onScopeDispose(cancel)

  return { results, resultsFor, searching, failed, retry }
}

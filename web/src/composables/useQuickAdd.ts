// Type-to-fill quick add (Look 1) for one title input: the live reading of the text (lib/quickAdd)
// against the workspace's tags and the people who may be assigned in the target list, the parts the
// person dismissed, and what a create sends. Used by Home's quick add, the List's add rows and the
// New task dialog.
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { newId } from '../api'
import { noop } from '../lib/async'
import { parseQuickAdd, tokenKey, type QuickParse, type QuickPerson, type QuickToken } from '../lib/quickAdd'
import { useMembers } from '../stores/members'
import { useSession } from '../stores/session'
import { useTags } from '../stores/tags'
import { useWorkspaces } from '../stores/workspaces'
import type { CreateTaskRequest, Uuid } from '../types'
import { useToday } from './useToday'

export interface QuickAdd {
  parsed: ComputedRef<QuickParse>
  tokens: ComputedRef<QuickToken[]>
  /** Keeps a token's text in the title. */
  dismiss(token: QuickToken): void
  /** Forgets the dismissals (after a create or when the text is cleared). */
  reset(): void
  /**
   * The typed fields merged over `base` for a create: the typed due date and priority win, typed
   * tags and assignees are added. New tags are created now (optimistically; the task create waits
   * for them). A start date after the due date moves to the due date.
   */
  apply(base: Partial<CreateTaskRequest>): Partial<CreateTaskRequest> & { title: string }
}

export function useQuickAdd(text: Ref<string>, listId: Readonly<Ref<Uuid | null>>): QuickAdd {
  const tags = useTags()
  const members = useMembers()
  const workspaces = useWorkspaces()
  const session = useSession()
  const today = useToday()
  void tags.ensure()
  void members.ensure()

  const ignored = ref<ReadonlySet<string>>(new Set())

  const people = computed<QuickPerson[]>(() => {
    const only = listId.value ? workspaces.assignableIn([listId.value]) : null
    return members.current
      .filter((m) => !only || only.has(m.userId))
      .map((m) => ({ id: m.userId, name: m.displayName, email: m.email }))
  })

  const parsed = computed(() =>
    parseQuickAdd(text.value, {
      today: today.value,
      tags: tags.current,
      people: people.value,
      meId: session.user?.id ?? null,
      canCreateTags: workspaces.canEdit(),
      ignored: ignored.value,
    }),
  )
  const tokens = computed(() => parsed.value.tokens)

  function dismiss(token: QuickToken): void {
    ignored.value = new Set([...ignored.value, tokenKey(token.kind, token.text)])
  }

  function reset(): void {
    if (ignored.value.size) ignored.value = new Set()
  }

  watch(text, (t) => {
    if (!t.trim()) reset()
  })

  function apply(base: Partial<CreateTaskRequest>): Partial<CreateTaskRequest> & { title: string } {
    const p = parsed.value
    const out: Partial<CreateTaskRequest> & { title: string } = { ...base, title: p.title }
    if (p.dueDate) out.dueDate = p.dueDate
    if (p.priority) out.priority = p.priority
    const created: Uuid[] = []
    for (const name of p.newTags) {
      const existing = tags.current.find((t) => t.name.trim().toLocaleLowerCase() === name.toLocaleLowerCase())
      if (existing) {
        created.push(existing.id)
        continue
      }
      const id = newId()
      void tags.create({ id, name }).catch(noop)
      created.push(id)
    }
    const tagIds = [...new Set([...(base.tagIds ?? []), ...p.tagIds, ...created])]
    if (tagIds.length) out.tagIds = tagIds
    const assigneeIds = [...new Set([...(base.assigneeIds ?? []), ...p.assigneeIds])]
    if (assigneeIds.length) out.assigneeIds = assigneeIds
    if (out.startDate && out.dueDate && out.startDate > out.dueDate) out.startDate = out.dueDate
    return out
  }

  return { parsed, tokens, dismiss, reset, apply }
}

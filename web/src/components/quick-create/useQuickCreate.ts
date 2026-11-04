// Quick create's state (8.12): the defaults rule, the fields, creating (one task, or one per pasted
// line) and what follows: Create more keeps the dialog open with the title and description cleared,
// otherwise it closes with "Task created" and Open. `recents.setLastList` runs either way.
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { newId } from '../../api'
import { useConfirm } from '../../composables/useConfirm'
import { useTaskPanel } from '../../composables/useTaskPanel'
import { useToast } from '../../composables/useToast'
import { noop } from '../../lib/async'
import { plainTextToHtml } from '../../lib/html'
import { MAX_PASTED_TASKS, splitPastedLines } from '../../lib/paste'
import { readString, writeString } from '../../lib/storage'
import { useRecents } from '../../stores/recents'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { CreateTaskRequest, IsoDate, Priority, Uuid } from '../../types'

const MORE_KEY = 'taskloom.quickCreate.more'
const TITLE_MAX = 500

export interface QuickCreate {
  title: Ref<string>
  description: Ref<string>
  listId: Ref<Uuid | null>
  statusId: Ref<Uuid | null>
  assigneeIds: Ref<Uuid[]>
  tagIds: Ref<Uuid[]>
  priority: Ref<Priority>
  dueDate: Ref<IsoDate | null>
  startDate: Ref<IsoDate | null>
  more: Ref<boolean>
  canCreate: ComputedRef<boolean>
  /** Fills the fields from the defaults rule; called each time the dialog opens. */
  reset(): void
  /** Creates the task. Returns its id, or null when nothing was created. */
  create(o: { open: boolean }): Uuid | null
  /** A paste of two or more lines into the title: true when it was handled (the paste is prevented). */
  pasteLines(text: string): boolean
}

/** "“a”, “b”, “c” and 2 more"; titles past 60 characters end in an ellipsis. */
function pasteMessage(lines: string[]): string {
  const quote = (s: string) => `“${s.length > 60 ? `${s.slice(0, 59).trimEnd()}…` : s}”`
  const shown = lines.slice(0, 3).map(quote)
  const rest = lines.length - shown.length
  if (rest > 0) return `${shown.join(', ')} and ${rest} more`
  return shown.length > 1 ? `${shown.slice(0, -1).join(', ')} and ${shown[shown.length - 1]}` : (shown[0] ?? '')
}

export function useQuickCreate(o: { focusTitle(): void }): QuickCreate {
  const ui = useUi()
  const tasks = useTasks()
  const workspaces = useWorkspaces()
  const recents = useRecents()
  const panel = useTaskPanel()
  const toast = useToast()
  const confirm = useConfirm()
  const route = useRoute()
  const router = useRouter()

  const title = ref('')
  const description = ref('')
  const listId = ref<Uuid | null>(null)
  const statusId = ref<Uuid | null>(null)
  const assigneeIds = ref<Uuid[]>([])
  const tagIds = ref<Uuid[]>([])
  const priority = ref<Priority>('None')
  const dueDate = ref<IsoDate | null>(null)
  const startDate = ref<IsoDate | null>(null)
  const parentTaskId = ref<Uuid | null>(null)
  const more = ref(readString(MORE_KEY) === 'on')
  watch(more, (on) => writeString(MORE_KEY, on ? 'on' : 'off'))

  function inWorkspace(id: Uuid | null | undefined): Uuid | null {
    return id && workspaces.allLists.some((x) => x.list.id === id) ? id : null
  }

  function statusOk(id: Uuid | null | undefined, list: Uuid | null): id is Uuid {
    return !!id && !!list && workspaces.statusById(id)?.listId === list
  }

  /** The list rule: the defaults, else the list page's list, else the last list used, else the first list. */
  function defaultList(d: { listId?: Uuid }): Uuid | null {
    const pageList =
      (route.name === 'list' || route.name === 'list-default') && typeof route.params.listId === 'string'
        ? route.params.listId
        : null
    return (
      inWorkspace(d.listId) ??
      inWorkspace(pageList) ??
      inWorkspace(recents.lastListId) ??
      workspaces.allLists[0]?.list.id ??
      null
    )
  }

  function reset(): void {
    const d = ui.quickCreate.defaults
    const list = defaultList(d)
    listId.value = list
    statusId.value = statusOk(d.statusId, list) ? d.statusId : (list ? (workspaces.defaultStatusFor(list)?.id ?? null) : null)
    title.value = (d.title ?? '').slice(0, TITLE_MAX)
    description.value = ''
    assigneeIds.value = [...(d.assigneeIds ?? [])]
    tagIds.value = [...(d.tagIds ?? [])]
    priority.value = d.priority ?? 'None'
    dueDate.value = d.dueDate ?? null
    startDate.value = d.startDate ?? null
    parentTaskId.value = d.parentTaskId ?? null
  }

  // A new list: keep the status only when it belongs there; a parent from another list goes.
  watch(listId, (list) => {
    if (!list) return
    if (!statusOk(statusId.value, list)) statusId.value = workspaces.defaultStatusFor(list)?.id ?? null
    const parent = parentTaskId.value ? tasks.get(parentTaskId.value) : null
    if (parentTaskId.value && (!parent || parent.listId !== list)) parentTaskId.value = null
  })

  const canCreate = computed(() => !!title.value.trim() && !!listId.value && workspaces.canEdit())

  function request(name: string): CreateTaskRequest {
    const r: CreateTaskRequest = { id: newId(), title: name.slice(0, TITLE_MAX) }
    if (statusId.value) r.statusId = statusId.value
    if (parentTaskId.value) r.parentTaskId = parentTaskId.value
    const text = description.value.trim()
    if (text) r.description = plainTextToHtml(description.value)
    if (priority.value !== 'None') r.priority = priority.value
    if (assigneeIds.value.length) r.assigneeIds = [...assigneeIds.value]
    if (tagIds.value.length) r.tagIds = [...tagIds.value]
    if (startDate.value) r.startDate = startDate.value
    if (dueDate.value) r.dueDate = dueDate.value
    return r
  }

  /** In the panel on Home or a list page (focusing its root), otherwise the full page. */
  function openTask(id: Uuid): void {
    if (route.name === 'home' || route.name === 'list') panel.open(id)
    else void router.push(`/t/${id}`).catch(noop)
  }

  /**
   * After a create: Create more clears the text and keeps the rest (a paste still toasts its
   * count); otherwise the dialog closes and toasts, with Open for a single task.
   */
  function after(o2: { toast: string; openId?: Uuid; always?: boolean }): void {
    if (more.value) {
      title.value = ''
      description.value = ''
      o.focusTitle()
      if (o2.always) toast.show({ message: o2.toast })
      return
    }
    ui.closeQuickCreate()
    const id = o2.openId
    toast.show({ message: o2.toast, action: id ? { label: 'Open', run: () => openTask(id) } : undefined })
  }

  function create(o2: { open: boolean }): Uuid | null {
    const name = title.value.trim()
    const list = listId.value
    if (!name || !list || !workspaces.canEdit()) return null
    const r = request(name)
    const id = r.id!
    void tasks.create(list, r)
    recents.setLastList(list)
    if (o2.open) {
      ui.closeQuickCreate()
      openTask(id)
      return id
    }
    after({ toast: 'Task created', openId: id })
    return id
  }

  function pasteLines(text: string): boolean {
    const lines = splitPastedLines(text)
    if (lines.length < 2) return false
    if (lines.length > MAX_PASTED_TASKS) {
      toast.show({ message: `You can paste up to ${MAX_PASTED_TASKS} tasks at a time.` })
      return true
    }
    const n = lines.length
    void confirm({ title: `Create ${n} tasks?`, message: pasteMessage(lines), confirmLabel: `Create ${n} tasks` }).then((ok) => {
      const list = listId.value
      if (!ok || !list || !ui.quickCreate.open || !workspaces.canEdit()) return
      // Creates for one list and status go one at a time (the store queues them), in pasted order.
      for (const line of lines) void tasks.create(list, request(line))
      recents.setLastList(list)
      after({ toast: `Created ${n} tasks`, always: true })
    })
    return true
  }

  return {
    title,
    description,
    listId,
    statusId,
    assigneeIds,
    tagIds,
    priority,
    dueDate,
    startDate,
    more,
    canCreate,
    reset,
    create,
    pasteLines,
  }
}

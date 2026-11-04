// App-wide UI state (10.2): the sidebar, the palette, quick create and the global dialogs. The
// open* actions apply the role rules, so a guest can't open a dialog they can't use.
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { DESKTOP_MIN } from '../composables/useBreakpoint'
import { readString, writeString } from '../lib/storage'
import type { CreateTaskRequest, Uuid } from '../types'
import { useWorkspaces } from './workspaces'

export type QuickCreateDefaults = Partial<
  Pick<CreateTaskRequest, 'title' | 'statusId' | 'assigneeIds' | 'tagIds' | 'priority' | 'dueDate' | 'startDate' | 'parentTaskId'>
> & { listId?: Uuid }

const SIDEBAR_KEY = 'taskloom.sidebar'

export const useUi = defineStore('ui', () => {
  const sidebarCollapsed = ref(readString(SIDEBAR_KEY) === 'collapsed')
  const sidebarDrawerOpen = ref(false)
  const paletteOpen = ref(false)
  const paletteQuery = ref('')
  const shortcutsOpen = ref(false)
  const quickCreate = ref<{ open: boolean; defaults: QuickCreateDefaults }>({ open: false, defaults: {} })
  const createDefaults = ref<QuickCreateDefaults | null>(null)
  const statusEditor = ref<{ listId: Uuid } | null>(null)
  const deleteStatus = ref<{ statusId: Uuid } | null>(null)
  const newSpace = ref(false)
  const newList = ref<{ spaceId: Uuid | null } | null>(null)
  const moveList = ref<{ listId: Uuid } | null>(null)
  const invite = ref<{ email?: string; assignTo?: Uuid[] } | null>(null)
  const newWorkspace = ref(false)
  const moveTasks = ref<{ taskIds: Uuid[] } | null>(null)
  const inlineAddRequest = ref<{ listId: Uuid; at: number } | null>(null)
  /**
   * True while New list or New space was opened in place of quick create because the workspace
   * has no list yet: the dialog shows "Create a list to hold your tasks first." (8.1).
   */
  const noListHint = ref(false)

  function canEdit(): boolean {
    return useWorkspaces().canEdit()
  }

  /** At 1024px and wider it collapses the docked sidebar (persisted); below, it opens or closes the drawer (8.2). */
  function toggleSidebar(): void {
    const wide = typeof window === 'undefined' || window.innerWidth >= DESKTOP_MIN
    if (wide) {
      sidebarCollapsed.value = !sidebarCollapsed.value
      writeString(SIDEBAR_KEY, sidebarCollapsed.value ? 'collapsed' : 'open')
    } else {
      sidebarDrawerOpen.value = !sidebarDrawerOpen.value
    }
  }

  function openPalette(query = ''): void {
    paletteQuery.value = query
    paletteOpen.value = true
  }

  function openShortcuts(): void {
    shortcutsOpen.value = true
  }

  function setCreateDefaults(d: QuickCreateDefaults | null): void {
    createDefaults.value = d ? { ...d } : null
  }

  /** Quick create (8.12). Guests never get it; without a list it opens New list (or New space) instead. */
  function openQuickCreate(d?: QuickCreateDefaults): void {
    if (!canEdit()) return
    const ws = useWorkspaces().current
    if (!ws) return
    const hasList = ws.spaces.some((s) => s.lists.length > 0)
    if (!hasList) {
      const firstSpace = [...ws.spaces].sort((a, b) => a.position - b.position)[0]
      if (firstSpace) openNewList(firstSpace.id)
      else openNewSpace()
      noListHint.value = true
      return
    }
    quickCreate.value = { open: true, defaults: { ...(d ?? {}) } }
  }

  function closeQuickCreate(): void {
    quickCreate.value = { open: false, defaults: {} }
  }

  function openStatusEditor(listId: Uuid): void {
    if (canEdit()) statusEditor.value = { listId }
  }

  function openDeleteStatus(statusId: Uuid): void {
    if (canEdit()) deleteStatus.value = { statusId }
  }

  function openNewSpace(): void {
    if (!canEdit()) return
    noListHint.value = false
    newSpace.value = true
  }

  function openNewList(spaceId?: Uuid): void {
    if (!canEdit()) return
    noListHint.value = false
    newList.value = { spaceId: spaceId ?? null }
  }

  function openMoveList(listId: Uuid): void {
    if (canEdit()) moveList.value = { listId }
  }

  /** The invite dialog is for Admin+ (9.4). */
  function openInvite(o?: { email?: string; assignTo?: Uuid[] }): void {
    if (!useWorkspaces().isAdmin()) return
    invite.value = { ...(o?.email ? { email: o.email } : {}), ...(o?.assignTo ? { assignTo: [...o.assignTo] } : {}) }
  }

  /** Anyone signed in can create a workspace. */
  function openNewWorkspace(): void {
    newWorkspace.value = true
  }

  function openMoveTasks(ids: Uuid[]): void {
    if (!canEdit() || ids.length === 0) return
    moveTasks.value = { taskIds: [...ids] }
  }

  function requestInlineAdd(listId: Uuid): void {
    inlineAddRequest.value = { listId, at: Date.now() }
  }

  /** Closes every dialog (sign-out). The sidebar preference is kept. */
  function reset(): void {
    sidebarDrawerOpen.value = false
    paletteOpen.value = false
    paletteQuery.value = ''
    shortcutsOpen.value = false
    quickCreate.value = { open: false, defaults: {} }
    createDefaults.value = null
    statusEditor.value = null
    deleteStatus.value = null
    newSpace.value = false
    newList.value = null
    moveList.value = null
    invite.value = null
    newWorkspace.value = false
    moveTasks.value = null
    inlineAddRequest.value = null
    noListHint.value = false
  }

  return {
    sidebarCollapsed,
    sidebarDrawerOpen,
    paletteOpen,
    paletteQuery,
    shortcutsOpen,
    quickCreate,
    createDefaults,
    statusEditor,
    deleteStatus,
    newSpace,
    newList,
    moveList,
    invite,
    newWorkspace,
    moveTasks,
    inlineAddRequest,
    noListHint,
    toggleSidebar,
    openPalette,
    openShortcuts,
    setCreateDefaults,
    openQuickCreate,
    closeQuickCreate,
    openStatusEditor,
    openDeleteStatus,
    openNewSpace,
    openNewList,
    openMoveList,
    openInvite,
    openNewWorkspace,
    openMoveTasks,
    requestInlineAdd,
    reset,
  }
})

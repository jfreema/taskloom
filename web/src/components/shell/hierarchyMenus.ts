// The space and list "..." menus (3.4, 8.3) and the deletes behind them. The sidebar uses both;
// the list page header can use `listMenuItems` for its own list menu ("the same items as the
// sidebar list menu", 8.4). Items follow the role rules of 9.4: guests get only "Copy link" on lists
// and no space menu at all; deleting a space needs Admin.
import { FolderInput, Link, ListChecks, Pencil, Plus, Trash2 } from '@lucide/vue'
import { useClipboard } from '../../composables/useClipboard'
import { useConfirm } from '../../composables/useConfirm'
import { useToast } from '../../composables/useToast'
import { pluralize } from '../../lib/format'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import type { MenuItem } from '../ui/menu'

export interface HierarchyMenuOptions {
  /** Shows "Rename" (Member+); the caller starts its inline edit. */
  onRename?: () => void
  /** A key hint for Rename ("F2" in the sidebar, where F2 works on the row). */
  renameKbd?: string
}

export function useHierarchyMenus(): {
  listMenuItems(listId: Uuid, o?: HierarchyMenuOptions): MenuItem[]
  spaceMenuItems(spaceId: Uuid, o?: HierarchyMenuOptions): MenuItem[]
  deleteList(listId: Uuid): Promise<void>
  deleteSpace(spaceId: Uuid): Promise<void>
  copyListLink(listId: Uuid): Promise<void>
} {
  const workspaces = useWorkspaces()
  const tasks = useTasks()
  const ui = useUi()
  const toast = useToast()
  const confirm = useConfirm()
  const clipboard = useClipboard()

  /** Tasks in these lists, all levels; null when any of them isn't loaded (8.3). */
  function knownTaskCount(listIds: readonly Uuid[]): number | null {
    let n = 0
    for (const id of listIds) {
      if ((tasks.listMeta[id]?.loadedAt ?? 0) === 0) return null
      n += tasks.inList(id).length
    }
    return n
  }

  async function copyListLink(listId: Uuid): Promise<void> {
    const ok = await clipboard.copy(`${window.location.origin}/l/${listId}`)
    if (ok) toast.success('Link copied')
    else toast.show({ message: "Couldn't copy the link.", tone: 'danger' })
  }

  async function deleteList(listId: Uuid): Promise<void> {
    const found = workspaces.findList(listId)
    if (!found || !workspaces.canEdit(found.workspace.id)) return
    const n = knownTaskCount([listId])
    const what =
      n === null ? 'Deletes the list and all its tasks.' : n === 0 ? 'The list has no tasks.' : `Deletes ${pluralize(n, 'task')}.`
    const ok = await confirm({
      title: `Delete “${found.list.name}”?`,
      message: `${what} This can't be undone.`,
      confirmLabel: 'Delete list',
      danger: true,
    })
    if (!ok) return
    // Optimistic; the store leaves the list's page, and toasts and restores the list on failure.
    try {
      await workspaces.deleteList(listId)
    } catch {
      // Reported by the store.
    }
  }

  async function deleteSpace(spaceId: Uuid): Promise<void> {
    const found = workspaces.findSpace(spaceId)
    if (!found || !workspaces.isAdmin(found.workspace.id)) return
    const { space } = found
    const lists = space.lists.length
    const n = knownTaskCount(space.lists.map((l) => l.id))
    let what: string
    if (lists === 0) what = 'The space has no lists.'
    else if (n === null) what = `Deletes ${pluralize(lists, 'list')} and all ${lists === 1 ? 'its' : 'their'} tasks.`
    else what = `Deletes ${pluralize(lists, 'list')} and ${pluralize(n, 'task')}.`
    const ok = await confirm({
      title: `Delete “${space.name}”?`,
      message: `${what} This can't be undone.`,
      confirmLabel: 'Delete space',
      danger: true,
      requireText: space.name,
    })
    if (!ok) return
    try {
      await workspaces.deleteSpace(spaceId)
    } catch {
      // Reported by the store.
    }
  }

  function listMenuItems(listId: Uuid, o: HierarchyMenuOptions = {}): MenuItem[] {
    const found = workspaces.findList(listId)
    if (!found) return []
    const copy: MenuItem = { id: 'copy-link', label: 'Copy link', icon: Link, onSelect: () => void copyListLink(listId) }
    if (!workspaces.canEdit(found.workspace.id)) return [copy]
    const otherSpaces = found.workspace.spaces.length > 1
    const items: MenuItem[] = []
    if (o.onRename) items.push({ id: 'rename', label: 'Rename', icon: Pencil, kbd: o.renameKbd, onSelect: o.onRename })
    items.push(
      { id: 'statuses', label: 'Edit statuses', icon: ListChecks, onSelect: () => ui.openStatusEditor(listId) },
      {
        id: 'move',
        label: 'Move to space…',
        icon: FolderInput,
        disabled: !otherSpaces,
        hint: otherSpaces ? undefined : 'There is no other space to move it to.',
        onSelect: () => ui.openMoveList(listId),
      },
      copy,
      { kind: 'separator', id: 'sep-delete' },
      { id: 'delete', label: 'Delete list', icon: Trash2, danger: true, onSelect: () => void deleteList(listId) },
    )
    return items
  }

  function spaceMenuItems(spaceId: Uuid, o: HierarchyMenuOptions = {}): MenuItem[] {
    const found = workspaces.findSpace(spaceId)
    if (!found || !workspaces.canEdit(found.workspace.id)) return []
    const { space } = found
    const items: MenuItem[] = []
    if (o.onRename) items.push({ id: 'rename', label: 'Rename', icon: Pencil, kbd: o.renameKbd, onSelect: o.onRename })
    items.push(
      {
        kind: 'swatches',
        id: 'colour',
        label: 'Colour',
        value: space.color,
        onSelect: (hex) => {
          if (hex !== space.color) void workspaces.updateSpace(spaceId, { color: hex }).catch(() => undefined)
        },
      },
      { id: 'new-list', label: 'New list', icon: Plus, onSelect: () => ui.openNewList(spaceId) },
    )
    if (workspaces.isAdmin(found.workspace.id)) {
      items.push(
        { kind: 'separator', id: 'sep-delete' },
        { id: 'delete', label: 'Delete space', icon: Trash2, danger: true, onSelect: () => void deleteSpace(spaceId) },
      )
    }
    return items
  }

  return { listMenuItems, spaceMenuItems, deleteList, deleteSpace, copyListLink }
}

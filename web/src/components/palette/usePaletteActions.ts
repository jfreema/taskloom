// The palette's actions (8.11), in order: New task {N} (Member+), Go to Home {G H}, the three
// themes, the two styles (Look 3), Keyboard shortcuts {?}, Settings, Invite people (Admin+), New space and New list…
// (Member+; New list asks for the space when there are several), Edit statuses on list pages
// (Member+), then one "Switch to <workspace>" per other workspace.
import { computed, type Component, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Building2, House, Inbox, Keyboard, Landmark, LayoutDashboard, ListChecks, Monitor, Moon, Plus, Settings, Spool, SquarePen, Sun, UserPlus } from '@lucide/vue'
import { useLook, type Look } from '../../composables/useLook'
import { useTheme } from '../../composables/useTheme'
import { useWorkspaceSwitch } from '../../composables/useWorkspaceSwitch'
import { noop } from '../../lib/async'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { ThemePreference } from '../../types'

export interface PaletteAction {
  key: string
  label: string
  icon: Component
  kbd?: string
  /** Theme and style items: the current choice. */
  current?: boolean
  /** Extra words it answers to, matched at the start of a word. */
  keywords?: string
  run(): void
}

const THEMES: { value: ThemePreference; label: string; icon: Component }[] = [
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
]

const LOOKS: { value: Look; label: string; icon: Component; keywords: string }[] = [
  { value: 'loom', label: 'Loom', icon: Spool, keywords: 'default' },
  { value: 'pelican', label: 'Pelican', icon: Landmark, keywords: 'louisiana state government' },
]

export function usePaletteActions(): ComputedRef<PaletteAction[]> {
  const ui = useUi()
  const workspaces = useWorkspaces()
  const theme = useTheme()
  const style = useLook()
  const router = useRouter()
  const route = useRoute()
  const switcher = useWorkspaceSwitch()

  function go(path: string): void {
    void router.push(path).catch(noop)
  }

  return computed<PaletteAction[]>(() => {
    const out: PaletteAction[] = []
    const edit = workspaces.canEdit()
    const ws = workspaces.current
    if (edit) {
      out.push({
        key: 'new-task',
        label: 'New task',
        icon: SquarePen,
        kbd: 'N',
        keywords: 'create add',
        run: () => ui.openQuickCreate(ui.createDefaults ?? undefined),
      })
    }
    out.push({ key: 'home', label: 'Go to Home', icon: House, kbd: 'G H', keywords: 'my work', run: () => go('/') })
    if (ws) {
      out.push({ key: 'inbox', label: 'Go to Inbox', icon: Inbox, kbd: 'G I', keywords: 'notifications mentions unread reminders', run: () => go('/inbox') })
      out.push({ key: 'dashboard', label: 'Go to Dashboard', icon: LayoutDashboard, kbd: 'G D', keywords: 'reports charts overview stats workload', run: () => go('/dashboard') })
    }
    for (const t of THEMES) {
      out.push({
        key: `theme-${t.value}`,
        label: `Theme: ${t.label}`,
        icon: t.icon,
        current: theme.preference.value === t.value,
        keywords: 'appearance mode colour color',
        run: () => theme.setPreference(t.value),
      })
    }
    for (const l of LOOKS) {
      out.push({
        key: `look-${l.value}`,
        label: `Style: ${l.label}`,
        icon: l.icon,
        current: style.look.value === l.value,
        keywords: `appearance theme look design ${l.keywords}`,
        run: () => style.setLook(l.value),
      })
    }
    out.push({ key: 'shortcuts', label: 'Keyboard shortcuts', icon: Keyboard, kbd: '?', keywords: 'keys help', run: () => ui.openShortcuts() })
    out.push({ key: 'settings', label: 'Settings', icon: Settings, keywords: 'preferences profile', run: () => go('/settings') })
    if (workspaces.isAdmin()) {
      out.push({ key: 'invite', label: 'Invite people', icon: UserPlus, keywords: 'members add', run: () => ui.openInvite() })
    }
    if (edit && ws) {
      out.push({ key: 'new-space', label: 'New space', icon: Plus, keywords: 'create add', run: () => ui.openNewSpace() })
      if (ws.spaces.length > 0) {
        // One space: the list goes there; several: the dialog asks which.
        const only = ws.spaces.length === 1 ? ws.spaces[0].id : undefined
        out.push({ key: 'new-list', label: 'New list…', icon: Plus, keywords: 'create add', run: () => ui.openNewList(only) })
      }
      const listId = route.name === 'list' && typeof route.params.listId === 'string' ? route.params.listId : null
      if (listId && workspaces.findList(listId)) {
        out.push({
          key: 'edit-statuses',
          label: 'Edit statuses',
          icon: ListChecks,
          keywords: 'status workflow columns',
          run: () => ui.openStatusEditor(listId),
        })
      }
    }
    for (const w of workspaces.list) {
      if (w.id === workspaces.currentId) continue
      out.push({
        key: `switch-${w.id}`,
        label: `Switch to ${w.name}`,
        icon: Building2,
        keywords: 'workspace',
        run: () => void switcher.switchTo(w.id, { navigate: true }),
      })
    }
    return out
  })
}

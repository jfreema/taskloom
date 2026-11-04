// The global shortcuts AppShell registers (9.5): Mod+K, /, N, ?, [ and G then H. Mod+Z is
// registered by UiToastHost and the panel's Esc by TaskPanelHost. useShortcut applies the rules:
// single keys never fire while typing, while a modal layer is open, or when single-key shortcuts
// are off; handlers return false to pass the key on.
import { useRouter } from 'vue-router'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useLayerStack } from '../../composables/useLayerStack'
import { useShortcut } from '../../composables/useShortcut'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'

/** The view's own search box ("Search this list"), when the page shows one. */
function viewSearchBox(): HTMLInputElement | null {
  const el = document.getElementById('main')?.querySelector<HTMLInputElement>('input[type="search"], input[role="searchbox"]')
  return el && !el.disabled && el.getClientRects().length > 0 ? el : null
}

export function useShellShortcuts(): void {
  const ui = useUi()
  const workspaces = useWorkspaces()
  const router = useRouter()
  const layers = useLayerStack()
  const { isDesktop } = useBreakpoint()

  // Also from text fields. Not over another dialog (the navigation drawer aside).
  useShortcut(
    'Mod+k',
    () => {
      if (ui.paletteOpen || !workspaces.current) return false
      if (layers.hasModal.value && !ui.sidebarDrawerOpen) return false
      ui.openPalette()
    },
    { allowInInputs: true },
  )

  useShortcut('/', () => {
    const box = viewSearchBox()
    if (box) {
      box.focus()
      box.select()
      return
    }
    if (!workspaces.current) return false
    ui.openPalette()
  })

  useShortcut('n', () => {
    if (!workspaces.canEdit()) return false
    ui.openQuickCreate(ui.createDefaults ?? undefined)
  })

  useShortcut('?', () => ui.openShortcuts())

  useShortcut('[', () => {
    if (!isDesktop.value) return false
    ui.toggleSidebar()
  })

  useShortcut('g h', () => {
    void router.push({ name: 'home' }).catch(() => undefined)
  })
}

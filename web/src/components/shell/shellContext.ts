// What AppShell shares with the shell components inside it.
import type { InjectionKey, Ref } from 'vue'

/**
 * The inset content panel (8.2). TaskPanelHost measures it with a ResizeObserver to decide
 * whether the task panel docks beside the page or opens as a modal drawer (8.10).
 */
export const contentPanelKey: InjectionKey<Ref<HTMLElement | null>> = Symbol('taskloom.contentPanel')

/** The route names that show the phone "New task" button (3.6) and accept ?task= (3.2). */
export const PANEL_ROUTE_NAMES: ReadonlySet<string> = new Set(['home', 'list'])

/** Routes that still work without a workspace: the profile and appearance settings. */
export const WORKSPACE_FREE_ROUTES: ReadonlySet<string> = new Set(['settings-profile', 'settings-appearance'])

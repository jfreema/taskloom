// Routes (spec 3.2). Every route except /login needs a session; the guard loads it once.
// Route components are lazy. afterEach sets the document title and moves focus to #main after a
// path change (9.6).
import { nextTick } from 'vue'
import {
  START_LOCATION,
  createRouter,
  createWebHistory,
  type RouteLocationNormalizedLoaded,
  type RouteRecordRaw,
} from 'vue-router'
import { useSession } from './stores/session'
import { useWorkspaces } from './stores/workspaces'
import { useLayerStack } from './composables/useLayerStack'
import { readJson, userKey } from './lib/storage'
import type { Uuid, ViewKind } from './types'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without a session (the sign-in page). */
    public?: boolean
    /** The page name in "<Page> · Taskloom"; a function for pages named after their data. */
    title?: string | ((route: RouteLocationNormalizedLoaded) => string | null | undefined)
  }
}

const VIEWS: readonly ViewKind[] = ['list', 'board', 'calendar']

function asView(v: unknown): ViewKind | null {
  return typeof v === 'string' && (VIEWS as readonly string[]).includes(v) ? (v as ViewKind) : null
}

const CONTROL_CHARS = /[\u0000-\u001f\u007f-\u009f]/

/**
 * The sign-in return address (5.8): accepted only when it starts with exactly one "/", doesn't
 * start with "//" or "/\", and contains no backslash or control character. Anything else is "/".
 */
export function safeReturnUrl(raw: unknown): string {
  const value = Array.isArray(raw) ? raw[0] : raw
  if (typeof value !== 'string' || !value.startsWith('/')) return '/'
  if (value.startsWith('//') || value.includes('\\') || CONTROL_CHARS.test(value)) return '/'
  return value
}

interface LastViews {
  byList?: Record<Uuid, ViewKind>
  last?: ViewKind
}

/** The view /l/:listId opens: the last one used for that list, else the last used anywhere, else List. */
function lastViewFor(listId: Uuid): ViewKind {
  const userId = useSession().user?.id
  if (!userId) return 'list'
  const stored = readJson<LastViews | null>(userKey('taskloom.lastView', userId), null)
  return asView(stored?.byList?.[listId]) ?? asView(stored?.last) ?? 'list'
}

function listTitle(route: RouteLocationNormalizedLoaded): string {
  return useWorkspaces().findList(String(route.params.listId))?.list.name ?? 'List'
}

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('./views/LoginView.vue'),
    meta: { public: true, title: 'Sign in' },
  },
  {
    path: '/',
    component: () => import('./views/AppShell.vue'),
    children: [
      { path: '', name: 'home', component: () => import('./views/HomeView.vue'), meta: { title: 'Home' } },
      {
        path: 'l/:listId',
        name: 'list-default',
        // Never rendered: beforeEnter always redirects. A guard rather than `redirect`, because the
        // stored view is per user and the session is only known once the global guard has run.
        component: { render: () => null },
        beforeEnter: (to) => ({
          name: 'list',
          params: { listId: to.params.listId, view: lastViewFor(String(to.params.listId)) },
          query: to.query,
          hash: to.hash,
          replace: true,
        }),
      },
      {
        path: 'l/:listId/:view(list|board|calendar)',
        name: 'list',
        component: () => import('./views/ListPage.vue'),
        props: true,
        meta: { title: listTitle },
      },
      {
        path: 't/:taskId',
        name: 'task',
        component: () => import('./views/TaskPage.vue'),
        props: true,
        meta: { title: 'Task' },
      },
      {
        path: 'settings',
        name: 'settings',
        component: () => import('./views/settings/SettingsLayout.vue'),
        redirect: { name: 'settings-profile' },
        children: [
          {
            path: 'profile',
            name: 'settings-profile',
            component: () => import('./views/settings/ProfileSettings.vue'),
            meta: { title: 'Profile' },
          },
          {
            path: 'appearance',
            name: 'settings-appearance',
            component: () => import('./views/settings/AppearanceSettings.vue'),
            meta: { title: 'Appearance' },
          },
          {
            path: 'workspace',
            name: 'settings-workspace',
            component: () => import('./views/settings/WorkspaceSettings.vue'),
            meta: { title: 'Workspace settings' },
          },
          {
            path: 'members',
            name: 'settings-members',
            component: () => import('./views/settings/MembersSettings.vue'),
            meta: { title: 'Members' },
          },
          {
            path: 'tags',
            name: 'settings-tags',
            component: () => import('./views/settings/TagsSettings.vue'),
            meta: { title: 'Tags' },
          },
        ],
      },
    ],
  },
  ...(import.meta.env.DEV
    ? [
        {
          path: '/_ui',
          name: 'ui-gallery',
          component: () => import('./views/dev/UiGallery.vue'),
          meta: { title: 'UI gallery' },
        } satisfies RouteRecordRaw,
      ]
    : []),
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

/** "<Page> · Taskloom". Reactive when called inside an effect (App.vue keeps it current). */
export function pageTitle(route: RouteLocationNormalizedLoaded): string {
  const t = route.meta.title
  const name = typeof t === 'function' ? t(route) : t
  return name ? `${name} · Taskloom` : 'Taskloom'
}

router.beforeEach(async (to) => {
  const session = useSession()
  if (!session.loaded) {
    try {
      await session.load()
    } catch {
      // Unreachable server: treated as signed out; the sign-in page shows the error when used.
    }
  }
  if (to.meta.public) {
    if (to.name === 'login' && session.user) return safeReturnUrl(to.query.returnUrl)
    return true
  }
  if (!session.user) {
    return { name: 'login', query: to.fullPath === '/' ? {} : { returnUrl: to.fullPath }, replace: true }
  }
  return true
})

// The last input, so a navigation made by keyboard from the sidebar can keep its focus (9.6).
const SIDEBAR = 'nav[aria-label="Sidebar"]'
let lastKeyTarget: EventTarget | null = null
if (typeof document !== 'undefined') {
  document.addEventListener('keydown', (e) => (lastKeyTarget = e.target), true)
  document.addEventListener('pointerdown', () => (lastKeyTarget = null), true)
}

function keyboardFromSidebar(): boolean {
  const active = document.activeElement
  return lastKeyTarget instanceof Element && !!lastKeyTarget.closest(SIDEBAR) && !!active?.closest(SIDEBAR)
}

router.afterEach((to, from, failure) => {
  if (failure) return
  document.title = pageTitle(to)
  // Focus moves only after a path change: not on the first load, and not for ?task= changes.
  if (from === START_LOCATION || to.path === from.path) return
  void nextTick(() => {
    if (keyboardFromSidebar() || useLayerStack().hasModal.value) return
    document.getElementById('main')?.focus({ preventScroll: true })
  })
})

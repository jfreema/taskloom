// The app's router for code that may run outside a component's setup (store actions, event
// handlers, toast actions). `useRouter()` only works where `inject()` does; this caches the router
// the first time it is reachable and falls back to the app Pinia is installed on.
import { getActivePinia } from 'pinia'
import { hasInjectionContext, inject, type App } from 'vue'
import { routerKey, type Router } from 'vue-router'

let cached: Router | null = null

export function appRouter(): Router | null {
  if (cached) return cached
  if (hasInjectionContext()) {
    const fromInject = inject(routerKey, null)
    if (fromInject) return (cached = fromInject)
  }
  // Pinia keeps the app it was installed on (`_a`); vue-router registers itself as `$router` there.
  const pinia = getActivePinia() as unknown as { _a?: App | null } | undefined
  const fromApp = pinia?._a?.config.globalProperties.$router as Router | undefined
  if (fromApp) return (cached = fromApp)
  return null
}

export function requireRouter(): Router {
  const router = appRouter()
  if (!router) throw new Error('The router is not installed yet.')
  return router
}

import '@fontsource-variable/inter'
// Pelican's typeface. Its files load only while the Pelican style shows text in it.
import '@fontsource-variable/public-sans'
import './styles/index.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router, safeReturnUrl } from './router'
import { setUnauthorizedHandler } from './api'
import { useSession } from './stores/session'
import { useDensity } from './composables/useDensity'
import { useLook } from './composables/useLook'
import { useTheme } from './composables/useTheme'
import { vTooltip } from './components/ui/tooltip'

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.directive('tooltip', vTooltip)

// Applies the stored theme (theme-init.js already did before first paint) and follows the OS.
useTheme()
// The same for the density (Look 2) and the style (Look 3).
useDensity()
useLook()

// A 401 means the session ended: forget the user and go to sign-in, coming back here afterwards.
setUnauthorizedHandler(() => {
  useSession().user = null
  const current = router.currentRoute.value
  if (current.meta.public) return
  const returnUrl = safeReturnUrl(current.fullPath)
  void router.push({ name: 'login', query: returnUrl === '/' ? {} : { returnUrl } })
})

app.mount('#app')

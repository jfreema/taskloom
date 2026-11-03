import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { setUnauthorizedHandler } from './api'
import './style.css'

const app = createApp(App)
app.use(createPinia())
app.use(router)
setUnauthorizedHandler(() => {
  if (router.currentRoute.value.name !== 'login')
    router.push({ name: 'login', query: { returnUrl: router.currentRoute.value.fullPath } })
})
app.mount('#app')

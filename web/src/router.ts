import { createRouter, createWebHistory } from 'vue-router'
import { useSession } from './stores/session'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('./views/LoginView.vue') },
    {
      path: '/',
      component: () => import('./views/AppShell.vue'),
      children: [
        { path: '', name: 'home', component: () => import('./views/HomeView.vue') },
        {
          path: 'l/:listId/:view(list|board)?',
          name: 'list',
          component: () => import('./views/ListPage.vue'),
          props: true,
        },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})

router.beforeEach(async (to) => {
  const session = useSession()
  if (!session.loaded) await session.load()
  if (to.name !== 'login' && !session.user) return { name: 'login', query: { returnUrl: to.fullPath } }
  if (to.name === 'login' && session.user) return '/'
})

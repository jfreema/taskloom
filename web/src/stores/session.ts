import { defineStore } from 'pinia'
import { api } from '../api'
import type { User } from '../types'

interface Providers { devLogin: boolean; providers: { key: string; displayName: string }[] }

export const useSession = defineStore('session', {
  state: () => ({ user: null as User | null, loaded: false }),
  actions: {
    async load() {
      try {
        this.user = await api.get<User>('/api/me')
      } catch {
        this.user = null
      }
      this.loaded = true
      return this.user
    },
    providers: () => api.get<Providers>('/auth/providers'),
    async devLogin(email: string, displayName: string) {
      this.user = await api.post<User>('/auth/dev-login', { email, displayName })
    },
    async logout() {
      await api.post('/auth/logout')
      this.user = null
    },
  },
})

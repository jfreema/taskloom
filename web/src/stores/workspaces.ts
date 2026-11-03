import { defineStore } from 'pinia'
import { api, newId } from '../api'
import type { ListSummary, Space, Workspace } from '../types'

export const useWorkspaces = defineStore('workspaces', {
  state: () => ({
    workspaces: [] as Workspace[],
    currentId: localStorage.getItem('taskloom.workspace') as string | null,
    loaded: false,
  }),
  getters: {
    current(state): Workspace | undefined {
      return state.workspaces.find((w) => w.id === state.currentId) ?? state.workspaces[0]
    },
    firstListId(): string | undefined {
      return this.current?.spaces.flatMap((s) => s.lists)[0]?.id
    },
  },
  actions: {
    async load() {
      this.workspaces = await api.get<Workspace[]>('/api/workspaces')
      this.loaded = true
    },
    select(id: string) {
      this.currentId = id
      localStorage.setItem('taskloom.workspace', id)
    },
    findList(listId: string): { space: Space; list: ListSummary } | undefined {
      for (const w of this.workspaces)
        for (const space of w.spaces) {
          const list = space.lists.find((l) => l.id === listId)
          if (list) return { space, list }
        }
    },
    async createWorkspace(name: string) {
      const id = newId()
      await api.post('/api/workspaces', { id, name })
      await this.load()
      this.select(id)
    },
    async createSpace(name: string) {
      if (!this.current) return
      const space = await api.post<Space>(`/api/workspaces/${this.current.id}/spaces`, { id: newId(), name })
      this.current.spaces.push(space)
      return space
    },
    async renameSpace(space: Space, name: string) {
      await api.patch(`/api/spaces/${space.id}`, { name })
      space.name = name
    },
    async deleteSpace(space: Space) {
      await api.del(`/api/spaces/${space.id}`)
      if (this.current) this.current.spaces = this.current.spaces.filter((s) => s.id !== space.id)
    },
    async createList(space: Space, name: string) {
      const list = await api.post<ListSummary>(`/api/spaces/${space.id}/lists`, { id: newId(), name })
      space.lists.push(list)
      return list
    },
    async renameList(listId: string, name: string) {
      await api.patch(`/api/lists/${listId}`, { name })
      const found = this.findList(listId)
      if (found) found.list.name = name
    },
    async deleteList(listId: string) {
      await api.del(`/api/lists/${listId}`)
      const found = this.findList(listId)
      if (found) found.space.lists = found.space.lists.filter((l) => l.id !== listId)
    },
  },
})

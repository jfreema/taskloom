import { defineStore } from 'pinia'
import { api, newId } from '../api'
import type { ListDetail, Status, Task } from '../types'

/** The list currently open, its statuses and all its tasks (subtasks included). */
export const useList = defineStore('list', {
  state: () => ({
    detail: null as ListDetail | null,
    tasks: [] as Task[],
    loading: false,
    error: null as string | null,
  }),
  getters: {
    statuses: (s): Status[] => s.detail?.statuses ?? [],
    topLevel: (s): Task[] => s.tasks.filter((t) => !t.parentTaskId),
    tasksIn() {
      return (statusId: string): Task[] =>
        this.topLevel.filter((t) => t.statusId === statusId).sort((a, b) => a.position - b.position)
    },
    subtasksOf: (s) => (taskId: string): Task[] =>
      s.tasks.filter((t) => t.parentTaskId === taskId).sort((a, b) => a.position - b.position),
    statusById: (s) => (id: string): Status | undefined => s.detail?.statuses.find((x) => x.id === id),
    memberById: (s) => (id: string | null) => (id ? s.detail?.members.find((m) => m.id === id) : undefined),
  },
  actions: {
    async open(listId: string) {
      this.loading = true
      this.error = null
      try {
        const [detail, tasks] = await Promise.all([
          api.get<ListDetail>(`/api/lists/${listId}`),
          api.get<Task[]>(`/api/lists/${listId}/tasks`),
        ])
        this.detail = detail
        this.tasks = tasks
      } catch (e) {
        this.error = (e as Error).message
        this.detail = null
        this.tasks = []
      } finally {
        this.loading = false
      }
    },
    replace(task: Task) {
      const i = this.tasks.findIndex((t) => t.id === task.id)
      if (i >= 0) this.tasks[i] = task
      else this.tasks.push(task)
    },
    async createTask(title: string, statusId?: string, parentTaskId?: string) {
      if (!this.detail) return
      const task = await api.post<Task>(`/api/lists/${this.detail.id}/tasks`, {
        id: newId(),
        title,
        statusId,
        parentTaskId,
      })
      this.tasks.push(task)
      return task
    },
    /** Optimistic: the UI changes at once and rolls back if the server refuses. */
    async updateTask(id: string, patch: Partial<Task>) {
      const i = this.tasks.findIndex((t) => t.id === id)
      const before = i >= 0 ? { ...this.tasks[i] } : undefined
      if (i >= 0) Object.assign(this.tasks[i], patch)
      try {
        const saved = await api.patch<Task>(`/api/tasks/${id}`, patch)
        this.replace(saved)
        return saved
      } catch (e) {
        if (before) this.replace(before)
        throw e
      }
    },
    /** Move a task into a status column, between two neighbours (by id). */
    async moveTask(id: string, statusId: string, beforeTaskId: string | null, afterTaskId: string | null) {
      const task = this.tasks.find((t) => t.id === id)
      if (!task) return
      const before = beforeTaskId ? this.tasks.find((t) => t.id === beforeTaskId)?.position : undefined
      const after = afterTaskId ? this.tasks.find((t) => t.id === afterTaskId)?.position : undefined
      const snapshot = { ...task }
      task.statusId = statusId
      task.position =
        before !== undefined && after !== undefined ? (before + after) / 2
        : before !== undefined ? before + 1024
        : after !== undefined ? after - 1024
        : Math.max(0, ...this.tasksIn(statusId).filter((t) => t.id !== id).map((t) => t.position)) + 1024
      try {
        this.replace(await api.post<Task>(`/api/tasks/${id}/move`, { statusId, beforeTaskId, afterTaskId }))
      } catch (e) {
        this.replace(snapshot)
        throw e
      }
    },
    async deleteTask(id: string) {
      await api.del(`/api/tasks/${id}`)
      this.tasks = this.tasks.filter((t) => t.id !== id && t.parentTaskId !== id)
    },
    async addStatus(name: string, color: string) {
      if (!this.detail) return
      const s = await api.post<Status>(`/api/lists/${this.detail.id}/statuses`, { id: newId(), name, color, category: 'Active' })
      this.detail.statuses.push(s)
    },
    async updateStatus(status: Status, patch: Partial<Status>) {
      const saved = await api.patch<Status>(`/api/statuses/${status.id}`, patch)
      Object.assign(status, saved)
    },
    async deleteStatus(status: Status) {
      if (!this.detail) return
      await api.del(`/api/statuses/${status.id}`)
      await this.open(this.detail.id)
    },
  },
})

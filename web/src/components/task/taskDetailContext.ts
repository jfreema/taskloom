// What TaskDetail shares with its sections (8.10): the task being shown, the mode, the heading
// level under its title heading, whether the user may edit, and the actions every section needs.
import { inject, type ComputedRef, type InjectionKey } from 'vue'
import type { Uuid } from '../../types'

export type TaskDetailMode = 'docked' | 'modal' | 'sheet' | 'page'

export interface TaskDetailContext {
  taskId: ComputedRef<Uuid>
  mode: ComputedRef<TaskDetailMode>
  /** Section headings: h2 on the full page (under its h1), h3 in the panel (under its h2). */
  headingTag: ComputedRef<'h2' | 'h3'>
  /** Member or above in the task's workspace. */
  canEdit: ComputedRef<boolean>
  /** Opens another task: in the panel (panel modes) or as the full page (page mode). */
  openTask(id: Uuid): void
  /** Uploads files to this task. `announce` toasts "Added … to attachments" with Show (drop and paste). */
  addFiles(files: File[], o?: { announce?: boolean }): void
}

export const taskDetailKey: InjectionKey<TaskDetailContext> = Symbol('taskDetail')

export function useTaskDetailContext(): TaskDetailContext {
  const ctx = inject(taskDetailKey)
  if (!ctx) throw new Error('useTaskDetailContext() is only available inside TaskDetail')
  return ctx
}

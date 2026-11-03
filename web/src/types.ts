export type Role = 'Owner' | 'Admin' | 'Member' | 'Guest'
export type StatusCategory = 'NotStarted' | 'Active' | 'Done'
export type Priority = 'None' | 'Urgent' | 'High' | 'Normal' | 'Low'

export interface User { id: string; email: string; displayName: string }
export interface ListSummary { id: string; name: string; position: number }
export interface Space { id: string; name: string; color: string; position: number; lists: ListSummary[] }
export interface Workspace { id: string; name: string; role: Role; spaces: Space[] }
export interface Status { id: string; name: string; color: string; category: StatusCategory; position: number }
export interface ListDetail { id: string; spaceId: string; workspaceId: string; name: string; statuses: Status[]; members: User[] }
export interface Member { userId: string; email: string; displayName: string; role: Role }

export interface Task {
  id: string
  listId: string
  statusId: string
  parentTaskId: string | null
  title: string
  description: string
  priority: Priority
  assigneeId: string | null
  startDate: string | null
  dueDate: string | null
  position: number
  createdAt: string
  updatedAt: string
  completedAt: string | null
}

export interface Comment { id: string; taskId: string; author: User; body: string; createdAt: string }
export interface Attachment { id: string; taskId: string; fileName: string; contentType: string; sizeBytes: number; createdAt: string }
export interface TaskDetail { task: Task; subtasks: Task[]; comments: Comment[]; attachments: Attachment[] }

export const priorities: { value: Priority; label: string; color: string }[] = [
  { value: 'Urgent', label: 'Urgent', color: '#e5484d' },
  { value: 'High', label: 'High', color: '#f5a623' },
  { value: 'Normal', label: 'Normal', color: '#4194f6' },
  { value: 'Low', label: 'Low', color: '#87909e' },
  { value: 'None', label: 'No priority', color: 'transparent' },
]

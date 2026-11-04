// Sample data for the /_ui gallery demos.
import { Tag } from '@lucide/vue'
import type { ListboxOption } from '../listbox'

export const PEOPLE = [
  { id: '5f0c8a52-2a7e-4f55-9a43-0d6e0f6c2b11', displayName: 'Jared Doe' },
  { id: '0b9e1c3d-7f21-4c8a-b0e4-6a5d2f9c8e72', displayName: 'Ana Moreno' },
  { id: 'c2d4e6f8-1a3b-4c5d-8e7f-9a0b1c2d3e43', displayName: 'Sam Lee' },
  { id: '7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c24', displayName: 'Priya Natarajan' },
  { id: 'e1f2a3b4-c5d6-4e7f-8a9b-0c1d2e3f4a85', displayName: 'Tomás Ruiz' },
]

/** Statuses grouped as the status picker groups them (7.11). */
export const STATUS_OPTIONS: ListboxOption[] = [
  { value: 'todo', label: 'To do', group: 'Not started' },
  { value: 'progress', label: 'In progress', group: 'Active' },
  { value: 'review', label: 'In review', group: 'Active' },
  { value: 'blocked', label: 'Blocked', group: 'Active', disabled: true, description: 'Turned off for this list' },
  { value: 'done', label: 'Done', group: 'Done' },
]

/** More than 7 options, so search turns on. */
export const MEMBER_OPTIONS: ListboxOption[] = [
  { value: 'jared', label: 'Jared Doe', description: 'jared@example.com', keywords: ['jared@example.com'] },
  { value: 'ana', label: 'Ana Moreno', description: 'ana@example.com', keywords: ['ana@example.com'] },
  { value: 'sam', label: 'Sam Lee', description: 'sam@example.com', keywords: ['sam@example.com'] },
  { value: 'priya', label: 'Priya Natarajan', description: 'priya@example.com', keywords: ['priya@example.com'] },
  { value: 'tomas', label: 'Tomás Ruiz', description: 'tomas@example.com', keywords: ['tomas@example.com'] },
  { value: 'mei', label: 'Mei Chen', description: 'Invited', keywords: ['mei@example.com'] },
  { value: 'olu', label: 'Olu Adeyemi', description: 'Guest · view and comment only', keywords: ['olu@example.com'] },
  { value: 'lena', label: 'Lena Fischer', description: 'lena@example.com', keywords: ['lena@example.com'] },
  { value: 'raj', label: 'Raj Patel', description: 'raj@example.com', keywords: ['raj@example.com'] },
]

export function tagOption(name: string): ListboxOption {
  return { value: name, label: name, icon: Tag }
}

export const TAG_NAMES = ['bug', 'design', 'finance', 'home', 'urgent-ish']

export const ROLE_OPTIONS: ListboxOption[] = [
  { value: 'Guest', label: 'Guest', description: 'View and comment only' },
  { value: 'Member', label: 'Member', description: 'Create and edit tasks and lists' },
  { value: 'Admin', label: 'Admin', description: 'Also invites people and changes roles' },
  { value: 'Owner', label: 'Owner', description: 'Only one per workspace', disabled: true },
]

export const SPACE_OPTIONS: ListboxOption[] = [
  { value: 'home', label: 'Home' },
  { value: 'work', label: 'Work' },
  { value: 'side', label: 'Side projects' },
]

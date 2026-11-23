// web/src/types.ts
// Wire types for the Taskloom API. Mirrors section 5 of the iteration-2 spec and Taskloom.Api/Dtos.cs.
// Change both sides together.

/** A UUID string. Client-created rows use newId() in api.ts: crypto.randomUUID() when available, else a v4 UUID from crypto.getRandomValues (a dev server opened over plain http on the LAN is not a secure context). */
export type Uuid = string
/** A calendar date "YYYY-MM-DD" with no time zone. Never pass it to `new Date()`; use lib/dates.ts. */
export type IsoDate = string
/** An ISO-8601 timestamp with offset, for example "2026-11-04T03:29:55.1234567+00:00". */
export type IsoTimestamp = string

// ---- Enums (strings with the C# names) ----

export type Role = 'Owner' | 'Admin' | 'Member' | 'Guest'
/** Roles that can be given through the members API (ownership never changes hands this iteration). */
export type AssignableRole = Exclude<Role, 'Owner'>
export type StatusCategory = 'NotStarted' | 'Active' | 'Done'
export type Priority = 'None' | 'Urgent' | 'High' | 'Normal' | 'Low'
export type StatusPreset = 'Simple' | 'Review' | 'Requests'
export type Placement = 'Top' | 'Bottom'
export type ActivityKind =
  | 'Renamed'
  | 'StatusChanged'
  | 'AssigneeAdded'
  | 'AssigneeRemoved'
  | 'DueDateChanged'
  | 'StartDateChanged'
  | 'PriorityChanged'
  | 'TagAdded'
  | 'TagRemoved'
  | 'DescriptionChanged'
  | 'MovedList'
  | 'AttachmentAdded'
  | 'AttachmentRemoved'
  | 'SubtaskAdded'
  /** `from` and `to` are repeat rules as JSON (a RepeatRule without its null fields), or null. */
  | 'RepeatChanged'
  /** Completing the task created the next one; `to` is its due date. */
  | 'Repeated'
/** What an Inbox row tells its reader (Features 2). */
export type NotificationKind =
  /** The actor assigned the task to the reader. */
  | 'Assigned'
  /** The actor mentioned the reader in a comment; `to` is the comment on one line. */
  | 'Mentioned'
  /** The actor commented on a task of the reader's; `to` is the comment on one line. */
  | 'Commented'
  /** The actor moved the task into a Done status; `from` and `to` are status names. */
  | 'Completed'
  | 'StatusChanged'
  /** `from` and `to` are dates, or null for none. */
  | 'DueDateChanged'
  /** `from` and `to` are priority names. */
  | 'PriorityChanged'
  /** The morning reminder: due today. `to` is the date; no actor. */
  | 'DueToday'
  /** The morning reminder: was due on `to` and is still open; no actor. */
  | 'Overdue'
/** Query parameter of GET /api/workspaces/{id}/tasks (lower case on the wire). */
export type TaskScope = 'mine' | 'all'
export type RepeatUnit = 'Day' | 'Week' | 'Month' | 'Year'
/** Schedule: the rule's own dates, counted from the due date. Completion: counted from the day the task is done. */
export type RepeatBasis = 'Schedule' | 'Completion'
/** Day names as .NET's DayOfWeek: index i is `Date.getDay() === i`. */
export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

// ---- Responses ----

export interface User {
  id: Uuid
  email: string
  displayName: string
  /** Their photo ("/api/users/{id}/photo?v=…", same origin); null when they have none and avatars show initials. */
  photoUrl: string | null
}

/** GET and PATCH /api/me and the photo calls: the signed-in user. Everywhere else a person is a plain User. */
export interface Me extends User {
  /** Listed in Auth:SiteAdmins: sees Settings › People and may turn accounts off and on. */
  isSiteAdmin: boolean
  /** They uploaded or removed their photo by hand, so signing in no longer brings the provider's photo. */
  photoChosen: boolean
  /** The IANA time zone their browser last reported ("America/Chicago"); it sets when their morning reminder goes out. */
  timeZone: string | null
  /** Email me when someone assigns me a task or mentions me. */
  emailMentions: boolean
  /** Email me the morning reminder of tasks due today or overdue. */
  emailReminders: boolean
  /** The server really sends email (Email:Provider is not "Log"). */
  emailConfigured: boolean
}

export interface Member {
  userId: Uuid
  email: string
  displayName: string
  role: Role
  /** True while the person has been invited but has never signed in. */
  pending: boolean
  /** A site admin turned the account off. The person stays a member and an assignee, but can't sign in. */
  disabled: boolean
  photoUrl: string | null
}

/** One account in GET /api/admin/users (site admins only), ordered by display name. */
export interface AdminUser {
  id: Uuid
  email: string
  displayName: string
  /** Invited and never signed in (no linked identity). */
  pending: boolean
  /** Turned off: signed out everywhere and refused at sign-in until turned back on. */
  disabled: boolean
  isSiteAdmin: boolean
  createdAt: IsoTimestamp
  /** The newest session's last use; null when the person has no session at all. */
  lastSeenAt: IsoTimestamp | null
  /** Sessions that are neither signed out nor expired. */
  activeSessions: number
  /** How many workspaces the person is a member of. */
  workspaces: number
  photoUrl: string | null
}

export interface Status {
  id: Uuid
  listId: Uuid
  name: string
  /** "#RRGGBB", normally one of the 13 swatches (section 7.2). */
  color: string
  category: StatusCategory
  position: number
}

export interface ListSummary {
  id: Uuid
  spaceId: Uuid
  name: string
  position: number
  /** "#RRGGBB" for the list's icon, or null: a grey icon. */
  color: string | null
  /** A key of lib/listIcons.ts LIST_ICONS, or null: the plain list icon. */
  icon: string | null
  /** Ordered by position as the server sends them; display order comes from workspaces.statusesFor(). */
  statuses: Status[]
}

export interface Space {
  id: Uuid
  name: string
  color: string
  position: number
  /** Only `memberIds` can see it (and anything in it); to everyone else it doesn't exist. */
  isPrivate: boolean
  /** The people of a private space, by when they were added; empty for a space everyone in the workspace can see. */
  memberIds: Uuid[]
  lists: ListSummary[]
}

/** POST /api/spaces/{id}/members: the space's people after the change. */
export interface SpaceMembersResponse {
  spaceId: Uuid
  isPrivate: boolean
  memberIds: Uuid[]
}

/** A row of GET /api/workspaces/{id}/private-spaces (Admin+), including private spaces the caller isn't in. */
export interface PrivateSpaceSummary {
  id: Uuid
  name: string
  color: string
  memberCount: number
  isMember: boolean
}

/** GET /api/workspaces returns the whole tree the caller can see. */
export interface Workspace {
  id: Uuid
  name: string
  /** The caller's role in this workspace. */
  role: Role
  spaces: Space[]
}

export interface Tag {
  id: Uuid
  workspaceId: Uuid
  name: string
  color: string
  createdAt: IsoTimestamp
}

/**
 * How a task repeats. Completing it creates the next task of the series, which takes the rule over. The server fits
 * the rule to its unit and basis: `weekdays` only for weekly rules on schedule, `monthDay` or `monthWeek` and
 * `monthWeekday` only for monthly ones.
 */
export interface RepeatRule {
  unit: RepeatUnit
  /** Every 1 to 365 units. */
  every: number
  basis: RepeatBasis
  /** In week order from Sunday. */
  weekdays: Weekday[]
  /** 1 to 31 (the month's last day when shorter), or -1 for the last day. */
  monthDay: number | null
  /** The 1st to 4th, or -1 for the last, `monthWeekday` of the month. */
  monthWeek: number | null
  monthWeekday: Weekday | null
  /** No task is created due after this day. */
  until: IsoDate | null
}

/** A rule as a client sends it: the server fills what's missing from the due date (else today). */
export interface RepeatRequest {
  unit: RepeatUnit
  every?: number
  basis?: RepeatBasis
  weekdays?: Weekday[]
  monthDay?: number | null
  monthWeek?: number | null
  monthWeekday?: Weekday | null
  until?: IsoDate | null
}

export interface Task {
  id: Uuid
  listId: Uuid
  statusId: Uuid
  parentTaskId: Uuid | null
  title: string
  /** The description itself only comes with TaskDetail. */
  hasDescription: boolean
  priority: Priority
  /** Ordered by assignment time. */
  assigneeIds: Uuid[]
  /** Ordered by tag name. */
  tagIds: Uuid[]
  startDate: IsoDate | null
  dueDate: IsoDate | null
  position: number
  createdById: Uuid
  createdAt: IsoTimestamp
  updatedAt: IsoTimestamp
  /** Set while the task is in a Done status. */
  completedAt: IsoTimestamp | null
  /** Direct subtasks, and how many of them are done. */
  subtaskCount: number
  subtaskDoneCount: number
  commentCount: number
  attachmentCount: number
  /** Only the newest task of a series has it (top-level tasks only). */
  repeat: RepeatRule | null
  /** The task this one's completion created, while that link stands. Reopening this task takes it back if untouched. */
  nextRepeatId: Uuid | null
}

export interface TaskRef {
  id: Uuid
  title: string
  listId: Uuid
}

/** Someone a comment mentions, by the name they have now. */
export interface Mention {
  userId: Uuid
  displayName: string
}

export interface Comment {
  id: Uuid
  taskId: Uuid
  author: User
  /**
   * Plain text; a mention is the token `<@user id>` (lib/mentions.ts). Render with commentSegments,
   * never v-html.
   */
  body: string
  createdAt: IsoTimestamp
  editedAt: IsoTimestamp | null
  /** The people the body mentions. */
  mentions: Mention[]
}

/** One row of the Inbox (Features 2). Values are display values copied when it happened. */
export interface InboxItem {
  id: Uuid
  kind: NotificationKind
  taskId: Uuid
  taskTitle: string
  listId: Uuid
  /** Who did it; null for reminders. */
  actor: User | null
  from: string | null
  to: string | null
  /** The comment of a Mentioned or Commented row. */
  commentId: Uuid | null
  createdAt: IsoTimestamp
  readAt: IsoTimestamp | null
}

/** GET /api/workspaces/{id}/notifications: a page of the Inbox, newest first. */
export interface InboxPage {
  items: InboxItem[]
  /** Every unread row, not only this page's. */
  unread: number
  hasMore: boolean
}

/** PATCH /api/me/notifications: only what is sent changes. */
export interface UpdateNotificationSettingsRequest {
  emailMentions?: boolean
  emailReminders?: boolean
  timeZone?: string
}

export interface Attachment {
  id: Uuid
  taskId: Uuid
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedById: Uuid
  createdAt: IsoTimestamp
}

export interface Activity {
  id: Uuid
  taskId: Uuid
  actor: User
  kind: ActivityKind
  /** Display values copied when the change happened (names, dates, enum names). */
  from: string | null
  to: string | null
  createdAt: IsoTimestamp
}

export interface TaskDetail {
  task: Task
  /** Sanitized HTML. Render only through Tiptap. "" when empty. */
  description: string
  workspaceId: Uuid
  /** Who created the task (panel footer). */
  createdBy: User
  parent: TaskRef | null
  subtasks: Task[]
  comments: Comment[]
  attachments: Attachment[]
  /** Latest 200 entries, oldest first. */
  activity: Activity[]
}

export interface WorkspaceTasksResponse {
  tasks: Task[]
  /** Parents of returned subtasks that are not themselves in `tasks`. */
  parents: TaskRef[]
}

export interface BulkUpdateResponse {
  /** Same order as the request's `updates`. */
  tasks: Task[]
}

export interface AuthProvider {
  key: string
  displayName: string
}

export interface AuthProviders {
  devLogin: boolean
  providers: AuthProvider[]
}

/** Body of 400 and 409 responses. `detail` is one sentence the UI may show as is. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
}

// ---- Requests ----
// Optional `id` on create requests: send newId() so the row exists in the store before the server answers.

export interface DevLoginRequest {
  email: string
  displayName?: string
}

export interface UpdateMeRequest {
  displayName: string
}

export interface CreateWorkspaceRequest {
  id?: Uuid
  name: string
}

export interface RenameRequest {
  name: string
}

export interface AddMemberRequest {
  email: string
  role: AssignableRole
  /** From "Name <email>"; used only when a new placeholder user is created. */
  displayName?: string
}

export interface UpdateMemberRequest {
  role: AssignableRole
}

export interface CreateTagRequest {
  id?: Uuid
  name: string
  color?: string
}

export interface UpdateTagRequest {
  name?: string
  color?: string
}

export interface CreateSpaceRequest {
  id?: Uuid
  name: string
  color?: string
  /** Only the creator and `memberIds` will see it. */
  isPrivate?: boolean
  /** Workspace members to add to a private space besides the creator. */
  memberIds?: Uuid[]
}

export interface UpdateSpaceRequest {
  name?: string
  color?: string
  position?: number
  /** true keeps the space to its people (the caller is added); false shows it to everyone and clears its people. */
  isPrivate?: boolean
}

export interface AddSpaceMembersRequest {
  userIds: Uuid[]
}

export interface CreateListRequest {
  id?: Uuid
  name: string
  /** Defaults to 'Review' on the server; the New list dialog sends 'Simple' by default. */
  statusPreset?: StatusPreset
  /** Copies that list's statuses; wins over statusPreset. */
  statusesFromListId?: Uuid
  /** "#RRGGBB"; absent or "" for none. */
  color?: string
  /** A LIST_ICONS key; absent, "" or "list" for the plain list icon. */
  icon?: string
}

export interface UpdateListRequest {
  name?: string
  position?: number
  /** Moves the list to another space of the same workspace. */
  spaceId?: Uuid
  /** "#RRGGBB" sets the icon's colour, "" takes it away. */
  color?: string
  /** A LIST_ICONS key sets the icon, "" goes back to the plain list icon. */
  icon?: string
}

export interface CreateStatusRequest {
  id?: Uuid
  name: string
  color?: string
  category: StatusCategory
}

export interface UpdateStatusRequest {
  name?: string
  color?: string
  category?: StatusCategory
  position?: number
}

export interface CreateTaskRequest {
  id?: Uuid
  title: string
  statusId?: Uuid
  parentTaskId?: Uuid
  /** HTML; the server sanitizes it. */
  description?: string
  priority?: Priority
  assigneeIds?: Uuid[]
  tagIds?: Uuid[]
  startDate?: IsoDate | null
  dueDate?: IsoDate | null
  /** Where the task goes among tasks with the same status and parent. Default 'Bottom'. */
  placement?: Placement
  /** Top-level tasks only; a task without a due date gets the rule's first date. */
  repeat?: RepeatRequest
}

/** PATCH /api/tasks/{id}. Only present properties change; null clears a nullable field. */
export interface TaskPatch {
  title?: string
  description?: string | null
  priority?: Priority | null
  startDate?: IsoDate | null
  dueDate?: IsoDate | null
  statusId?: Uuid
  /** Replaces the whole set. */
  assigneeIds?: Uuid[]
  /** Replaces the whole set. */
  tagIds?: Uuid[]
  /** Moves a top-level task (and its subtasks) to another list of the same workspace. */
  listId?: Uuid
  /** Sets the position exactly. Sent only by undo; without it a status change puts the task at the top of its new status. */
  position?: number
  /** Top-level tasks only; null stops repeating. A task without a due date gets the rule's first date. */
  repeat?: RepeatRequest | null
}

export interface MoveTaskRequest {
  statusId: Uuid
  /** The neighbour ABOVE the drop point (smaller position), or null at the top. */
  beforeTaskId?: Uuid | null
  /** The neighbour BELOW the drop point (larger position), or null at the bottom. */
  afterTaskId?: Uuid | null
}

/** One item of POST /api/tasks/bulk. Same rules as TaskPatch; title and description are not allowed. */
export interface BulkTaskUpdate {
  id: Uuid
  statusId?: Uuid
  priority?: Priority | null
  startDate?: IsoDate | null
  dueDate?: IsoDate | null
  /** Replaces the set. Not combinable with add/removeAssigneeIds in the same item. */
  assigneeIds?: Uuid[]
  addAssigneeIds?: Uuid[]
  removeAssigneeIds?: Uuid[]
  /** Replaces the set. Not combinable with add/removeTagIds in the same item. */
  tagIds?: Uuid[]
  addTagIds?: Uuid[]
  removeTagIds?: Uuid[]
  listId?: Uuid
  /** Sets the position exactly (undo only). */
  position?: number
}

export interface BulkUpdateRequest {
  updates: BulkTaskUpdate[]
}

export interface BulkDeleteRequest {
  ids: Uuid[]
}

export interface CreateCommentRequest {
  id?: Uuid
  body: string
}

export interface UpdateCommentRequest {
  body: string
}

export interface DevSeedRequest {
  workspaceId: Uuid
  /** Filler tasks to add; default 48, 0–2000. */
  fillers?: number
}

/** Query of GET /api/workspaces/{id}/tasks; api.ts turns it into a query string. */
export interface WorkspaceTasksQuery {
  scope?: TaskScope
  q?: string
  includeDone?: boolean
  doneSince?: IsoDate
  limit?: number
}

// ---- Client-side shared types ----

export type ViewKind = 'list' | 'board' | 'calendar'
export type ThemePreference = 'system' | 'light' | 'dark'

// ---- Ordering and labels ----

/** Display rank of status categories. Statuses display by (category rank, position). */
export const STATUS_CATEGORY_ORDER: Record<StatusCategory, number> = { NotStarted: 0, Active: 1, Done: 2 }

/** Sort rank of priorities; None sorts last in both directions (lib/taskQuery.ts). */
export const PRIORITY_ORDER: Record<Priority, number> = { Urgent: 0, High: 1, Normal: 2, Low: 3, None: 4 }

export const STATUS_CATEGORY_LABEL: Record<StatusCategory, string> = {
  NotStarted: 'Not started',
  Active: 'Active',
  Done: 'Done',
}

export const PRIORITY_LABEL: Record<Priority, string> = {
  Urgent: 'Urgent',
  High: 'High',
  Normal: 'Normal',
  Low: 'Low',
  None: 'No priority',
}

/** Menus list priorities in this order. */
export const PRIORITIES: Priority[] = ['Urgent', 'High', 'Normal', 'Low', 'None']

export const ROLE_LABEL: Record<Role, string> = { Owner: 'Owner', Admin: 'Admin', Member: 'Member', Guest: 'Guest' }

/** Plain-language role descriptions used by the invite dialog and the members page. */
export const ROLE_DESCRIPTION: Record<Role, string> = {
  Owner: 'Owns the workspace and can delete it.',
  Admin: 'Also manages people and settings.',
  Member: 'Creates and edits tasks and lists.',
  Guest: 'Views and comments only.',
}

// ---- Dashboard (GET /api/workspaces/{id}/dashboard; Endpoints/DashboardEndpoints.cs) ----

export type DashboardScope = 'all' | 'mine'

export interface DashboardTotals {
  total: number
  open: number
  done: number
  /** Open and due before today. */
  overdue: number
  dueToday: number
  /** Open without a due date. */
  noDueDate: number
  /** Open and assigned to nobody. */
  unassigned: number
  subtasks: number
  completedLast7: number
  createdLast7: number
}

/** Open tasks by due date; nextWeek is 2 to 7 days out. */
export interface DashboardDue {
  overdue: number
  today: number
  tomorrow: number
  nextWeek: number
  later: number
  none: number
}

export interface DashboardPerson {
  /** null: the unassigned row. */
  userId: Uuid | null
  open: number
  overdue: number
  /** Open and due within the next 7 days. */
  dueSoon: number
  done: number
}

export interface DashboardTask {
  id: Uuid
  title: string
  listId: Uuid
  dueDate: IsoDate
  priority: Priority
  assigneeIds: Uuid[]
}

export interface Dashboard {
  today: IsoDate
  totals: DashboardTotals
  due: DashboardDue
  /** Tasks per status id. */
  byStatus: { id: Uuid; count: number }[]
  byPriority: { priority: Priority; open: number; done: number }[]
  byList: { listId: Uuid; open: number; done: number; overdue: number }[]
  byAssignee: DashboardPerson[]
  /** The last 14 days, oldest first. */
  trend: { date: IsoDate; created: number; completed: number }[]
  /** The most overdue open tasks, oldest due date first. */
  overdue: DashboardTask[]
}

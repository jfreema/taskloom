// Shared fixtures and helpers for the Taskloom end-to-end suite (spec 11.1). Scenario files import from here only:
//   import { test, expect, api, day, dragTo, expectNoAxeViolations, gotoMonth, stageC } from './fixtures'
// Every test gets the console rule automatically. The `seeded` fixture signs a fresh user in and seeds the 11.3
// dataset. Tag a test `{ tag: '@phone' }` to run it in the phone project (390×844) instead of desktop (1440×900).
import { AxeBuilder } from '@axe-core/playwright'
import {
  test as base,
  expect,
  type APIRequestContext,
  type APIResponse,
  type Locator,
  type Page,
  type TestInfo,
} from '@playwright/test'
import type {
  IsoDate,
  ListSummary,
  Member,
  ProblemDetails,
  Space,
  Tag,
  Task,
  TaskDetail,
  TaskPatch,
  User,
  Uuid,
  Workspace,
} from '../src/types'

/** Playwright's expect, re-exported so a scenario file needs a single import. */
export { expect }
/** Every API wire type (web/src/types.ts), for typing what api(page) returns. */
export type * from '../src/types'

// ---- Constants ----

/** The CSRF header that every non-GET call to /api/*, /auth/dev-login and /auth/logout must carry (spec 5.1). */
export const CSRF_HEADER = { 'X-Taskloom': '1' } as const

/** 'B' while a stage-B engineer runs their own scenarios (TASKLOOM_E2E_STAGE=B), otherwise 'C' (the whole suite). */
export const STAGE: 'B' | 'C' = process.env.TASKLOOM_E2E_STAGE === 'B' ? 'B' : 'C'

/** Playwright's key name for Mod (Meta on macOS, Control elsewhere): "Mod+K" is page.keyboard.press(MOD + '+K'). */
export const MOD = 'ControlOrMeta'

/** The display name that signIn() and the seeded fixture give a new user unless told otherwise. */
export const DEFAULT_DISPLAY_NAME = 'Jared Doe'

/** The axe-core rule tags that expectNoAxeViolations() runs (spec 11.1); wcag22aa includes target-size (24px). */
export const AXE_TAGS: readonly string[] = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']

// ---- API access ----

/** Thrown by signIn(), seed() and the api() JSON helpers on a non-2xx answer; `detail` is the ProblemDetails sentence. */
export class ApiError extends Error {
  readonly status: number
  readonly detail: string | undefined

  constructor(message: string, status: number, detail: string | undefined) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.detail = detail
  }

  /** Builds the error for a failed response, with its ProblemDetails `detail` when the body has one. */
  static async from(res: APIResponse, method: string, url: string, hint = ''): Promise<ApiError> {
    const body = await res.text().catch(() => '')
    let detail: string | undefined
    try {
      const parsed = JSON.parse(body) as ProblemDetails | null
      if (typeof parsed?.detail === 'string') detail = parsed.detail
    } catch {
      // Not JSON (an empty 401 or 404, or an HTML page).
    }
    const said = detail ?? body.slice(0, 200)
    return new ApiError(`${method} ${url} answered ${res.status()}${said ? `: ${said}` : ''}${hint}`, res.status(), detail)
  }
}

/** The methods of api(page)'s JSON helper. */
export type ApiMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** What api(page) returns: page.request's methods with X-Taskloom: 1 added, plus typed JSON helpers. */
export interface Api {
  get: APIRequestContext['get']
  post: APIRequestContext['post']
  put: APIRequestContext['put']
  patch: APIRequestContext['patch']
  delete: APIRequestContext['delete']
  head: APIRequestContext['head']
  fetch: APIRequestContext['fetch']
  /** Sends `data` as JSON and returns the parsed answer (undefined for an empty one); throws ApiError on a non-2xx. */
  json<T = unknown>(method: ApiMethod, url: string, data?: unknown): Promise<T>
  /** GET /api/me */
  me(): Promise<User>
  /** GET /api/workspaces: every workspace tree the user can see, oldest first. */
  workspaces(): Promise<Workspace[]>
  /** GET /api/lists/{id}/tasks: all tasks of the list, subtasks included. */
  listTasks(listId: Uuid): Promise<Task[]>
  /** GET /api/tasks/{id} */
  task(id: Uuid): Promise<TaskDetail>
  /** PATCH /api/tasks/{id} */
  patchTask(id: Uuid, patch: TaskPatch): Promise<Task>
  /** GET /api/workspaces/{id}/members */
  members(workspaceId: Uuid): Promise<Member[]>
  /** GET /api/workspaces/{id}/tags */
  tags(workspaceId: Uuid): Promise<Tag[]>
}

function withCsrf<O extends { headers?: { [key: string]: string } }>(options?: O): O {
  return { ...(options as O), headers: { ...CSRF_HEADER, ...options?.headers } }
}

/** Wraps page.request (the page context's cookies and baseURL) so every call sends X-Taskloom: 1; for persistence checks. */
export function api(page: Page): Api {
  const request = page.request
  const json = async <T = unknown>(method: ApiMethod, url: string, data?: unknown): Promise<T> => {
    const res = await request.fetch(url, { method, headers: CSRF_HEADER, data })
    if (!res.ok()) throw await ApiError.from(res, method, url)
    const text = await res.text()
    return (text ? JSON.parse(text) : undefined) as T
  }
  return {
    get: (url, options) => request.get(url, withCsrf(options)),
    post: (url, options) => request.post(url, withCsrf(options)),
    put: (url, options) => request.put(url, withCsrf(options)),
    patch: (url, options) => request.patch(url, withCsrf(options)),
    delete: (url, options) => request.delete(url, withCsrf(options)),
    head: (url, options) => request.head(url, withCsrf(options)),
    fetch: (urlOrRequest, options) => request.fetch(urlOrRequest, withCsrf(options)),
    json,
    me: () => json<User>('GET', '/api/me'),
    workspaces: () => json<Workspace[]>('GET', '/api/workspaces'),
    listTasks: (listId) => json<Task[]>('GET', `/api/lists/${listId}/tasks`),
    task: (id) => json<TaskDetail>('GET', `/api/tasks/${id}`),
    patchTask: (id, patch) => json<Task>('PATCH', `/api/tasks/${id}`, patch),
    members: (workspaceId) => json<Member[]>('GET', `/api/workspaces/${workspaceId}/members`),
    tags: (workspaceId) => json<Tag[]>('GET', `/api/workspaces/${workspaceId}/tags`),
  }
}

// ---- Sign-in and data ----

let lastStamp = 0

function slugify(text: string, max = 40): string {
  const slug = text.toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, max).replace(/^-+|-+$/g, '')
  return slug || 'test'
}

/** A fresh dev-login email, e2e-<slug>-<timestamp>@example.com; the timestamp never repeats within a worker. */
export function uniqueEmail(slug: string): string {
  lastStamp = Math.max(Date.now(), lastStamp + 1)
  return `e2e-${slugify(slug)}-${lastStamp}@example.com`
}

// The file name without ".spec.ts" and the test's title path; a repeat (--repeat-each) gets its own suffix so two
// workers running the same test at the same millisecond still get different users.
function testSlug(info: TestInfo): string {
  const [file = '', ...titles] = info.titlePath
  const stem = (file.split(/[\\/]/).pop() ?? '').replace(/(\.spec)?\.ts$/, '')
  const repeat = info.repeatEachIndex ? `-r${info.repeatEachIndex}` : ''
  return slugify(`${stem} ${titles.join(' ')}`, 40 - repeat.length) + repeat
}

/** Options of signIn(): the email to sign in with, and the display name a new user gets (default "Jared Doe"). */
export interface SignInOptions {
  email: string
  displayName?: string
}

/** Signs in with POST /auth/dev-login on page.request, so the session cookie lands in the page's browser context. */
export async function signIn(page: Page, { email, displayName = DEFAULT_DISPLAY_NAME }: SignInOptions): Promise<User> {
  return test.step(
    `sign in as ${email}`,
    async () => {
      const res = await page.request.post('/auth/dev-login', { headers: CSRF_HEADER, data: { email, displayName } })
      if (!res.ok()) {
        const hint = res.status() === 404 ? ' (is dev login enabled, and does TASKLOOM_E2E_URL point at Taskloom?)' : ''
        throw await ApiError.from(res, 'POST', '/auth/dev-login', hint)
      }
      return (await res.json()) as User
    },
    { box: true },
  )
}

/** Options of seed(): the number of filler tasks (default 48, 0–2000) and the workspace (default the first one). */
export interface SeedOptions {
  fillers?: number
  workspaceId?: Uuid
}

/** A list as seed() returns it: the ListSummary plus its status ids by name ("To do", "In progress", …). */
export interface SeededList extends ListSummary {
  statusIds: Record<string, Uuid>
}

/** What seed() returns: the copy of the 11.3 dataset it just added, read back through the API and keyed by name. */
export interface SeedResult {
  workspaceId: Uuid
  /** The workspace tree right after the seed. */
  workspace: Workspace
  /** Spaces by name: "Work" (created by the seed) and "Personal". */
  spaces: Record<string, Space>
  /** Lists by name: "Website refresh", "Admin" and "Tasks"; a list this seed created wins over an older namesake. */
  lists: Record<string, SeededList>
  /** Every task this seed created, subtasks included, by title, as it was right after the seed. */
  tasks: Record<string, Task>
  /** The workspace's tags by name (design, bug, docs, ops); empty while the API has no tag endpoints (404). */
  tags: Record<string, Tag>
  /** The workspace's members by display name: you, "Ana Moreno" (Member) and "Sam Lee" (Guest). */
  members: Record<string, Member>
}

const listsOf = (workspace: Workspace): ListSummary[] => workspace.spaces.flatMap((space) => space.lists)

// Older rows first, so that Object.fromEntries lets the rows this seed created win a name clash.
const oldFirst = <T extends { id: Uuid }>(rows: T[], oldIds: Set<Uuid>): T[] => [
  ...rows.filter((row) => oldIds.has(row.id)),
  ...rows.filter((row) => !oldIds.has(row.id)),
]

const byName = <T>(rows: T[], name: (row: T) => string): Record<string, T> =>
  Object.fromEntries(rows.map((row) => [name(row), row]))

/** Seeds the 11.3 dataset into the first workspace (POST /api/dev/seed) and returns what it added, by name. */
export async function seed(page: Page, { fillers, workspaceId }: SeedOptions = {}): Promise<SeedResult> {
  return test.step(
    'seed the 11.3 dataset',
    async () => {
      const client = api(page)
      const before = await client.workspaces()
      const id = workspaceId ?? before[0]?.id
      const old = before.find((w) => w.id === id)
      if (!id || !old) throw new Error(`seed(): the signed-in user has no workspace${workspaceId ? ` ${workspaceId}` : ''}`)
      const oldSpaceIds = new Set(old.spaces.map((s) => s.id))
      const oldListIds = new Set(listsOf(old).map((l) => l.id))
      const oldTasks = await Promise.all(listsOf(old).map((l) => client.listTasks(l.id)))
      const oldTaskIds = new Set(oldTasks.flat().map((t) => t.id))

      const data = fillers === undefined ? { workspaceId: id } : { workspaceId: id, fillers }
      const res = await client.post('/api/dev/seed', { data })
      if (!res.ok()) {
        const hint = res.status() === 404 ? ' (the seed exists only when dev login is enabled)' : ''
        throw await ApiError.from(res, 'POST', '/api/dev/seed', hint)
      }

      const workspace = (await client.workspaces()).find((w) => w.id === id)
      if (!workspace) throw new Error(`seed(): workspace ${id} disappeared`)
      const lists = listsOf(workspace)
      const [taskLists, members, tags] = await Promise.all([
        Promise.all(lists.map((l) => client.listTasks(l.id))),
        client.members(id),
        client.tags(id).catch((error: unknown) => {
          if (!(error instanceof ApiError) || error.status !== 404) throw error
          test.info().annotations.push({ type: 'warning', description: 'seed(): this API has no tag endpoints yet, so tags is {}' })
          return []
        }),
      ])
      return {
        workspaceId: id,
        workspace,
        spaces: byName(oldFirst(workspace.spaces, oldSpaceIds), (s) => s.name),
        lists: byName(
          oldFirst(lists, oldListIds).map((l): SeededList => ({
            ...l,
            statusIds: Object.fromEntries(l.statuses.map((s) => [s.name, s.id])),
          })),
          (l) => l.name,
        ),
        tasks: byName(taskLists.flat().filter((t) => !oldTaskIds.has(t.id)), (t) => t.title),
        tags: byName(tags, (t) => t.name),
        members: byName(members, (m) => m.displayName),
      }
    },
    { box: true },
  )
}

/** What the seeded fixture gives a test: the seed result, the signed-in user, and the email it signed in with. */
export interface Seeded extends SeedResult {
  me: User
  email: string
}

// ---- Fixtures ----

/** An allowed console message: a RegExp it must match, or a string it must contain. */
export type ConsolePattern = string | RegExp

// Chromium logs every 4xx and 5xx response like this (the signed-out GET /api/me 401 included), so these never fail a test.
const RESOURCE_ERROR = 'Failed to load resource'

const allowed = (text: string, patterns: ConsolePattern[]): boolean =>
  text.startsWith(RESOURCE_ERROR) ||
  patterns.some((p) => (typeof p === 'string' ? text.includes(p) : text.search(p) !== -1))

/** The fixtures this file adds to Playwright's own. */
export interface TaskloomFixtures {
  /** More console errors this test allows: test.use({ allowConsoleErrors: [/…/] }). Applies to page errors too. */
  allowConsoleErrors: ConsolePattern[]
  /** Signed in as a fresh user (a unique email per test) whose first workspace holds the 11.3 dataset. */
  seeded: Seeded
  /** The console rule (automatic): the test fails on a page error or a console error that isn't exempt. */
  consoleRule: void
}

/** Playwright's test with the console rule and the seeded and allowConsoleErrors fixtures. */
export const test = base.extend<TaskloomFixtures>({
  allowConsoleErrors: [[], { option: true }],

  consoleRule: [
    async ({ context, allowConsoleErrors }, use) => {
      const problems: string[] = []
      const record = (kind: string, text: string, where?: string) => {
        if (!allowed(text, allowConsoleErrors)) problems.push(`${kind}: ${text}${where ? ` (${where})` : ''}`)
      }
      const watch = (page: Page) => {
        page.on('pageerror', (error) => record('pageerror', `${error.name}: ${error.message}`))
        page.on('console', (message) => {
          if (message.type() === 'error') record('console.error', message.text(), message.location().url || undefined)
        })
      }
      context.pages().forEach(watch)
      context.on('page', watch)
      await use()
      if (problems.length) {
        throw new Error(
          `The console rule (spec 11.1) failed this test: ${problems.length} page or console error(s). ` +
            `Exempt are "${RESOURCE_ERROR}…" and the test's allowConsoleErrors.\n  ${problems.join('\n  ')}`,
        )
      }
    },
    { auto: true, title: 'console rule' },
  ],

  seeded: async ({ page }, use, testInfo) => {
    const email = uniqueEmail(testSlug(testInfo))
    const me = await signIn(page, { email })
    const result = await seed(page)
    await use({ ...result, me, email })
  },
})

// ---- Accessibility ----

// @axe-core/playwright types its page with the top-level playwright-core (an npm-installed peer, 1.64.1), while
// @playwright/test brings its own 1.63.0, whose Page lacks two newer members. Axe uses only evaluate(), frames() and
// context().newPage() (a blank page for its last step, closed again), so the cast is safe at runtime.
type AxePage = ConstructorParameters<typeof AxeBuilder>[0]['page']

/** Options of expectNoAxeViolations(): CSS selectors to limit the check to (default the whole page). */
export interface AxeOptions {
  include?: string | string[]
}

/** Runs axe (AXE_TAGS) on the page and fails on any serious or critical violation, listing each rule and its elements. */
export async function expectNoAxeViolations(page: Page, { include }: AxeOptions = {}): Promise<void> {
  await test.step(
    'no serious or critical axe violations',
    async () => {
      let builder = new AxeBuilder({ page: page as unknown as AxePage }).withTags([...AXE_TAGS])
      for (const selector of include === undefined ? [] : [include].flat()) builder = builder.include(selector)
      const { violations } = await builder.analyze()
      const blocking = violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
      if (!blocking.length) return
      await test.info().attach('axe-violations.json', { body: JSON.stringify(blocking, null, 2), contentType: 'application/json' })
      const lines = blocking.map((v) => {
        const targets = v.nodes.slice(0, 5).map((n) => n.target.join(' >> '))
        const more = v.nodes.length > 5 ? ` and ${v.nodes.length - 5} more` : ''
        return `${v.id} (${v.impact}): ${v.help}. ${targets.join(', ')}${more}`
      })
      throw new Error(`${blocking.length} serious or critical axe violation(s):\n  ${lines.join('\n  ')}`)
    },
    { box: true },
  )
}

// ---- Drag and drop ----

/** Options of dragTo(): grab and drop points relative to each element's top-left (default: centre), and steps (≥ 10). */
export interface DragOptions {
  sourcePosition?: { x: number; y: number }
  targetPosition?: { x: number; y: number }
  steps?: number
}

async function pointIn(locator: Locator, position?: { x: number; y: number }): Promise<{ x: number; y: number }> {
  const box = await locator.boundingBox()
  if (!box) throw new Error(`dragTo(): ${locator} is not visible`)
  return { x: box.x + (position?.x ?? box.width / 2), y: box.y + (position?.y ?? box.height / 2) }
}

/** Drags with real pointer movement, which Pragmatic drag and drop needs: hover, press, ≥ 10 steps, 2px more, release. */
export async function dragTo(page: Page, source: Locator, target: Locator, options: DragOptions = {}): Promise<void> {
  await test.step(
    'drag and drop',
    async () => {
      await source.hover({ position: options.sourcePosition })
      const from = await pointIn(source, options.sourcePosition)
      await page.mouse.down()
      const to = await pointIn(target, options.targetPosition)
      await page.mouse.move(to.x, to.y, { steps: Math.max(10, options.steps ?? 10) })
      // 2px further along the drag, so that dragover fires on the target after it has been entered.
      const length = Math.hypot(to.x - from.x, to.y - from.y)
      const [dx, dy] = length ? [(2 * (to.x - from.x)) / length, (2 * (to.y - from.y)) / length] : [0, 2]
      await page.mouse.move(to.x + dx, to.y + dy)
      await page.mouse.up()
    },
    { box: true },
  )
}

// ---- Calendar ----

/** A day relative to T (today's UTC date), in the formats the screens show (lib/dates.ts, locale en-US). */
export interface Day {
  /** "2026-11-06" */
  iso: IsoDate
  /** "Friday, November 6": the calendar gridcell name prefix, and a chip's due date. */
  label: string
  /** Matches that day's whole gridcell name ("Friday, November 6, 2 tasks") and no other day's. */
  cell: RegExp
  /** "Nov 6" ("Add task on Nov 6"), with ", 2027" outside T's year. */
  short: string
  /** "Fri, Nov 6" (undo toasts, agenda headings), with ", 2027" outside T's year. */
  medium: string
  /** "November 2026": the calendar title of the day's month. */
  month: string
}

const format = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', ...options })
const LONG = format({ weekday: 'long', month: 'long', day: 'numeric' })
const SHORT = format({ month: 'short', day: 'numeric' })
const SHORT_WITH_YEAR = format({ month: 'short', day: 'numeric', year: 'numeric' })
const MEDIUM = format({ weekday: 'short', month: 'short', day: 'numeric' })
const MEDIUM_WITH_YEAR = format({ weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' })
const MONTH_TITLE = format({ month: 'long', year: 'numeric' })
const MONTH_NAMES = Array.from({ length: 12 }, (_, m) => format({ month: 'long' }).format(Date.UTC(2026, m, 1)))
const MONTH_TITLE_PATTERN = new RegExp(`^(${MONTH_NAMES.join('|')}) (\\d{4})$`)

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

/** T + offset days as an ISO date and as the screens' labels. Look a day up only after gotoMonth() to its month. */
export function day(offset = 0): Day {
  const now = new Date()
  const date = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + offset))
  const sameYear = date.getUTCFullYear() === now.getUTCFullYear()
  const label = LONG.format(date)
  return {
    iso: date.toISOString().slice(0, 10),
    label,
    cell: new RegExp(`^${escapeRegExp(label)}(?:,|$)`),
    short: (sameYear ? SHORT : SHORT_WITH_YEAR).format(date),
    medium: (sameYear ? MEDIUM : MEDIUM_WITH_YEAR).format(date),
    month: MONTH_TITLE.format(date),
  }
}

// Months since year 0 of a calendar title like "November 2026", so two titles compare in time order.
const monthNumber = (title: string): number => {
  const match = MONTH_TITLE_PATTERN.exec(title)
  if (!match) throw new Error(`gotoMonth(): "${title}" is not a month title like "November 2026"`)
  return Number(match[2]) * 12 + MONTH_NAMES.indexOf(match[1])
}

/** Presses "Next month"/"Previous month" in #app (never in an open date picker) until the calendar shows isoDate's month. */
export async function gotoMonth(page: Page, isoDate: IsoDate): Promise<void> {
  const [year, month] = isoDate.split('-').map(Number)
  const target = MONTH_TITLE.format(Date.UTC(year, month - 1, 1))
  await test.step(
    `go to ${target}`,
    async () => {
      const app = page.locator('#app')
      const title = app.getByText(MONTH_TITLE_PATTERN).filter({ visible: true }).first()
      for (let clicks = 0; clicks <= 120; clicks++) {
        await expect(title, 'the calendar title ("November 2026")').toBeVisible()
        const shown = (await title.innerText()).trim()
        if (shown === target) return
        const name = monthNumber(shown) < monthNumber(target) ? 'Next month' : 'Previous month'
        await app.getByRole('button', { name, exact: true }).click()
        await expect(title).not.toHaveText(shown)
      }
      throw new Error(`gotoMonth(): the calendar didn't reach ${target}`)
    },
    { box: true },
  )
}

// ---- Stages ----

/** Runs a step marked (C) in 11.1 with test.step; under TASKLOOM_E2E_STAGE=B it is skipped with test.step.skip. */
export async function stageC(title: string, body: () => Promise<void>): Promise<void> {
  if (STAGE === 'B') await test.step.skip(title, body)
  else await test.step(title, body)
}

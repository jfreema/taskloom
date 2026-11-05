// Platform scenarios (spec 11.1, 27–31): a guest's read-only workspace, the phone layout, axe on every screen in
// both themes, the review screenshots (web/e2e/screenshots/<screen>-<theme>-<width>.png) and recovery from a failed
// save. They cross every screen, so they run in stage C, once every screen is merged. Saved values are checked
// through api(page) or after a reload.
import path from 'node:path'
import type { Locator, Page, Request } from '@playwright/test'
import { MOD, api, dragTo, expect, expectNoAxeViolations, signIn, test, type Seeded } from './fixtures'

const LIST = 'Website refresh'

type Theme = 'light' | 'dark'
const THEMES: readonly Theme[] = ['light', 'dark']

const escapeRegExp = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

// ---- Landmarks, rows and cards (the 11.1 selector contract) ----

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })
const views = (page: Page): Locator => page.getByRole('navigation', { name: 'Views' })
const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** The toast holding `text` (a status, or an alert for errors). */
const toast = (page: Page, text: string): Locator =>
  notifications(page).locator('[role="status"], [role="alert"]').filter({ hasText: text })

const grid = (page: Page, list = LIST): Locator => page.getByRole('treegrid', { name: `${list} tasks` })

/** A List row by its task title. */
const row = (page: Page, title: string, list = LIST): Locator =>
  grid(page, list).getByRole('row', { name: title, exact: true })

/** The task rows the List renders. */
const taskRows = (page: Page, list = LIST): Locator => grid(page, list).locator('[role="row"][data-task-row]')

const board = (page: Page): Locator => page.getByRole('region', { name: 'Board', exact: true })

/** A Board column, the region named "<status>, N tasks". */
const column = (page: Page, status: string): Locator =>
  board(page).getByRole('region', { name: new RegExp(`^${escapeRegExp(status)}, \\d+ tasks?$`) })

/** A Board card by its task title. */
const card = (scope: Page | Locator, title: string): Locator => scope.getByRole('article', { name: title, exact: true })

/** The task panel: an aside named by the task title when docked, otherwise a dialog (8.10). */
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))

/** Every field button of the List and the panel: "Status: To do, change", "Assignees: none, add", … */
const FIELD_BUTTON = /^(Status|Assignees|Due date|Start date|Priority|Tags): .+, (change|add)$/

// ---- Navigation ----

async function gotoHome(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: /^Good (morning|afternoon|evening)/ })).toBeVisible()
  await expect(page.getByRole('heading', { level: 2, name: 'Today' })).toBeVisible()
  // Rows mount in batches (S10): the page is aria-busy until they all have.
  await expect(page.locator('main [aria-busy="true"]')).toHaveCount(0)
}

/** Opens a list's List view (optionally with a task's panel open) and waits for its rows. */
async function gotoList(page: Page, seeded: Seeded, o: { list?: string; task?: string } = {}): Promise<void> {
  const list = o.list ?? LIST
  const query = o.task ? `?task=${seeded.tasks[o.task].id}` : ''
  await page.goto(`/l/${seeded.lists[list].id}/list${query}`)
  await expect(taskRows(page, list).first()).toBeVisible()
  await expect(page.getByRole('treegrid', { name: `${list} tasks` })).not.toHaveAttribute('aria-busy', 'true')
  if (o.task) await panelLoaded(page, o.task)
}

/** Opens a list's Board and waits for its cards. */
async function gotoBoard(page: Page, seeded: Seeded, list = LIST): Promise<void> {
  await page.goto(`/l/${seeded.lists[list].id}/board`)
  await expect(board(page).getByRole('article').first()).toBeVisible()
  await expect(board(page).locator('[aria-busy="true"]')).toHaveCount(0)
}

/** Opens a list's Calendar: the month grid, or the agenda below 640px of page width. */
async function gotoCalendar(page: Page, seeded: Seeded, list = LIST): Promise<void> {
  await page.goto(`/l/${seeded.lists[list].id}/calendar`)
  await expect(
    page.getByRole('grid', { name: 'Calendar' }).or(page.getByRole('heading', { name: 'Today', exact: true })),
  ).toBeVisible()
}

/** Waits until the open task panel shows its detail (description and activity), not skeletons. */
async function panelLoaded(page: Page, title: string): Promise<Locator> {
  const panel = taskPanel(page, title)
  await expect(panel).toBeVisible()
  await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()
  await expect(panel.getByRole('textbox', { name: 'Write a comment' })).toBeVisible()
  await expect(panel.locator('[aria-busy="true"]')).toHaveCount(0)
  return panel
}

// ---- Themes, motion and settling ----

/** Waits until no CSS transition runs (colours following the theme, a toast fading), so pixels and contrast settle. */
async function settled(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document.getAnimations().every((a) => !(a instanceof CSSTransition) || a.playState !== 'running'),
  )
}

/** Follows the system theme (the default) in `theme`, with reduced motion (11.1 Motion). */
async function setTheme(page: Page, theme: Theme): Promise<void> {
  await page.emulateMedia({ colorScheme: theme, reducedMotion: 'reduce' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe(theme)
  await settled(page)
}

/** Dismisses every toast and waits until they are gone, so none fades out while axe measures contrast. */
async function clearToasts(page: Page): Promise<void> {
  await expect(async () => {
    for (const dismiss of await notifications(page).getByRole('button', { name: 'Dismiss' }).all()) {
      await dismiss.click({ timeout: 1000 }).catch(() => undefined)
    }
    await expect(notifications(page).locator('[role="status"], [role="alert"]')).toHaveCount(0, { timeout: 1000 })
  }).toPass({ timeout: 10_000 })
}

/** axe in the light theme, then the dark one, then back to light (scenario 29). */
async function axeBothThemes(page: Page, include?: string | string[]): Promise<void> {
  await clearToasts(page)
  for (const theme of THEMES) {
    await setTheme(page, theme)
    await expectNoAxeViolations(page, { include })
  }
  await setTheme(page, 'light')
}

/** Saves web/e2e/screenshots/<screen>-<theme>-<width>.png of the viewport, with settled fonts and colours. */
async function shot(page: Page, screen: string, theme: Theme): Promise<void> {
  await setTheme(page, theme)
  await page.mouse.move(0, 0)
  await page.evaluate(async () => {
    await document.fonts.ready
  })
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0)
  await settled(page)
  const width = page.viewportSize()?.width ?? 0
  const file = path.join(test.info().project.testDir, 'screenshots', `${screen}-${theme}-${width}.png`)
  await page.screenshot({ path: file, animations: 'disabled', caret: 'hide' })
}

/** Both themes of one screen. */
async function shots(page: Page, screen: string): Promise<void> {
  for (const theme of THEMES) await shot(page, screen, theme)
  await setTheme(page, 'light')
}

/** Records the requests that would change tasks: PATCH, move, bulk and delete. */
function taskWrites(page: Page): Request[] {
  const seen: Request[] = []
  page.on('request', (r) => {
    const { pathname } = new URL(r.url())
    if (r.method() !== 'GET' && /^\/api\/tasks\//.test(pathname) && !/\/comments$/.test(pathname)) seen.push(r)
  })
  return seen
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

// ---- 27 ----

test.describe('Guest', () => {
  test('27 a guest is labelled in pickers, sees read-only screens and can only comment', async ({ page, seeded }) => {
    const website = seeded.lists[LIST]
    const guestEmail = `guest-${Date.now()}${test.info().parallelIndex}@example.com`
    let guestName = ''

    await test.step('the owner invites the guest as Guest', async () => {
      await page.goto('/settings/members')
      await expect(page.getByRole('table', { name: 'Members' })).toBeVisible()
      await page.getByRole('button', { name: 'Invite people' }).click()
      const dialog = page.getByRole('dialog', { name: 'Invite people' })
      await dialog.getByRole('textbox', { name: 'Email addresses' }).fill(guestEmail)
      await dialog.getByRole('radio', { name: 'Guest' }).check()
      await expect(dialog.getByRole('radio', { name: /^Guest/ })).toBeChecked()
      await dialog.getByRole('button', { name: 'Add 1 person' }).click()
      await expect(dialog).toBeHidden()
      const guest = (await api(page).members(seeded.workspaceId)).find((m) => m.email === guestEmail)
      expect(guest).toMatchObject({ role: 'Guest', pending: true })
      guestName = guest!.displayName
      await expect(toast(page, `Added ${guestName}. They get access when they sign in with this address.`)).toBeVisible()
    })

    await test.step('the assignee picker labels the guest "Guest · view and comment only"', async () => {
      await gotoList(page, seeded)
      await row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Assignees: none, add' }).click()
      const option = page.getByRole('option', { name: new RegExp(`^${escapeRegExp(guestName)}`) })
      await expect(option).toBeVisible()
      await expect(option).toContainText('Guest · view and comment only')
      await page.keyboard.press('Escape')
      await expect(option).toBeHidden()
    })

    await test.step('control: the same drag moves a card for the owner', async () => {
      await gotoBoard(page, seeded)
      const plan = seeded.tasks['Plan Q1 roadmap']
      const moved = page.waitForResponse(
        (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === `/api/tasks/${plan.id}/move`,
      )
      await dragTo(page, card(column(page, 'To do'), plan.title), column(page, 'In progress'))
      expect((await moved).ok()).toBe(true)
      await expect(card(column(page, 'In progress'), plan.title)).toBeVisible()
    })

    const writes = taskWrites(page)

    await test.step('signed in as the guest: the guest notice, no "New task" and static pickers', async () => {
      await signIn(page, { email: guestEmail })
      await gotoList(page, seeded)
      await expect(sidebar(page).getByRole('button', { name: `Account: ${guestName}` })).toBeVisible()
      await expect(page.getByRole('status').filter({ hasText: "You're a guest" })).toBeVisible()
      await expect(page.getByRole('button', { name: 'New task' })).toHaveCount(0)
      await expect(grid(page).getByRole('button', { name: FIELD_BUTTON })).toHaveCount(0)
      await expect(grid(page).getByRole('checkbox')).toHaveCount(0)
      await expect(grid(page).getByRole('button', { name: 'Add task', exact: true })).toHaveCount(0)
      // The read-only row still shows its values, as text.
      await expect(row(page, 'Fix sign-in redirect')).toContainText('Urgent')
    })

    await test.step('dragging a card on the Board does nothing', async () => {
      await views(page).getByRole('link', { name: 'Board' }).click()
      await expect(card(column(page, 'To do'), 'Update pricing page')).toBeVisible()
      await expect(column(page, 'Review')).toHaveAccessibleName('Review, 6 tasks')
      await dragTo(page, card(page, 'Update pricing page'), column(page, 'Review'))
      // Give a drop that slipped through the time to reach the server.
      await page.waitForTimeout(500)
      await expect(card(column(page, 'To do'), 'Update pricing page')).toBeVisible()
      await expect(column(page, 'Review')).toHaveAccessibleName('Review, 6 tasks')
      expect(writes.map((r) => `${r.method()} ${new URL(r.url()).pathname}`)).toEqual([])
      const task = await api(page).task(seeded.tasks['Update pricing page'].id)
      expect(task.task.statusId).toBe(website.statusIds['To do'])
    })

    await test.step('the guest comments; only their own comment has a menu', async () => {
      await card(page, 'Write onboarding guide').click()
      const panel = await panelLoaded(page, 'Write onboarding guide')
      await expect(panel.getByRole('status').filter({ hasText: "You're a guest" })).toBeVisible()
      await expect(panel.getByRole('button', { name: FIELD_BUTTON })).toHaveCount(0)

      const composer = panel.getByRole('textbox', { name: 'Write a comment' })
      await composer.fill('Happy to review the draft.')
      const posted = page.waitForResponse(
        (r) => r.request().method() === 'POST' && new URL(r.url()).pathname.endsWith('/comments'),
      )
      await composer.press('Enter')
      expect((await posted).ok()).toBe(true)
      const mine = panel.getByRole('listitem').filter({ hasText: 'Happy to review the draft.' })
      await expect(mine).toBeVisible()
      await expect(mine.getByRole('button', { name: 'Comment actions' })).toHaveCount(1)
      for (const theirs of ['Draft is in the shared folder, comments welcome.', 'Looks good, can we add screenshots?']) {
        const item = panel.getByRole('listitem').filter({ hasText: theirs })
        await expect(item).toBeVisible()
        await expect(item.getByRole('button', { name: 'Comment actions' })).toHaveCount(0)
      }
      await expect(panel.getByRole('button', { name: 'Comment actions' })).toHaveCount(1)

      const detail = await api(page).task(seeded.tasks['Write onboarding guide'].id)
      expect(detail.comments.map((c) => [c.author.email, c.body])).toContainEqual([guestEmail, 'Happy to review the draft.'])
      expect(writes.map((r) => `${r.method()} ${new URL(r.url()).pathname}`)).toEqual([])
    })
  })
})

// ---- 28 ----

test.describe('Phone', () => {
  test('28 Home, the List, the Board and the Calendar on a phone @phone', async ({ page, seeded }) => {
    const website = seeded.lists[LIST]

    await test.step('Home shows the New task button (FAB)', async () => {
      await gotoHome(page)
      const fab = page.getByRole('button', { name: 'New task' })
      await expect(fab).toBeVisible()
      const box = await fab.boundingBox()
      expect(box && [Math.round(box.width), Math.round(box.height)]).toEqual([56, 56])
      expect(box!.x + box!.width).toBeGreaterThan(390 - 24)
    })

    await test.step('"Open navigation", then Website refresh: two-line rows and no sideways scroll', async () => {
      await page.getByRole('button', { name: 'Open navigation' }).tap()
      await sidebar(page).getByRole('link', { name: LIST, exact: true }).tap()
      await expect(page).toHaveURL(new RegExp(`/l/${website.id}/list`))
      await expect(sidebar(page)).toBeHidden()
      const plan = row(page, 'Plan Q1 roadmap')
      await expect(plan).toBeVisible()
      const rowBox = await plan.boundingBox()
      const title = await plan.getByText('Plan Q1 roadmap', { exact: true }).boundingBox()
      const due = await plan.getByRole('button', { name: /^Due date: / }).boundingBox()
      expect(rowBox!.height).toBeGreaterThanOrEqual(52)
      expect(due!.y).toBeGreaterThanOrEqual(title!.y + title!.height - 1)
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    })

    await test.step("tapping a row's status icon opens the status picker as a full-width popover", async () => {
      await row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Status: To do, change' }).tap()
      const picker = page.getByRole('listbox').filter({ has: page.getByRole('option', { name: 'Review' }) })
      await expect(picker).toBeVisible()
      // The viewport width minus the 8px gutters (the listbox sits inside the popover's 1px border).
      const box = await picker.boundingBox()
      expect(box!.x).toBeGreaterThanOrEqual(8)
      expect(box!.x).toBeLessThanOrEqual(10)
      expect(box!.x + box!.width).toBeGreaterThanOrEqual(390 - 10)
      expect(box!.x + box!.width).toBeLessThanOrEqual(390 - 8)
      await page.keyboard.press('Escape')
      await expect(picker).toBeHidden()
    })

    await test.step('the Board: columns about 85vw wide that snap', async () => {
      await views(page).getByRole('link', { name: 'Board' }).tap()
      await expect(page).toHaveURL(new RegExp(`/l/${website.id}/board`))
      await expect(card(column(page, 'To do'), 'Plan Q1 roadmap')).toBeVisible()
      const box = await column(page, 'To do').boundingBox()
      expect(Math.abs(box!.width - 0.85 * 390)).toBeLessThanOrEqual(2)
      expect(await board(page).evaluate((el) => getComputedStyle(el).scrollSnapType)).toBe('x mandatory')
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    })

    await test.step('the card menu moves "Plan Q1 roadmap" to Review', async () => {
      const plan = card(column(page, 'To do'), 'Plan Q1 roadmap')
      await plan.getByRole('button', { name: 'More actions for Plan Q1 roadmap' }).tap()
      const menu = page.getByRole('menu', { name: 'Actions for Plan Q1 roadmap' })
      await menu.getByRole('menuitem', { name: 'Move to status' }).tap()
      await page.getByRole('menuitemradio', { name: 'Review' }).or(page.getByRole('menuitem', { name: 'Review' })).tap()
      await expect(card(column(page, 'Review'), 'Plan Q1 roadmap')).toBeVisible()
      await expect(card(column(page, 'To do'), 'Plan Q1 roadmap')).toHaveCount(0)
      await expect
        .poll(async () => (await api(page).task(seeded.tasks['Plan Q1 roadmap'].id)).task.statusId)
        .toBe(website.statusIds.Review)
    })

    await test.step('tapping a card opens a full-screen sheet with Back, and ?task= in the URL', async () => {
      await card(column(page, 'Review'), 'Plan Q1 roadmap').tap()
      const sheet = page.getByRole('dialog', { name: 'Plan Q1 roadmap', exact: true })
      await expect(sheet).toBeVisible()
      await expect(sheet.getByRole('button', { name: 'Back' })).toBeVisible()
      await expect(page).toHaveURL(new RegExp(`[?&]task=${seeded.tasks['Plan Q1 roadmap'].id}`))
      await expect.poll(async () => {
        const box = await sheet.boundingBox()
        return box && [Math.round(box.x), Math.round(box.y), Math.round(box.width), Math.round(box.height)]
      }).toEqual([0, 0, 390, 844])
    })

    await test.step("the browser's Back closes the sheet and the Board is still shown", async () => {
      await page.goBack()
      await expect(page.getByRole('dialog', { name: 'Plan Q1 roadmap', exact: true })).toHaveCount(0)
      await expect(page).not.toHaveURL(/[?&]task=/)
      await expect(page).toHaveURL(new RegExp(`/l/${website.id}/board`))
      await expect(card(column(page, 'Review'), 'Plan Q1 roadmap')).toBeVisible()
    })

    await test.step('the Calendar is an agenda whose Today section holds "Write onboarding guide"', async () => {
      await views(page).getByRole('link', { name: 'Calendar' }).tap()
      await expect(page).toHaveURL(new RegExp(`/l/${website.id}/calendar`))
      await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
      await expect(page.getByRole('region', { name: 'Today', exact: true }).getByText('Write onboarding guide')).toBeVisible()
      await expect(page.getByRole('grid', { name: 'Calendar' })).toHaveCount(0)
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    })
  })
})

// ---- 29 ----

test.describe('Accessibility', () => {
  test.describe.configure({ mode: 'parallel' })

  test('29 Login', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Taskloom' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'Email' })).toBeVisible()
    await axeBothThemes(page)
  })

  test('29 Home', async ({ page, seeded }) => {
    expect(seeded.tasks['Write onboarding guide']).toBeDefined()
    await gotoHome(page)
    await axeBothThemes(page)
  })

  test('29 List', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    await axeBothThemes(page)
  })

  test('29 Board', async ({ page, seeded }) => {
    await gotoBoard(page, seeded)
    await axeBothThemes(page)
  })

  test('29 Calendar', async ({ page, seeded }) => {
    await gotoCalendar(page, seeded, 'Admin')
    await expect(page.getByRole('region', { name: 'To schedule', exact: true })).toBeVisible()
    await axeBothThemes(page)
  })

  test('29 the open task panel', async ({ page, seeded }) => {
    await gotoList(page, seeded, { task: 'Write onboarding guide' })
    await axeBothThemes(page)
  })

  test('29 Settings › Members', async ({ page, seeded }) => {
    expect(seeded.members['Ana Moreno']).toBeDefined()
    await page.goto('/settings/members')
    await expect(page.getByRole('table', { name: 'Members' })).toBeVisible()
    await axeBothThemes(page)
  })

  test('29 the open palette', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    await page.keyboard.press(`${MOD}+K`)
    const palette = page.getByRole('dialog', { name: 'Command palette' })
    await expect(palette.getByRole('combobox', { name: 'Search tasks or type a command' })).toBeFocused()
    await axeBothThemes(page)
    await palette.getByRole('combobox').fill('an')
    await expect(palette.getByRole('option', { name: /^Show all \d+ results$/ })).toBeVisible()
    await axeBothThemes(page)
  })

  test('29 the open quick create', async ({ page, seeded }) => {
    expect(seeded.lists.Admin).toBeDefined()
    await gotoHome(page)
    await page.keyboard.press('n')
    const dialog = page.getByRole('dialog', { name: 'New task' })
    await expect(dialog.getByRole('textbox', { name: 'Task title' })).toBeFocused()
    await axeBothThemes(page)
    // With a title, "Create task" is enabled, so its key hint is checked too.
    await dialog.getByRole('textbox', { name: 'Task title' }).fill('Book flights')
    await axeBothThemes(page)
  })
})

// ---- 30 ----

test.describe('Screenshots', () => {
  test.describe.configure({ mode: 'parallel' })

  test('30 1920×1080: the List with the panel docked', async ({ page, seeded }) => {
    await page.setViewportSize({ width: 1920, height: 1080 })
    await gotoList(page, seeded, { task: 'Write onboarding guide' })
    await expect(page.getByRole('complementary', { name: 'Write onboarding guide', exact: true })).toBeVisible()
    await shots(page, 'list-panel-docked')
  })

  test('30 1440×900: Home, List, Board, Calendar, the modal panel, Members, the palette and quick create', async ({
    page,
    seeded,
  }) => {
    test.setTimeout(120_000)
    await gotoHome(page)
    await shots(page, 'home')

    await gotoList(page, seeded)
    await shots(page, 'list')

    await gotoBoard(page, seeded)
    await shots(page, 'board')

    await gotoCalendar(page, seeded)
    await shots(page, 'calendar')

    await gotoList(page, seeded, { task: 'Write onboarding guide' })
    await expect(page.getByRole('dialog', { name: 'Write onboarding guide', exact: true })).toBeVisible()
    await shots(page, 'list-panel-modal')

    await page.goto('/settings/members')
    await expect(page.getByRole('table', { name: 'Members' })).toBeVisible()
    await shots(page, 'settings-members')

    await gotoHome(page)
    await page.keyboard.press(`${MOD}+K`)
    const palette = page.getByRole('dialog', { name: 'Command palette' })
    await expect(palette.getByRole('combobox', { name: 'Search tasks or type a command' })).toBeFocused()
    await shots(page, 'palette')
    await page.keyboard.press('Escape')
    await expect(palette).toBeHidden()

    await page.keyboard.press('n')
    const title = page.getByRole('dialog', { name: 'New task' }).getByRole('textbox', { name: 'Task title' })
    await expect(title).toBeFocused()
    await title.fill('Book flights')
    await shots(page, 'quick-create')
  })

  // In the phone project (390×844 with touch), so the shots show the touch sizes and the always-visible row menus.
  test('30 390×844: Home, List, the Calendar agenda and the task sheet @phone', async ({ page, seeded }) => {
    test.setTimeout(90_000)
    expect(page.viewportSize()).toEqual({ width: 390, height: 844 })
    await gotoHome(page)
    expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true)
    await shots(page, 'home')

    await gotoList(page, seeded)
    await shots(page, 'list')

    await gotoCalendar(page, seeded)
    await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
    await shots(page, 'calendar-agenda')

    await gotoList(page, seeded, { task: 'Write onboarding guide' })
    await expect(page.getByRole('dialog', { name: 'Write onboarding guide', exact: true }).getByRole('button', { name: 'Back' })).toBeVisible()
    await shots(page, 'task-sheet')
  })
})

// ---- 31 ----

test.describe('Resilience', () => {
  test('31 a failed save reverts with Retry, and Mod+Backspace in the panel deletes with Undo', async ({ page, seeded }) => {
    const plan = seeded.tasks['Plan Q1 roadmap']
    expect(plan.priority).toBe('Normal')
    await gotoList(page, seeded)

    await test.step('PATCH answers 500: the priority reverts and a danger toast offers Retry', async () => {
      await page.route('**/api/tasks/*', (route) =>
        route.request().method() === 'PATCH'
          ? route.fulfill({
              status: 500,
              contentType: 'application/problem+json',
              body: JSON.stringify({ title: 'An error occurred while processing your request.', status: 500 }),
            })
          : route.fallback(),
      )
      const failed = page.waitForResponse((r) => r.request().method() === 'PATCH' && r.url().endsWith(`/api/tasks/${plan.id}`))
      await row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Priority: Normal, change' }).click()
      await page.getByRole('menuitemradio', { name: /^High/ }).or(page.getByRole('menuitem', { name: /^High/ })).click()
      expect((await failed).status()).toBe(500)
      await expect(row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Priority: Normal, change' })).toBeVisible()
      const alert = notifications(page).getByRole('alert').filter({ hasText: 'Something went wrong' })
      await expect(alert).toBeVisible()
      await expect(alert.getByRole('button', { name: 'Retry' })).toBeVisible()
      expect((await api(page).task(plan.id)).task.priority).toBe('Normal')
    })

    await test.step('unrouted, Retry saves the change', async () => {
      await page.unroute('**/api/tasks/*')
      const saved = page.waitForResponse((r) => r.request().method() === 'PATCH' && r.url().endsWith(`/api/tasks/${plan.id}`))
      await notifications(page).getByRole('alert').getByRole('button', { name: 'Retry' }).click()
      expect((await saved).ok()).toBe(true)
      await expect(row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Priority: High, change' })).toBeVisible()
      expect((await api(page).task(plan.id)).task.priority).toBe('High')
      await page.reload()
      await expect(row(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Priority: High, change' })).toBeVisible()
    })

    await test.step('Mod+Backspace in the panel closes it and hides the row with Undo; Undo brings it back', async () => {
      const deletes: string[] = []
      page.on('request', (r) => {
        if (r.method() === 'DELETE' || /bulk-delete$/.test(r.url())) deletes.push(r.url())
      })
      await row(page, 'Plan Q1 roadmap').getByText('Plan Q1 roadmap', { exact: true }).click()
      await panelLoaded(page, 'Plan Q1 roadmap')
      await page.keyboard.press(`${MOD}+Backspace`)
      await expect(taskPanel(page, 'Plan Q1 roadmap')).toHaveCount(0)
      await expect(page).not.toHaveURL(/[?&]task=/)
      await expect(row(page, 'Plan Q1 roadmap')).toHaveCount(0)
      const undo = toast(page, 'Plan Q1 roadmap').getByRole('button', { name: 'Undo' })
      await expect(undo).toBeVisible()
      await undo.click()
      await expect(row(page, 'Plan Q1 roadmap')).toBeVisible()
      expect(deletes).toEqual([])
      expect((await api(page).task(plan.id)).task.title).toBe('Plan Q1 roadmap')
    })
  })
})

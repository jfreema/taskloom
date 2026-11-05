// Sign-in, theme and profile, and the month Calendar (spec 11.1 scenarios 1, 2 and 16), plus three checks of the
// same screens that 11.1 leaves out: the dev sign-in form's inline errors and returnUrl (8.14), the start date that
// moves with a dropped chip (8.9), and the agenda below 640px of page width (8.9). Every screen is checked with axe
// in both themes; saved values are checked after a reload or through api(page).
import {
  STAGE,
  api,
  day,
  dragTo,
  expect,
  expectNoAxeViolations,
  gotoMonth,
  stageC,
  test,
  uniqueEmail,
  type Day,
} from './fixtures'

type Page = Parameters<typeof dragTo>[0]
type Locator = Parameters<typeof dragTo>[1]

const escapeRegExp = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })
const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })
const calendar = (page: Page): Locator => page.getByRole('grid', { name: 'Calendar' })
const tray = (page: Page): Locator => page.getByRole('region', { name: 'To schedule', exact: true })
const noDueSection = (page: Page): Locator => tray(page).getByRole('region', { name: /^No due date \d+$/ })

/** A day's gridcell ("Wednesday, November 4, 3 tasks"). Look a day up only after gotoMonth() to its month. */
const cell = (page: Page, d: Day): Locator => calendar(page).getByRole('gridcell', { name: d.cell })

/** A day's chips, named "<title>, <status>, due <date>". */
const chipsOf = (page: Page, d: Day): Locator =>
  cell(page, d).getByRole('button', { name: new RegExp(`, due ${escapeRegExp(d.label)}$`) })

/** One task's chip on a day. */
const chip = (page: Page, d: Day, title: string): Locator =>
  cell(page, d).getByRole('button', { name: new RegExp(`^${escapeRegExp(title)}, .+, due ${escapeRegExp(d.label)}$`) })

/** The next answer to PATCH /api/tasks/{id} (every drop is one undoable update). */
const patched = (page: Page) =>
  page.waitForResponse((r) => r.request().method() === 'PATCH' && /^\/api\/tasks\/[^/]+$/.test(new URL(r.url()).pathname))

/** Drags `source` onto `target` and waits for the update that the drop saves. */
async function dropAndSave(page: Page, source: Locator, target: Locator): Promise<void> {
  const saved = patched(page)
  await dragTo(page, source, target)
  const res = await saved
  expect(res.ok(), `PATCH ${new URL(res.url()).pathname} answered ${res.status()}`).toBe(true)
}

/** Waits until no CSS transition runs (colours following the theme), so axe measures settled colours. */
async function settled(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document.getAnimations().every((a) => !(a instanceof CSSTransition) || a.playState !== 'running'),
  )
}

/** Dismisses every toast and waits until they are gone, so none fades out while axe measures contrast. */
async function clearToasts(page: Page): Promise<void> {
  // Retried as a whole: a toast can still arrive (a create's "Task created") while the others are dismissed.
  await expect(async () => {
    for (const dismiss of await notifications(page).getByRole('button', { name: 'Dismiss' }).all()) {
      await dismiss.click({ timeout: 1000 }).catch(() => undefined)
    }
    await expect(notifications(page).locator('[role="status"], [role="alert"]')).toHaveCount(0, { timeout: 1000 })
  }).toPass({ timeout: 10_000 })
}

/** axe in the light theme, then the dark one (the theme follows the system), then back to light. */
async function axeBothThemes(page: Page): Promise<void> {
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme })
    await expect
      .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme))
      .toBe(colorScheme)
    await settled(page)
    await expectNoAxeViolations(page)
  }
  await page.emulateMedia({ colorScheme: 'light' })
  await settled(page)
}

/** The dark tokens of spec 7.2 that scenario 2 reads back, as tokens.css writes them. */
const DARK_TOKENS: Record<string, string> = {
  '--bg-surface': '#151A1C',
  '--accent': '#3DBFAE',
  '--bg-column': '#101416',
  '--bg-card': '#20272A',
  '--bg-canvas': '#0E1112',
  '--text-primary': '#E6EBEC',
  '--border-default': '#323B3F',
}

const readTokens = (page: Page): Promise<Record<string, string>> =>
  page.evaluate((names) => {
    const style = getComputedStyle(document.documentElement)
    return Object.fromEntries(names.map((n) => [n, style.getPropertyValue(n).trim().toUpperCase()]))
  }, Object.keys(DARK_TOKENS))

const bodyBackground = (page: Page): Promise<string> =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor)

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

test.describe('Sign-in and settings', () => {
  test('1 sign-in', async ({ page }) => {
    await test.step('with no session, / goes to /login: "Sign in to Taskloom" and the Dev only badge', async () => {
      await page.goto('/')
      await expect(page).toHaveURL((url) => url.pathname === '/login')
      await expect(page.getByRole('heading', { level: 1, name: 'Sign in to Taskloom' })).toBeVisible()
      await expect(page.getByText('Dev only', { exact: true })).toBeVisible()
    })

    await test.step('?error= shows its message in an alert', async () => {
      await page.goto('/login?error=This%20account%20is%20not%20allowed.')
      await expect(page.getByRole('alert').filter({ hasText: 'This account is not allowed.' })).toBeVisible()
      // The message stays after the page drops ?error= from the address.
      await expect(page).toHaveURL((url) => url.pathname === '/login' && !url.searchParams.has('error'))
      await expect(page.getByRole('alert').filter({ hasText: 'This account is not allowed.' })).toBeVisible()
    })
    await axeBothThemes(page)

    await test.step('signing in with an email and "Jared Doe" opens Home with Personal and Tasks', async () => {
      await page.getByRole('textbox', { name: 'Email' }).fill(uniqueEmail('calendar-auth 1 sign-in'))
      await page.getByRole('textbox', { name: 'Display name' }).fill('Jared Doe')
      await page.getByRole('button', { name: 'Sign in', exact: true }).click()
      await expect(page).toHaveURL((url) => url.pathname === '/')
      await expect(sidebar(page).getByRole('button', { name: 'Personal', exact: true })).toBeVisible()
      await expect(sidebar(page).getByRole('link', { name: 'Tasks', exact: true })).toBeVisible()
      await expect(sidebar(page).getByRole('button', { name: 'Account: Jared Doe', exact: true })).toBeVisible()
    })
    await stageC('Home greets "Good …, Jared"', async () => {
      await expect(page.getByRole('heading', { level: 1, name: /^Good.*Jared$/ })).toBeVisible()
    })
  })

  test('dev sign-in: inline errors, returnUrl, and an unsafe returnUrl goes Home', async ({ page, baseURL }) => {
    const email = page.getByRole('textbox', { name: 'Email' })
    const signInButton = page.getByRole('button', { name: 'Sign in', exact: true })

    await test.step('an empty or malformed email is an inline error on the Email field', async () => {
      await page.goto('/login?returnUrl=%2Fsettings%2Fappearance')
      await signInButton.click()
      await expect(page.getByRole('alert').filter({ hasText: 'Enter your email address.' })).toBeVisible()
      await expect(email).toHaveAttribute('aria-invalid', 'true')
      await expect(email).toBeFocused()
      await email.fill('not-an-email')
      await expect(page.getByRole('alert')).toHaveCount(0)
      await signInButton.click()
      await expect(page.getByRole('alert').filter({ hasText: 'Enter a valid email address.' })).toBeVisible()
    })
    await axeBothThemes(page)

    const address = uniqueEmail('calendar-auth return url')
    await test.step('signing in goes to returnUrl', async () => {
      await email.fill(address)
      await signInButton.click()
      await expect(page).toHaveURL((url) => url.pathname === '/settings/appearance')
      await expect(page.getByRole('radiogroup', { name: 'Theme' })).toBeVisible()
    })

    await test.step('Sign out in Settings › Profile goes back to the sign-in page', async () => {
      await page.getByRole('navigation', { name: 'Settings' }).getByRole('link', { name: 'Profile', exact: true }).click()
      await page.getByRole('button', { name: 'Sign out', exact: true }).click()
      await expect(page).toHaveURL((url) => url.pathname === '/login')
      expect((await api(page).get('/api/me')).status()).toBe(401)
    })

    await test.step('a returnUrl to another site is ignored: Home opens', async () => {
      await page.goto('/login?returnUrl=%2F%2Fevil.example.com%2Fsteal')
      await email.fill(address)
      await signInButton.click()
      await expect(page).toHaveURL(new URL('/', baseURL).href)
      await expect(sidebar(page)).toBeVisible()
    })
  })

  test('2 theme and profile', async ({ page, seeded }) => {
    await page.goto('/')
    const account = sidebar(page).getByRole('button', { name: `Account: ${seeded.me.displayName}`, exact: true })
    const accountMenu = page.getByRole('menu', { name: 'Account' })

    await test.step('Account menu › Theme › Dark: html[data-theme="dark"] on the dark canvas', async () => {
      await account.click()
      await accountMenu.getByRole('menuitemradio', { name: 'Dark' }).click()
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
      await expect.poll(() => bodyBackground(page)).toBe('rgb(14, 17, 18)')
    })

    let explicitDark: Record<string, string> = {}
    await test.step('a reload is dark before <body> exists, so nothing flashes', async () => {
      await page.addInitScript(() => {
        const w = window as unknown as { themeAtBody?: string | null }
        const record = (): boolean => {
          if (!document.body) return false
          if (!('themeAtBody' in w)) w.themeAtBody = document.documentElement.getAttribute('data-theme')
          return true
        }
        if (!record()) {
          const observer = new MutationObserver(() => {
            if (record()) observer.disconnect()
          })
          observer.observe(document, { childList: true, subtree: true })
        }
      })
      await page.reload()
      await expect(account).toBeVisible()
      expect(await page.evaluate(() => (window as unknown as { themeAtBody?: string | null }).themeAtBody)).toBe('dark')
      explicitDark = await readTokens(page)
      expect(explicitDark).toEqual(DARK_TOKENS)
    })

    await test.step('System in Settings › Appearance removes the attribute', async () => {
      await account.click()
      await accountMenu.getByRole('menuitem', { name: 'Settings' }).click()
      await expect(page).toHaveURL((url) => url.pathname === '/settings/appearance')
      const theme = page.getByRole('radiogroup', { name: 'Theme' })
      await expect(theme.getByRole('radio', { name: 'Dark' })).toBeChecked()
      await theme.getByRole('radio', { name: 'System' }).check()
      await expect(theme.getByRole('radio', { name: 'System' })).toBeChecked()
      await expect.poll(() => page.evaluate(() => document.documentElement.hasAttribute('data-theme'))).toBe(false)
    })

    await test.step('a dark system gives the same tokens as the dark choice', async () => {
      await page.emulateMedia({ colorScheme: 'dark' })
      await expect.poll(() => readTokens(page)).toEqual(DARK_TOKENS)
      expect(await readTokens(page)).toEqual(explicitDark)
      await expect.poll(() => bodyBackground(page)).toBe('rgb(14, 17, 18)')
    })
    await axeBothThemes(page)

    await test.step('Settings › Profile: "Jared D" and Enter renames the account button', async () => {
      await page.getByRole('navigation', { name: 'Settings' }).getByRole('link', { name: 'Profile', exact: true }).click()
      const name = page.getByRole('textbox', { name: 'Display name' })
      await expect(name).toHaveValue(seeded.me.displayName)
      await name.fill('Jared D')
      const saved = page.waitForResponse((r) => r.request().method() === 'PATCH' && new URL(r.url()).pathname === '/api/me')
      await name.press('Enter')
      expect((await saved).ok()).toBe(true)
      await expect(sidebar(page).getByRole('button', { name: 'Account: Jared D', exact: true })).toBeVisible()
      expect((await api(page).me()).displayName).toBe('Jared D')
      await page.reload()
      await expect(sidebar(page).getByRole('button', { name: 'Account: Jared D', exact: true })).toBeVisible()
      await expect(name).toHaveValue('Jared D')
    })
    await axeBothThemes(page)
  })
})

test.describe('Calendar', () => {
  test('16 calendar (month)', async ({ page, seeded }) => {
    const admin = seeded.lists['Admin']
    const T = day(0)
    const T1 = day(1)
    // The drag target stays in T+1's month: T+3, or T−1 when T+3 falls in a later month.
    const target = day(3).month === T1.month ? day(3) : day(-1)

    await page.goto(`/l/${admin.id}/calendar`)
    await expect(calendar(page)).toBeVisible()

    await test.step('the To schedule tray is open: Overdue 2, and No due date 3 with Book team offsite first', async () => {
      await expect(page.getByRole('button', { name: 'To schedule, 5 tasks', exact: true })).toHaveAttribute(
        'aria-pressed',
        'true',
      )
      await expect(tray(page)).toBeVisible()
      const overdue = tray(page).getByRole('region', { name: 'Overdue 2', exact: true })
      await expect(overdue.getByRole('button')).toHaveCount(2)
      await expect(overdue.getByRole('button', { name: /^Send invoice to Contoso, overdue since / })).toBeVisible()
      await expect(overdue.getByRole('button', { name: /^Cancel unused subscriptions, overdue since / })).toBeVisible()
      const noDue = tray(page).getByRole('region', { name: 'No due date 3', exact: true })
      await expect(noDue.getByRole('button')).toHaveCount(3)
      await expect(noDue.getByRole('button').first()).toHaveAccessibleName(/^Book team offsite, .+, no due date$/)
    })
    await axeBothThemes(page)

    await test.step(`Renew domain names is on T+1; dropped on ${target.medium} it toasts and stays after a reload`, async () => {
      await gotoMonth(page, T1.iso)
      await expect(chip(page, T1, 'Renew domain names')).toBeVisible()
      await dropAndSave(page, chip(page, T1, 'Renew domain names'), cell(page, target))
      await expect(notifications(page).getByText(`Rescheduled to ${target.medium}`)).toBeVisible()
      await expect(chip(page, target, 'Renew domain names')).toBeVisible()
      await page.reload()
      await expect(calendar(page)).toBeVisible()
      await gotoMonth(page, target.iso)
      await expect(chip(page, target, 'Renew domain names')).toBeVisible()
      await expect(chip(page, T1, 'Renew domain names')).toHaveCount(0)
    })

    await test.step('Send invoice to Contoso from Overdue onto T leaves Overdue (Overdue 1) and shows on T', async () => {
      await gotoMonth(page, T.iso)
      await dropAndSave(
        page,
        tray(page).getByRole('button', { name: /^Send invoice to Contoso, overdue since / }),
        cell(page, T),
      )
      await expect(tray(page).getByRole('region', { name: 'Overdue 1', exact: true })).toBeVisible()
      await expect(chip(page, T, 'Send invoice to Contoso')).toBeVisible()
    })

    await test.step('Book team offsite from No due date onto T is scheduled: T has 3 chips', async () => {
      await dropAndSave(page, noDueSection(page).getByRole('button', { name: /^Book team offsite, / }), cell(page, T))
      await expect(chip(page, T, 'Book team offsite')).toBeVisible()
      await expect(chipsOf(page, T)).toHaveCount(3)
      await expect(tray(page).getByRole('region', { name: 'No due date 2', exact: true })).toBeVisible()
    })

    await stageC(`"Add task on ${T.short}" opens quick create, and "Order cake" is created on T`, async () => {
      await cell(page, T).hover()
      await cell(page, T).getByRole('button', { name: `Add task on ${T.short}`, exact: true }).click()
      const dialog = page.getByRole('dialog', { name: 'New task' })
      const title = dialog.getByRole('textbox', { name: 'Task title' })
      await expect(title).toBeFocused()
      await title.fill('Order cake')
      const created = page.waitForResponse(
        (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === `/api/lists/${admin.id}/tasks`,
      )
      await dialog.getByRole('button', { name: 'Create task', exact: true }).click()
      expect((await created).ok()).toBe(true)
      await expect(dialog).toBeHidden()
    })
    if (STAGE === 'B') {
      // Quick create is another engineer's screen: in stage B the same task comes from the API.
      await api(page).json('POST', `/api/lists/${admin.id}/tasks`, { title: 'Order cake', dueDate: T.iso })
      await page.reload()
      await expect(calendar(page)).toBeVisible()
    }
    const cake = (await api(page).listTasks(admin.id)).find((t) => t.title === 'Order cake')
    expect(cake?.dueDate).toBe(T.iso)

    await test.step('T shows 3 chips and "+1 more", which lists all 4 tasks', async () => {
      await clearToasts(page)
      await expect(chipsOf(page, T)).toHaveCount(3)
      await expect(cell(page, T)).toHaveAccessibleName(`${T.label}, 4 tasks`)
      const more = cell(page, T).getByRole('button', { name: '+1 more', exact: true })
      await more.click()
      const list = page.getByRole('dialog', { name: T.label, exact: true })
      await expect(list.getByRole('button')).toHaveCount(4)
      for (const title of ['Prepare board pack', 'Send invoice to Contoso', 'Book team offsite', 'Order cake']) {
        await expect(list.getByRole('button', { name: new RegExp(`^${title}, `) })).toBeVisible()
      }
      await axeBothThemes(page)
      await page.keyboard.press('Escape')
      await expect(list).toBeHidden()
      await expect(more).toBeFocused()
    })

    await test.step('a chip of T dropped on No due date loses its date: "+1 more" is gone, "No due date 3"', async () => {
      const first = chipsOf(page, T).first()
      const title = ((await first.getAttribute('aria-label')) ?? '').split(', ')[0]
      await dropAndSave(page, first, noDueSection(page))
      await expect(notifications(page).getByText('Removed due date')).toBeVisible()
      await expect(cell(page, T).getByRole('button', { name: /^\+\d+ more$/ })).toHaveCount(0)
      await expect(chipsOf(page, T)).toHaveCount(3)
      const noDue = tray(page).getByRole('region', { name: 'No due date 3', exact: true })
      await expect(noDue.getByRole('button', { name: new RegExp(`^${escapeRegExp(title)}, .+, no due date$`) })).toBeVisible()
      const moved = (await api(page).listTasks(admin.id)).find((t) => t.title === title)
      expect(moved?.dueDate).toBeNull()
    })

    await test.step('keyboard: ArrowRight then T returns to today; Enter goes to the first chip and Esc back', async () => {
      await cell(page, T).focus()
      await page.keyboard.press('ArrowRight')
      await expect(cell(page, T1)).toBeFocused()
      await page.keyboard.press('t')
      await expect(cell(page, T)).toBeFocused()
      await page.keyboard.press('Enter')
      await expect(chipsOf(page, T).first()).toBeFocused()
      await page.keyboard.press('ArrowDown')
      await expect(chipsOf(page, T).nth(1)).toBeFocused()
      await page.keyboard.press('Escape')
      await expect(cell(page, T)).toBeFocused()
    })
  })

  test('drops keep the start date in step and can be undone', async ({ page, seeded }) => {
    const admin = seeded.lists['Admin']
    const board = seeded.tasks['Prepare board pack']
    const offsite = seeded.tasks['Book team offsite']
    const T = day(0)
    // Two days later, or two days earlier when that leaves T's month.
    const shift = day(2).month === T.month ? 2 : -2
    const to = day(shift)
    await api(page).patchTask(board.id, { startDate: day(-2).iso })
    await api(page).patchTask(offsite.id, { startDate: day(5).iso })
    await page.goto(`/l/${admin.id}/calendar`)
    await expect(calendar(page)).toBeVisible()

    await test.step('day to day: the start date moves by the same number of days', async () => {
      await dropAndSave(page, chip(page, T, 'Prepare board pack'), cell(page, to))
      await expect(notifications(page).getByText(`Rescheduled to ${to.medium}`)).toBeVisible()
      const { task: saved } = await api(page).task(board.id)
      expect([saved.dueDate, saved.startDate]).toEqual([to.iso, day(shift - 2).iso])
    })

    await test.step('No due date to a day: a start later than that day comes back to it', async () => {
      await dropAndSave(page, noDueSection(page).getByRole('button', { name: /^Book team offsite, / }), cell(page, T))
      const { task: saved } = await api(page).task(offsite.id)
      expect([saved.dueDate, saved.startDate]).toEqual([T.iso, T.iso])
    })

    await test.step('onto No due date: the due date goes and the start stays; Undo brings the date back', async () => {
      await dropAndSave(page, chip(page, T, 'Book team offsite'), noDueSection(page))
      const { task: cleared } = await api(page).task(offsite.id)
      expect([cleared.dueDate, cleared.startDate]).toEqual([null, T.iso])
      const toast = notifications(page).getByRole('status').filter({ hasText: 'Removed due date' })
      const undone = patched(page)
      await toast.getByRole('button', { name: 'Undo', exact: true }).click()
      expect((await undone).ok()).toBe(true)
      await expect(chip(page, T, 'Book team offsite')).toBeVisible()
      expect((await api(page).task(offsite.id)).task.dueDate).toBe(T.iso)
    })
  })

  test('the agenda below 640px of page width', async ({ page, seeded }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.goto(`/l/${seeded.lists['Website refresh'].id}/calendar`)
    await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
    await expect(page.getByRole('region', { name: 'Today', exact: true }).getByText('Write onboarding guide')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Overdue', exact: true })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'No due date', exact: true })).toBeVisible()
    if (day(1).month === day(0).month) {
      await expect(page.getByRole('heading', { name: day(1).medium, exact: true })).toBeVisible()
    }
    await expect(calendar(page)).toHaveCount(0)
    await expect(page.getByRole('button', { name: /^To schedule, / })).toHaveCount(0)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    await axeBothThemes(page)

    await test.step('another month has no Overdue section', async () => {
      await page.getByRole('button', { name: 'Next month', exact: true }).click()
      await expect(page.getByRole('heading', { name: day(0).month, exact: true })).toHaveCount(0)
      await expect(page.getByRole('heading', { name: 'Overdue', exact: true })).toHaveCount(0)
      await page.getByRole('button', { name: 'Today', exact: true }).click()
      await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
    })
  })
})

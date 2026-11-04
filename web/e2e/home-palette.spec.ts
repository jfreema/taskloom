// Home, quick create, the command palette and the shortcuts dialog (spec 11.1 scenarios 3, 21, 22 and 23), plus
// Home's first run and its phone layout. Each screen is checked with axe in both themes.
import type { Locator, Page } from '@playwright/test'
import { MOD, api, day, expect, expectNoAxeViolations, signIn, stageC, test, uniqueEmail } from './fixtures'

/** Home has loaded: the greeting and the sections. */
async function gotoHome(page: Page): Promise<void> {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: /^Good (morning|afternoon|evening)/ })).toBeVisible()
  await expect(page.getByRole('heading', { level: 2, name: 'Today' })).toBeVisible()
}

/** A Home section's rows: the list named by its heading. */
const section = (page: Page, name: string): Locator => page.getByRole('list', { name, exact: true })

/** A Home row by its task title (the row's name starts with the title, then the parent and the due date). */
const row = (scope: Page | Locator, title: string): Locator =>
  scope.getByRole('listitem', { name: new RegExp(`^${title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(,|$)`) })

/** The task panel: an aside named by the task title when docked, otherwise a dialog (8.10). */
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))

const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** The toast holding `text` (a status, or an alert for errors). */
const toast = (page: Page, text: string): Locator =>
  notifications(page).locator('[role="status"], [role="alert"]').filter({ hasText: text })

/** The count a Home section shows, read from its markup. */
async function sectionCount(page: Page, key: string): Promise<number> {
  return Number(await page.locator(`[data-section="${key}"]`).getAttribute('data-count'))
}

/** axe in the light theme, then the dark one (the theme follows the system by default), then back to light. */
async function axeBothThemes(page: Page): Promise<void> {
  await page.emulateMedia({ colorScheme: 'light' })
  await expectNoAxeViolations(page)
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')
  await expectNoAxeViolations(page)
  await page.emulateMedia({ colorScheme: 'light' })
}

test.describe('Home', () => {
  test('21 sections, completing with undo, rescheduling, Move all to today, the scope and quick add', async ({
    page,
    seeded,
  }) => {
    await gotoHome(page)
    const overdue = section(page, 'Overdue')
    const today = section(page, 'Today')

    await expect(overdue.getByRole('listitem')).toHaveCount(7)
    await expect(row(overdue, 'Fix sign-in redirect')).toBeVisible()
    await expect(row(overdue, 'Send invoice to Contoso')).toBeVisible()
    await expect(row(today, 'Write onboarding guide')).toBeVisible()
    await expect(row(today, 'Call the printer company')).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: 'Completed in the last 7 days' })).toBeVisible()
    await axeBothThemes(page)

    await test.step('complete with the circle: the row holds for about 1s, then moves to Completed', async () => {
      const completedBefore = await sectionCount(page, 'completed')
      const circle = page.getByRole('checkbox', { name: 'Complete “Fix sign-in redirect”' })
      await circle.click()
      const clickedAt = Date.now()
      await expect(row(overdue, 'Fix sign-in redirect')).toBeVisible()
      await expect(circle).toBeChecked()
      await expect(row(overdue, 'Fix sign-in redirect')).toHaveCount(0)
      expect(Date.now() - clickedAt).toBeGreaterThan(700)
      await expect(overdue.getByRole('listitem')).toHaveCount(6)
      await expect.poll(() => sectionCount(page, 'completed')).toBe(completedBefore + 1)
      await toast(page, 'Completed “Fix sign-in redirect”').getByRole('button', { name: 'Undo' }).click()
      await expect(row(overdue, 'Fix sign-in redirect')).toBeVisible()
      await expect(overdue.getByRole('listitem')).toHaveCount(7)
      await expect.poll(() => sectionCount(page, 'completed')).toBe(completedBefore)
    })

    await test.step('reschedule to tomorrow: the row moves to Next 7 days', async () => {
      await row(overdue, 'Send invoice to Contoso').hover()
      await page.getByRole('button', { name: 'Reschedule Send invoice to Contoso' }).click()
      await page.getByRole('group', { name: 'Quick picks' }).getByRole('button', { name: /^Tomorrow/ }).click()
      await expect(row(section(page, 'Next 7 days'), 'Send invoice to Contoso')).toBeVisible()
      await expect(row(overdue, 'Send invoice to Contoso')).toHaveCount(0)
      await expect(toast(page, `Rescheduled to ${day(1).medium}`)).toBeVisible()
      await expect
        .poll(async () => (await api(page).task(seeded.tasks['Send invoice to Contoso'].id)).task.dueDate)
        .toBe(day(1).iso)
    })

    await test.step('Move all to today: one bulk change, one toast, and Undo brings Overdue back', async () => {
      await page.getByRole('heading', { level: 2, name: 'Overdue' }).hover()
      await page.getByRole('button', { name: 'Move all to today' }).click()
      await expect(toast(page, 'Rescheduled 6 tasks to today')).toHaveCount(1)
      await expect(page.getByRole('heading', { level: 2, name: 'Overdue' })).toHaveCount(0)
      await expect(today.getByRole('listitem')).toHaveCount(8)
      await expect(row(today, 'Fix sign-in redirect')).toBeVisible()
      await expect
        .poll(async () => (await api(page).task(seeded.tasks['Fix sign-in redirect'].id)).task.dueDate)
        .toBe(day(0).iso)
      await toast(page, 'Rescheduled 6 tasks to today').getByRole('button', { name: 'Undo' }).click()
      await expect(overdue.getByRole('listitem')).toHaveCount(6)
      await expect(today.getByRole('listitem')).toHaveCount(2)
      await expect
        .poll(async () => (await api(page).task(seeded.tasks['Fix sign-in redirect'].id)).task.dueDate)
        .toBe(day(-1).iso)
    })

    await test.step('"Assigned to me" hides what only I created', async () => {
      await page.getByRole('radiogroup', { name: 'Show' }).getByRole('radio', { name: 'Assigned to me' }).click()
      await expect(row(page, 'Call the printer company')).toHaveCount(0)
      await expect(row(today, 'Write onboarding guide')).toBeVisible()
    })

    await test.step('quick add "Pay rent": it lands in Today, assigned to me, and stays after a reload', async () => {
      const add = page.getByRole('textbox', { name: 'Add a task' })
      await add.fill('Pay rent')
      await add.press('Enter')
      await expect(row(today, 'Pay rent')).toBeVisible()
      await expect(add).toBeFocused()
      await expect(add).toHaveValue('')
      await page.reload()
      await expect(row(section(page, 'Today'), 'Pay rent')).toBeVisible()
      await expect(page.getByRole('radio', { name: 'Assigned to me' })).toBeChecked()
      const lists = Object.values(seeded.lists)
      const all = (await Promise.all(lists.map((l) => api(page).listTasks(l.id)))).flat()
      expect(all.find((t) => t.title === 'Pay rent')).toMatchObject({ dueDate: day(0).iso, assigneeIds: [seeded.me.id] })
    })
  })

  test('first run: the hint, the empty state and its two buttons', async ({ page }) => {
    await signIn(page, { email: uniqueEmail('home-first-run') })
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 1, name: /^Good/ })).toBeVisible()
    await expect(page.getByText('Your work shows up here')).toBeVisible()
    await expect(page.getByText('Enter to add ·')).toBeVisible()
    await expect(page.getByRole('radiogroup', { name: 'Show' })).toHaveCount(0)
    await expect(page.getByText('Nothing due today', { exact: true })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Keyboard shortcuts' })).toBeVisible()
    await expect(page.getByRole('link', { name: /^Open / })).toBeVisible()
    await axeBothThemes(page)
  })

  test('phone: two-line rows, the scope under the greeting, and the New task button @phone', async ({ page, seeded }) => {
    await gotoHome(page)
    const first = section(page, 'Overdue').getByRole('listitem').first()
    expect((await first.boundingBox())?.height).toBeGreaterThanOrEqual(55)
    const greeting = await page.getByRole('heading', { level: 1 }).boundingBox()
    const scope = await page.getByRole('radiogroup', { name: 'Show' }).boundingBox()
    expect(scope!.y).toBeGreaterThan(greeting!.y + greeting!.height)
    await axeBothThemes(page)
    await page.getByRole('button', { name: 'New task' }).click()
    await expect(page.getByRole('dialog', { name: 'New task' })).toBeVisible()
    await expect(page.getByRole('button', { name: `List: ${seeded.lists.Tasks.name}, change` })).toBeVisible()
  })
})

test.describe('Quick create', () => {
  test('3 N, defaults, Create more, create and open, and paste to create many', async ({ page, seeded }) => {
    await gotoHome(page)
    const dialog = page.getByRole('dialog', { name: 'New task' })
    const title = dialog.getByRole('textbox', { name: 'Task title' })

    await page.keyboard.press('n')
    await expect(dialog).toBeVisible()
    await expect(title).toBeFocused()
    await expect(dialog.getByRole('textbox', { name: 'Description' })).toBeVisible()
    await expect(dialog.getByRole('button', { name: /^Status: To do, change$/ })).toBeVisible()

    await title.fill('Book flights')
    // With a title, "Create task" is enabled, so axe checks its key hint too.
    await axeBothThemes(page)
    await dialog.getByRole('button', { name: /^List: .+, change$/ }).click()
    await page.getByRole('option', { name: /Admin$/ }).click()
    await expect(dialog.getByRole('button', { name: 'List: Admin, change' })).toBeVisible()
    await dialog.getByRole('button', { name: 'Due date: none, add' }).click()
    await page.getByRole('group', { name: 'Quick picks' }).getByRole('button', { name: /^Tomorrow/ }).click()
    await dialog.getByRole('button', { name: 'Priority: none, add' }).click()
    await page.getByRole('menuitem', { name: /^High/ }).click()
    await expect(dialog.getByRole('button', { name: 'Priority: High, change' })).toBeVisible()
    await title.press('Enter')

    await expect(dialog).toBeHidden()
    const created = toast(page, 'Task created')
    await expect(created).toBeVisible()
    await expect(created.getByRole('button', { name: 'Open' })).toBeVisible()
    await expect
      .poll(async () => (await api(page).listTasks(seeded.lists.Admin.id)).find((t) => t.title === 'Book flights'))
      .toMatchObject({ dueDate: day(1).iso, priority: 'High', statusId: seeded.lists.Admin.statusIds['To do'] })

    await test.step('Create more keeps the dialog open with an empty title', async () => {
      await page.keyboard.press('n')
      await expect(title).toBeFocused()
      await expect(dialog.getByRole('button', { name: 'List: Admin, change' })).toBeVisible()
      await dialog.getByRole('switch', { name: 'Create more' }).click()
      await title.fill('A')
      await title.press('Enter')
      await expect(title).toHaveValue('')
      await expect(title).toBeFocused()
      await title.fill('B')
      await title.press('Enter')
      await expect(dialog).toBeVisible()
      await expect(title).toHaveValue('')
      await expect
        .poll(async () => (await api(page).listTasks(seeded.lists.Admin.id)).filter((t) => ['A', 'B'].includes(t.title)).length)
        .toBe(2)
    })

    await test.step('Mod+Enter creates and opens the task in the panel', async () => {
      await title.fill('C')
      await title.press(`${MOD}+Enter`)
      await expect(dialog).toBeHidden()
      await expect(page).toHaveURL(/[?&]task=[0-9a-f-]{36}/)
      await expect(taskPanel(page, 'C')).toBeVisible()
    })

    await test.step('pasting two lines asks first, then creates both', async () => {
      await page.keyboard.press('Escape')
      await expect(page).not.toHaveURL(/[?&]task=/)
      await page.keyboard.press('n')
      await expect(title).toBeFocused()
      await page.evaluate(() => navigator.clipboard.writeText('x\ny'))
      await title.press(`${MOD}+V`)
      const confirm = page.getByRole('alertdialog', { name: 'Create 2 tasks?' })
      await expect(confirm).toBeVisible()
      await expect(confirm).toContainText('“x” and “y”')
      await confirm.getByRole('button', { name: 'Create 2 tasks' }).click()
      await expect(toast(page, 'Created 2 tasks')).toBeVisible()
      await expect
        .poll(async () =>
          (await api(page).listTasks(seeded.lists.Admin.id)).filter((t) => t.title === 'x' || t.title === 'y').length,
        )
        .toBe(2)
    })
  })
})

test.describe('Command palette', () => {
  test('22 recents and actions, server search, Show all, lists, and Create task', async ({ page, seeded }) => {
    await gotoHome(page)
    const palette = page.getByRole('dialog', { name: 'Command palette' })
    const input = palette.getByRole('combobox', { name: 'Search tasks or type a command' })

    await page.keyboard.press(`${MOD}+K`)
    await expect(input).toBeFocused()
    await expect(input).toHaveAttribute('aria-expanded', 'true')
    await expect(palette.getByText('Recent', { exact: true })).toBeVisible()
    const actions = palette.getByRole('group', { name: 'Actions' })
    await expect(actions.getByRole('option', { name: /^New task/ })).toBeVisible()
    await expect(actions.getByRole('option', { name: /^Theme: Dark/ })).toBeVisible()
    await axeBothThemes(page)

    await test.step('a task found on the server opens in the panel', async () => {
      await input.fill('invoice')
      const option = palette.getByRole('option', { name: /^Send invoice to Contoso/ })
      await expect(option).toBeVisible()
      await expect(option).toHaveAttribute('aria-selected', 'true')
      await expect(input).toHaveAttribute('aria-activedescendant', (await option.getAttribute('id'))!)
      await expectNoAxeViolations(page)
      await input.press('Enter')
      await expect(palette).toBeHidden()
      await expect(page).toHaveURL(new RegExp(`[?&]task=${seeded.tasks['Send invoice to Contoso'].id}`))
      await expect(taskPanel(page, 'Send invoice to Contoso')).toBeVisible()
    })

    await test.step('"Show all N results" expands the tasks in place', async () => {
      // Over the open panel (modal at 1440×900): Mod+K still opens the palette, and Esc closes only the palette.
      await page.keyboard.press(`${MOD}+K`)
      await expect(input).toBeFocused()
      await expect(input).toHaveValue('')
      await input.fill('an')
      const tasks = palette.getByRole('group', { name: 'Tasks' })
      const showAll = tasks.getByRole('option', { name: 'Show all 12 results' })
      await expect(showAll).toBeVisible()
      await expect(tasks.getByRole('option')).toHaveCount(9)
      await showAll.click()
      await expect(showAll).toHaveCount(0)
      await expect(tasks.getByRole('option')).toHaveCount(12)
      await expect(palette).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(palette).toBeHidden()
      await expect(taskPanel(page, 'Send invoice to Contoso')).toBeVisible()
    })

    await test.step('a list goes to its page', async () => {
      await page.keyboard.press(`${MOD}+K`)
      await input.fill('admin')
      await palette.getByRole('group', { name: 'Lists' }).getByRole('option', { name: /^Admin/ }).click()
      await expect(palette).toBeHidden()
      await expect(page).toHaveURL(new RegExp(`/l/${seeded.lists.Admin.id}(/|$)`))
    })

    await test.step('"Create task “zebra”" opens quick create with that title', async () => {
      await page.keyboard.press(`${MOD}+K`)
      await input.fill('zebra')
      await expect(palette.getByText('No results for “zebra”.')).toBeVisible()
      await palette.getByRole('option', { name: 'Create task “zebra”' }).click()
      const quick = page.getByRole('dialog', { name: 'New task' })
      await expect(quick).toBeVisible()
      await expect(quick.getByRole('textbox', { name: 'Task title' })).toHaveValue('zebra')
      await expect(quick.getByRole('button', { name: 'List: Admin, change' })).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(quick).toBeHidden()
    })

    await stageC('"Search all lists for “passport”" opens the palette with that query', async () => {
      await page.goto(`/l/${seeded.lists['Website refresh'].id}/list`)
      await page.getByRole('searchbox', { name: 'Search this list' }).fill('passport')
      await page.getByRole('button', { name: 'Search all lists for “passport”' }).click()
      await expect(input).toHaveValue('passport')
      await expect(palette.getByRole('option', { name: /^Renew passport/ })).toBeVisible()
    })
  })
})

test.describe('Shortcuts', () => {
  test('23 the shortcuts dialog, typing in a search box, and single-key shortcuts turned off', async ({ page, seeded }) => {
    await gotoHome(page)
    await page.keyboard.press('?')
    const dialog = page.getByRole('dialog', { name: 'Keyboard shortcuts' })
    await expect(dialog).toBeVisible()
    for (const label of ['Open the command palette', 'Go to Home', 'Delete, with undo', 'Open the row or card menu', 'Go to today']) {
      await expect(dialog.getByText(label, { exact: true })).toBeVisible()
    }
    await axeBothThemes(page)
    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()

    await test.step('n typed in the list search box stays in the box', async () => {
      await page.goto(`/l/${seeded.lists['Website refresh'].id}/list`)
      const search = page.getByRole('searchbox', { name: 'Search this list' })
      await search.click()
      await page.keyboard.type('n')
      await expect(search).toHaveValue('n')
      await expect(page.getByRole('dialog', { name: 'New task' })).toHaveCount(0)
    })

    await stageC('with single-key shortcuts off, N does nothing and Mod+K still works', async () => {
      await page.goto('/settings/appearance')
      const toggle = page.getByRole('switch', { name: 'Single-key shortcuts' })
      await toggle.click()
      await expect(toggle).toHaveAttribute('aria-checked', 'false')
      await gotoHome(page)
      await page.keyboard.press('n')
      await expect(page.getByRole('dialog', { name: 'New task' })).toHaveCount(0)
      await page.keyboard.press(`${MOD}+K`)
      await expect(page.getByRole('dialog', { name: 'Command palette' })).toBeVisible()
    })
  })
})

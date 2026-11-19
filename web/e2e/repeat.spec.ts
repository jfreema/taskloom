// Repeating tasks (Features 1): the Repeat row of the task panel and its presets, the repeat icon on the List's due
// date, completing a repeating task (the next one appears and the toast names its date; Undo takes it back), a
// custom rule from the dialog (checked with axe in both themes), and "Don't repeat" in the activity feed.
import { api, day, dragTo, expect, expectNoAxeViolations, test } from './fixtures'

// Playwright's Page and Locator types, read off the fixtures' own signatures (scenario files import fixtures only).
type Page = Parameters<typeof api>[0]
type Locator = Parameters<typeof dragTo>[1]

const LIST = 'Website refresh'
const TITLE = 'Plan Q1 roadmap'

/** The task panel: an aside named by the task title when docked, otherwise a dialog (8.10). */
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))

const listRows = (page: Page, title: string): Locator =>
  page.getByRole('treegrid', { name: `${LIST} tasks` }).getByRole('row', { name: title, exact: true })

const toast = (page: Page, text: string | RegExp): Locator =>
  page.getByRole('region', { name: 'Notifications' }).locator('[role="status"], [role="alert"]').filter({ hasText: text })

async function axeBothThemes(page: Page, include: string): Promise<void> {
  await page.emulateMedia({ colorScheme: 'light' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('light')
  await expectNoAxeViolations(page, { include })
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')
  await expectNoAxeViolations(page, { include })
  await page.emulateMedia({ colorScheme: 'light' })
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

test.describe('Repeating tasks', () => {
  test('repeat presets, completing makes the next one, Undo takes it back, a custom rule, and stopping', async ({
    page,
    seeded,
  }) => {
    const list = seeded.lists[LIST]
    const task = seeded.tasks[TITLE]
    const due = day(3)
    expect(task.dueDate).toBe(due.iso)
    const weekday = new Intl.DateTimeFormat('en-US', { weekday: 'long', timeZone: 'UTC' }).format(new Date(`${due.iso}T00:00:00Z`))
    const shortDay = weekday.slice(0, 3)

    await page.goto(`/l/${list.id}/list?task=${task.id}`)
    const panel = taskPanel(page, TITLE)
    await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()

    await test.step('the Repeat menu offers rules worded for the due date', async () => {
      await panel.getByRole('button', { name: 'Repeat: none, add' }).click()
      const menu = page.getByRole('menu', { name: 'Repeat' })
      for (const name of ['Daily', 'Every weekday (Mon–Fri)', `Weekly on ${weekday}`, 'Custom…']) {
        await expect(menu.getByRole(name === 'Custom…' ? 'menuitem' : 'menuitemradio', { name })).toBeVisible()
      }
      await expect(menu.getByRole('menuitem', { name: "Don't repeat" })).toHaveCount(0)
      await menu.getByRole('menuitemradio', { name: `Weekly on ${weekday}` }).click()
      await expect(panel.getByRole('button', { name: `Repeat: Weekly on ${shortDay}, change` })).toBeVisible()
      await expect
        .poll(async () => (await api(page).task(task.id)).task.repeat)
        .toMatchObject({ unit: 'Week', every: 1, basis: 'Schedule', weekdays: [weekday] })
      // The List's due date says the task repeats.
      await expect(listRows(page, TITLE).getByRole('button', { name: /^Due date: .+, repeats, change$/ })).toBeVisible()
    })

    await test.step('completing it makes the next one, a week on; Undo takes it back', async () => {
      await panel.getByRole('button', { name: 'Mark complete' }).click()
      const done = toast(page, /^Completed “Plan Q1 roadmap” · Next one due /)
      await expect(done).toBeVisible()
      const tasks = async () => (await api(page).listTasks(list.id)).filter((t) => t.title === TITLE)
      await expect.poll(async () => (await tasks()).length).toBe(2)
      const [old, next] = (await tasks()).sort((a, b) => (a.id === task.id ? -1 : b.id === task.id ? 1 : 0))
      expect(old.completedAt).not.toBeNull()
      expect(old.repeat).toBeNull()
      expect(old.nextRepeatId).toBe(next.id)
      expect(next.dueDate).toBe(day(10).iso)
      expect(next.repeat).toMatchObject({ unit: 'Week', weekdays: [weekday] })
      await expect(listRows(page, TITLE).getByRole('button', { name: /, repeats, change$/ })).toHaveCount(1)

      await done.getByRole('button', { name: 'Undo' }).click()
      await expect.poll(async () => (await tasks()).length).toBe(1)
      const back = (await api(page).task(task.id)).task
      expect(back.completedAt).toBeNull()
      expect(back.nextRepeatId).toBeNull()
      expect(back.repeat).toMatchObject({ unit: 'Week', weekdays: [weekday] })
      await expect(panel.getByRole('button', { name: 'Mark complete' })).toBeVisible()
    })

    await test.step('a custom rule: every 2 weeks on Monday and the due weekday', async () => {
      await panel.getByRole('button', { name: /^Repeat: / }).click()
      await page.getByRole('menu', { name: 'Repeat' }).getByRole('menuitem', { name: 'Custom…' }).click()
      const dialog = page.getByRole('dialog', { name: 'Custom repeat' })
      await expect(dialog).toBeVisible()
      await expect(dialog.getByRole('button', { name: weekday, exact: true })).toHaveAttribute('aria-pressed', 'true')
      await dialog.getByRole('textbox', { name: 'Every' }).fill('0')
      await dialog.getByRole('button', { name: 'Save' }).click()
      await expect(dialog.getByText('Enter a number from 1 to 365.')).toBeVisible()
      await dialog.getByRole('textbox', { name: 'Every' }).fill('2')
      if (weekday !== 'Monday') await dialog.getByRole('button', { name: 'Monday', exact: true }).click()
      const days = weekday === 'Monday' ? 'Mon' : ['Mon', shortDay].sort((a, b) => order(a) - order(b)).join(', ')
      await expect(dialog.getByText(`Every 2 weeks on ${days}`)).toBeVisible()
      await axeBothThemes(page, '#overlay-root')
      await dialog.getByRole('button', { name: 'Save' }).click()
      await expect(dialog).toBeHidden()
      await expect(panel.getByRole('button', { name: `Repeat: Every 2 weeks on ${days}, change` })).toBeVisible()
      await expect.poll(async () => (await api(page).task(task.id)).task.repeat).toMatchObject({ unit: 'Week', every: 2 })
    })

    await test.step("Don't repeat stops it, and the activity says so", async () => {
      await panel.getByRole('button', { name: /^Repeat: / }).click()
      await page.getByRole('menu', { name: 'Repeat' }).getByRole('menuitem', { name: "Don't repeat" }).click()
      await expect(panel.getByRole('button', { name: 'Repeat: none, add' })).toBeVisible()
      await expect.poll(async () => (await api(page).task(task.id)).task.repeat).toBeNull()
      // Undo's status change, the custom rule and this stop came in a row, so the feed folds them into one line.
      await expect(panel.getByText('updated the status and repeat')).toBeVisible()
      await panel.getByRole('button', { name: 'Show 3 changes' }).click()
      await expect(panel.getByText('stopped repeating this')).toBeVisible()
      await expect(panel.getByText(`set this to repeat weekly on ${shortDay}`)).toBeVisible()
    })
  })
})

/** Monday-first order of a short day name (the rule reads its days Sunday-first, as WEEKDAYS lists them). */
function order(short: string): number {
  return ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].indexOf(short)
}

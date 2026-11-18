// Type-to-fill quick add (Look 1) in the app: Home's quick add, the List's add row, the New task dialog and the date
// picker's typed-date box. The reading rules themselves are checked in quick-add-parse.spec.ts.
import type { Locator, Page } from '@playwright/test'
import { api, day, expect, expectNoAxeViolations, test, type Seeded, type Task } from './fixtures'

/** Every task in the seeded workspace, as the server has it. */
async function allTasks(page: Page, seeded: Seeded): Promise<Task[]> {
  return (await Promise.all(Object.values(seeded.lists).map((l) => api(page).listTasks(l.id)))).flat()
}

/** The task the server has with this title, once it has one. */
async function serverTask(page: Page, seeded: Seeded, title: string): Promise<Task> {
  let found: Task | undefined
  await expect
    .poll(async () => {
      found = (await allTasks(page, seeded)).find((t) => t.title === title)
      return !!found
    })
    .toBe(true)
  return found!
}

/** The coming Friday after today (UTC, as the browser runs in UTC), never today. */
function comingFriday(): string {
  const dow = new Date(`${day(0).iso}T00:00:00Z`).getUTCDay()
  return day((5 - dow + 7) % 7 || 7).iso
}

/** The chips that list what quick add recognised. */
const chip = (scope: Locator | Page, text: string): Locator =>
  scope.locator('.quick-add-chips .chip').filter({ hasText: text })

test.describe('Type-to-fill quick add', () => {
  test('Home: the parts fill the fields, show as chips, and one can be kept as text', async ({ page, seeded }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 2, name: 'Today' })).toBeVisible()
    const add = page.getByRole('textbox', { name: 'Add a task' })

    await add.fill('Pay rent tomorrow !high #ops @ana')
    await expect(chip(page, 'Tomorrow')).toBeVisible()
    await expect(chip(page, 'High')).toBeVisible()
    await expect(chip(page, 'ops')).toBeVisible()
    await expect(chip(page, 'Ana Moreno')).toBeVisible()
    await expect(page.getByText('due tomorrow', { exact: true })).toBeVisible()
    // The recognised parts are tinted in the input.
    await expect(page.locator('.ui-text-marks mark')).toHaveText(['tomorrow', '!high', '#ops', '@ana'])

    await test.step('axe with the chips showing, both themes', async () => {
      await page.emulateMedia({ colorScheme: 'light' })
      await expectNoAxeViolations(page)
      await page.emulateMedia({ colorScheme: 'dark' })
      await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')
      await expectNoAxeViolations(page)
      await page.emulateMedia({ colorScheme: 'light' })
    })

    await test.step('× on High keeps "!high" in the title and leaves focus in the input', async () => {
      await page.getByRole('button', { name: 'Keep “!high” as text' }).click()
      await expect(chip(page, 'High')).toHaveCount(0)
      await expect(page.locator('.ui-text-marks mark')).toHaveText(['tomorrow', '#ops', '@ana'])
      await expect(add).toBeFocused()
      // Typing on keeps it dismissed.
      await add.press('End')
      await add.pressSequentially(' now')
      await expect(chip(page, 'High')).toHaveCount(0)
    })

    await test.step('Enter creates the task with the typed fields and the rest as its title', async () => {
      await add.press('Enter')
      await expect(add).toHaveValue('')
      await expect(page.locator('.quick-add-chips')).toHaveCount(0)
      const t = await serverTask(page, seeded, 'Pay rent !high now')
      expect(t).toMatchObject({ dueDate: day(1).iso, priority: 'None', tagIds: [seeded.tags.ops.id] })
      expect(t.assigneeIds).toContain(seeded.members['Ana Moreno'].userId)
    })

    await test.step('a new tag is created with the task', async () => {
      await add.fill('Order toner #supplies')
      await expect(chip(page, 'supplies')).toContainText('new')
      await add.press('Enter')
      const t = await serverTask(page, seeded, 'Order toner')
      const tags = await api(page).tags(seeded.workspaceId)
      const supplies = tags.find((x) => x.name === 'supplies')
      expect(supplies).toBeTruthy()
      expect(t.tagIds).toEqual([supplies!.id])
      expect(t.dueDate).toBe(day(0).iso)
    })
  })

  test('List: the add row reads the title over the group defaults', async ({ page, seeded }) => {
    await page.goto(`/l/${seeded.lists['Website refresh'].id}/list`)
    const grid = page.getByRole('treegrid', { name: 'Website refresh tasks' })
    await expect(grid).not.toHaveAttribute('aria-busy', 'true')
    const todo = grid.getByRole('rowgroup', { name: 'To do', exact: true })
    await todo.getByRole('button', { name: 'Add task', exact: true }).click()
    const input = todo.getByRole('textbox', { name: 'Task name' })
    await input.fill('Draft agenda by fri !urgent #design')
    await expect(chip(todo, 'Urgent')).toBeVisible()
    await expect(chip(todo, 'design')).toBeVisible()
    await input.press('Enter')
    await expect(input).toBeFocused()
    await expect(input).toHaveValue('')
    const t = await serverTask(page, seeded, 'Draft agenda')
    expect(t).toMatchObject({
      dueDate: comingFriday(),
      priority: 'Urgent',
      tagIds: [seeded.tags.design.id],
      statusId: seeded.lists['Website refresh'].statusIds['To do'],
    })
    await expect(grid.getByRole('row', { name: 'Draft agenda', exact: true })).toBeVisible()
  })

  test('New task dialog: the field chips follow the title, and choosing a date takes the typed one out', async ({ page, seeded }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { level: 2, name: 'Today' })).toBeVisible()
    await page.keyboard.press('n')
    const dialog = page.getByRole('dialog', { name: 'New task' })
    await expect(dialog).toBeVisible()
    const title = dialog.getByRole('textbox', { name: 'Task title' })
    await title.fill('Book venue in 2 weeks #design !low')
    await expect(dialog.getByRole('button', { name: /^Due date: / })).not.toHaveAccessibleName('Due date: none, add')
    await expect(dialog.getByRole('button', { name: 'Tags: design, change' })).toBeVisible()
    await expect(dialog.getByRole('button', { name: /^Priority: Low/ })).toBeVisible()

    await test.step('picking Tomorrow takes "in 2 weeks" out of the title', async () => {
      await dialog.getByRole('button', { name: /^Due date: / }).click()
      await page.getByRole('group', { name: 'Quick picks' }).getByRole('button', { name: /^Tomorrow/ }).click()
      await expect(title).toHaveValue('Book venue #design !low')
      await expect(dialog.getByRole('button', { name: 'Due date: Tomorrow, change' })).toBeVisible()
    })

    await test.step('unticking the typed tag takes "#design" out too', async () => {
      await dialog.getByRole('button', { name: 'Tags: design, change' }).click()
      const listbox = page.getByRole('listbox')
      await listbox.getByRole('option', { name: 'design' }).click()
      if (await listbox.isVisible()) await page.keyboard.press('Escape')
      await expect(dialog).toBeVisible()
      await expect(title).toHaveValue('Book venue !low')
    })

    await title.press('Enter')
    const t = await serverTask(page, seeded, 'Book venue')
    expect(t).toMatchObject({ dueDate: day(1).iso, priority: 'Low', tagIds: [] })
  })

  test('date picker: type a date and press Enter', async ({ page, seeded }) => {
    const target = seeded.tasks['Write onboarding guide']
    await page.goto(`/t/${target.id}`)
    const due = page.getByRole('button', { name: /^Due date: / }).first()
    await due.click()
    const box = page.getByRole('textbox', { name: 'Type a date' })
    await expect(box).toBeVisible()
    await box.fill('banana')
    await expect(page.getByText('No date yet')).toBeVisible()
    await box.fill('in 2 weeks')
    await expect(page.getByText(day(14).medium, { exact: true })).toBeVisible()
    await expectNoAxeViolations(page)
    await box.press('Enter')
    await expect(box).toBeHidden()
    await expect.poll(async () => (await allTasks(page, seeded)).find((t) => t.id === target.id)?.dueDate).toBe(day(14).iso)
  })
})

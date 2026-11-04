// The List view (spec 11.1 scenarios 4–9 and 12): inline add and paste, inline edits, group and sort, filters,
// subtasks, the keyboard and selection, and drag. Each state is checked with axe in both themes.
import { MOD, api, dragTo, expect, expectNoAxeViolations, stageC, test, type Seeded, type Task } from './fixtures'

// Playwright's Page and Locator types, read off the fixtures' own signatures (scenario files import fixtures only).
type Page = Parameters<typeof api>[0]
type Locator = Parameters<typeof dragTo>[1]

const LIST = 'Website refresh'

const grid = (page: Page, list = LIST): Locator => page.getByRole('treegrid', { name: `${list} tasks` })

/** A group of the List: the rowgroup named by its label ("To do", "Urgent", "Overdue"). */
const group = (page: Page, label: string): Locator => grid(page).getByRole('rowgroup', { name: label, exact: true })

/** A task row by its title. */
const row = (scope: Page | Locator, title: string): Locator =>
  ('getByRole' in scope && 'goto' in scope ? grid(scope) : scope).getByRole('row', { name: title, exact: true })

/** The top-level task rows of a group (or the whole List), in order. */
const topRows = (scope: Locator): Locator => scope.locator('[role="row"][data-task-row][aria-level="1"]')

/** The titles of the top-level task rows, in order. */
const titlesOf = (scope: Locator): Promise<string[]> =>
  topRows(scope).evaluateAll((rows) => rows.map((r) => r.getAttribute('aria-label') ?? ''))

const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** The toast holding `text` (a status, or an alert for errors). */
const toast = (page: Page, text: string): Locator =>
  notifications(page).locator('[role="status"], [role="alert"]').filter({ hasText: text })

/** The task row that holds focus (the row itself or a control inside it), by title. */
const focusedRow = (page: Page): Promise<string | null> =>
  page.evaluate(() => document.activeElement?.closest('[data-task-row]')?.getAttribute('aria-label') ?? null)

/** Opens a list's List view and waits for its rows. */
async function gotoList(page: Page, seeded: Seeded, list = LIST): Promise<void> {
  await page.goto(`/l/${seeded.lists[list].id}/list`)
  await expect(topRows(grid(page, list)).first()).toBeVisible()
}

/** The list's tasks as the server has them. */
const serverTasks = (page: Page, seeded: Seeded, list = LIST): Promise<Task[]> => api(page).listTasks(seeded.lists[list].id)

/** Top-level titles of one status, in the server's order (position). */
async function serverOrder(page: Page, seeded: Seeded, status: string): Promise<string[]> {
  const statusId = seeded.lists[LIST].statusIds[status]
  const tasks = (await serverTasks(page, seeded)).filter((t) => t.statusId === statusId && !t.parentTaskId)
  return tasks.sort((a, b) => a.position - b.position).map((t) => t.title)
}

/** Waits until no CSS transition runs (a toast fading out, colours following the theme), so axe sees settled colours. */
async function settled(page: Page): Promise<void> {
  await page.waitForFunction(() =>
    document.getAnimations().every((a) => !(a instanceof CSSTransition) || a.playState !== 'running'),
  )
}

/** Dismisses the toasts so that none starts fading out while axe measures contrast, and waits until they are gone. */
async function clearToasts(page: Page): Promise<void> {
  for (const dismiss of await notifications(page).getByRole('button', { name: 'Dismiss' }).all()) {
    await dismiss.click({ timeout: 2000 }).catch(() => undefined)
  }
  await expect(notifications(page).locator('[role="status"], [role="alert"]')).toHaveCount(0)
}

/** axe in the light theme, then the dark one (the theme follows the system by default), then back to light. */
async function axeBothThemes(page: Page): Promise<void> {
  await clearToasts(page)
  await page.mouse.move(0, 0)
  await page.emulateMedia({ colorScheme: 'light', reducedMotion: 'reduce' })
  await settled(page)
  await expectNoAxeViolations(page)
  await page.emulateMedia({ colorScheme: 'dark', reducedMotion: 'reduce' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')
  await settled(page)
  await expectNoAxeViolations(page)
  await page.emulateMedia({ colorScheme: 'light', reducedMotion: 'no-preference' })
}

/** Resolves once the server has answered `count` task creates. */
function creates(page: Page, count: number): Promise<void> {
  let seen = 0
  return new Promise((resolve) => {
    const onResponse = (r: { request(): { method(): string }; url(): string; ok(): boolean }) => {
      if (r.request().method() === 'POST' && /\/api\/lists\/[^/]+\/tasks$/.test(new URL(r.url()).pathname) && r.ok()) {
        if (++seen === count) {
          page.off('response', onResponse)
          resolve()
        }
      }
    }
    page.on('response', onResponse)
  })
}

test.describe('List', () => {
  test('4 inline add and paste', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    await axeBothThemes(page)
    const todo = group(page, 'To do')
    const before = await titlesOf(todo)
    expect(before).toHaveLength(7)

    await test.step('Enter creates at the end of the group and keeps the input open', async () => {
      const saved = creates(page, 2)
      await todo.getByRole('button', { name: 'Add task', exact: true }).click()
      const input = todo.getByRole('textbox', { name: 'Task name' })
      await expect(input).toBeFocused()
      await input.fill('One')
      await input.press('Enter')
      await expect(input).toBeFocused()
      await expect(input).toHaveValue('')
      await input.fill('Two')
      await input.press('Enter')
      await expect(input).toBeFocused()
      await expect.poll(() => titlesOf(todo)).toEqual([...before, 'One', 'Two'])
      await saved
    })

    await test.step('pasting three lines asks first, then creates them in order', async () => {
      const input = todo.getByRole('textbox', { name: 'Task name' })
      const saved = creates(page, 3)
      await page.evaluate(() => navigator.clipboard.writeText('a\nb\nc'))
      await input.press(`${MOD}+V`)
      const confirm = page.getByRole('alertdialog', { name: 'Create 3 tasks?' })
      await expect(confirm).toBeVisible()
      await confirm.getByRole('button', { name: 'Create 3 tasks' }).click()
      await expect(toast(page, 'Created 3 tasks')).toBeVisible()
      await expect.poll(() => titlesOf(todo)).toEqual([...before, 'One', 'Two', 'a', 'b', 'c'])
      await saved
      await page.reload()
      await expect.poll(() => titlesOf(group(page, 'To do'))).toEqual([...before, 'One', 'Two', 'a', 'b', 'c'])
    })

    await test.step('the header "+" creates at the top of the group', async () => {
      const head = group(page, 'To do')
      const saved = creates(page, 1)
      await head.getByRole('row').first().hover()
      await head.getByRole('button', { name: 'Add task to To do' }).click()
      const input = head.getByRole('textbox', { name: 'Task name' })
      await expect(input).toBeFocused()
      await input.fill('Zero')
      await input.press('Enter')
      await expect.poll(async () => (await titlesOf(head))[0]).toBe('Zero')
      await saved
      await input.press('Escape')
      await expect(head.getByRole('button', { name: 'Add task to To do' })).toBeFocused()
      await page.reload()
      await expect.poll(async () => (await titlesOf(group(page, 'To do')))[0]).toBe('Zero')
    })
  })

  test('5 inline edits persist', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const id = seeded.tasks['Plan Q1 roadmap'].id
    const title = 'Plan Q1 roadmap'
    const r = () => row(page, title)

    await test.step('the row icon changes the status: the row moves to the top of In progress and keeps focus', async () => {
      await r().getByRole('button', { name: 'Status: To do, change' }).click()
      await page.getByRole('listbox', { name: 'Status' }).getByRole('option', { name: 'In progress' }).click()
      await expect.poll(async () => (await titlesOf(group(page, 'In progress')))[0]).toBe(title)
      await expect(row(group(page, 'To do'), title)).toHaveCount(0)
      await expect.poll(() => focusedRow(page)).toBe(title)
      await expect.poll(async () => (await api(page).task(id)).task.statusId).toBe(seeded.lists[LIST].statusIds['In progress'])
    })

    await test.step('D picks Today', async () => {
      await r().focus()
      await page.keyboard.press('d')
      await page.getByRole('group', { name: 'Quick picks' }).getByRole('button', { name: 'Today' }).click()
      await expect(r().getByRole('button', { name: 'Due date: Today, change' })).toBeVisible()
    })

    await test.step('P picks Urgent', async () => {
      await r().focus()
      await page.keyboard.press('p')
      await page.getByRole('menu', { name: 'Priority' }).getByRole('menuitem', { name: /^Urgent/ }).click()
      await expect(r().getByRole('button', { name: 'Priority: Urgent, change' })).toBeVisible()
    })

    await test.step('A adds Ana Moreno', async () => {
      await r().focus()
      await page.keyboard.press('a')
      const box = page.getByRole('listbox', { name: 'Assignees' })
      await box.getByRole('option', { name: /^Ana Moreno/ }).click()
      await page.keyboard.press('Escape')
      await expect(box).toBeHidden()
      await expect(r().getByRole('button', { name: /^Assignees: Ana Moreno, change$/ })).toBeVisible()
    })

    await test.step('"Tags: none, add" adds bug', async () => {
      await r().hover()
      await r().getByRole('button', { name: 'Tags: none, add' }).click()
      const box = page.getByRole('listbox', { name: 'Tags' })
      await box.getByRole('option', { name: 'bug', exact: true }).click()
      await page.keyboard.press('Escape')
      await expect(box).toBeHidden()
      await expect(r().getByRole('button', { name: 'Tags: bug, change' })).toBeVisible()
    })

    await test.step('F2 renames', async () => {
      await r().focus()
      await page.keyboard.press('F2')
      const field = r().getByRole('textbox', { name: 'Rename task' })
      await expect(field).toBeFocused()
      await field.fill('Plan Q1 roadmap v2')
      await field.press('Enter')
      await expect(row(page, 'Plan Q1 roadmap v2')).toBeVisible()
      await expect.poll(() => focusedRow(page)).toBe('Plan Q1 roadmap v2')
    })

    await test.step('after a reload every value is kept', async () => {
      await expect
        .poll(async () => {
          const t = (await api(page).task(id)).task
          return [t.title, t.dueDate !== null, t.priority, t.assigneeIds.length, t.tagIds.length]
        })
        .toEqual(['Plan Q1 roadmap v2', true, 'Urgent', 1, 1])
      await page.reload()
      const renamed = row(group(page, 'In progress'), 'Plan Q1 roadmap v2')
      await expect(renamed).toBeVisible()
      await expect(renamed.getByRole('button', { name: 'Status: In progress, change' }).first()).toBeVisible()
      await expect(renamed.getByRole('button', { name: 'Due date: Today, change' })).toBeVisible()
      await expect(renamed.getByRole('button', { name: 'Priority: Urgent, change' })).toBeVisible()
      await expect(renamed.getByRole('button', { name: 'Assignees: Ana Moreno, change' })).toBeVisible()
      await expect(renamed.getByRole('button', { name: 'Tags: bug, change' })).toBeVisible()
      const t = (await api(page).task(id)).task
      expect(t.assigneeIds).toEqual([seeded.members['Ana Moreno'].userId])
      expect(t.tagIds).toEqual([seeded.tags.bug.id])
    })
  })

  test('6 group and sort persist', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const groupButton = page.getByRole('button', { name: /^Group: / })
    const sortButton = page.getByRole('button', { name: /^Sort: / })
    const completed = page.getByRole('button', { name: 'Show completed tasks' })

    await test.step('group by Priority, sort by Due date descending, hide Tags, hide completed tasks', async () => {
      await groupButton.click()
      await page.getByRole('menuitemradio', { name: 'Priority' }).click()
      await expect(groupButton).toHaveAccessibleName('Group: Priority')
      await expect(group(page, 'Urgent')).toBeVisible()
      await sortButton.click()
      await page.getByRole('menuitemradio', { name: 'Due date' }).click()
      await expect(sortButton).toHaveAccessibleName('Sort: Due date')
      await sortButton.click()
      await page.getByRole('menuitemradio', { name: 'Descending' }).click()
      await page.getByRole('button', { name: 'Columns' }).click()
      await page.getByRole('menuitemcheckbox', { name: 'Tags' }).click()
      await page.keyboard.press('Escape')
      await expect(grid(page).getByRole('columnheader', { name: 'Tags' })).toHaveCount(0)
      await expect(completed).toHaveAttribute('aria-pressed', 'true')
      await completed.click()
      await expect(completed).toHaveAttribute('aria-pressed', 'false')
      await expect(group(page, 'Completed')).toHaveCount(0)
    })

    const expectConfig = async () => {
      await expect(groupButton).toHaveAccessibleName('Group: Priority')
      await expect(sortButton).toHaveAccessibleName('Sort: Due date')
      await expect(completed).toHaveAttribute('aria-pressed', 'false')
      await expect(group(page, 'Urgent')).toBeVisible()
      await expect(grid(page).getByRole('columnheader', { name: 'Status' })).toBeVisible()
      await expect(grid(page).getByRole('columnheader', { name: 'Tags' })).toHaveCount(0)
      // Due date descending inside Urgent: Sat (T+3), then Yesterday, then the one with no date (empty values last).
      await expect
        .poll(() => titlesOf(group(page, 'Urgent')))
        .toEqual(['Review accessibility report', 'Fix sign-in redirect', 'Add structured data'])
      await sortButton.click()
      await expect(page.getByRole('menuitemradio', { name: 'Descending' })).toHaveAttribute('aria-checked', 'true')
      await page.keyboard.press('Escape')
    }

    await test.step('the same after a reload', async () => {
      await page.reload()
      await expect(topRows(grid(page)).first()).toBeVisible()
      await expectConfig()
    })

    await test.step('the same after the Board and back', async () => {
      await page.getByRole('navigation', { name: 'Views' }).getByRole('link', { name: 'Board' }).click()
      await expect(page).toHaveURL(/\/board$/)
      await page.getByRole('navigation', { name: 'Views' }).getByRole('link', { name: 'List' }).click()
      await expect(page).toHaveURL(/\/list$/)
      await expectConfig()
    })

    await test.step('Admin keeps its own defaults', async () => {
      await gotoList(page, seeded, 'Admin')
      await expect(groupButton).toHaveAccessibleName('Group: Status')
      await expect(sortButton).toHaveAccessibleName('Sort: Manual')
      await expect(completed).toHaveAttribute('aria-pressed', 'true')
    })
  })

  test('7 filters', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const filterButton = page.getByRole('button', { name: /^Filter/ })
    const note = page.getByRole('paragraph').filter({ hasText: 'Filters are hiding' })

    await test.step('Status = Review shows the 6 Review tasks', async () => {
      await filterButton.click()
      await page.getByRole('group', { name: 'Status' }).getByRole('checkbox', { name: 'Review' }).check()
      await page.keyboard.press('Escape')
      await expect
        .poll(() => titlesOf(grid(page)))
        .toEqual([
          'Fix sign-in redirect',
          'Choose hero image',
          'Set up redirects for old URLs',
          'Update team photos',
          'Proofread About page',
          'Create 404 page',
        ])
      await expect(page.getByRole('button', { name: 'Status · 1' })).toBeVisible()
      await expect(note).toHaveText('Filters are hiding 19 tasks · Clear filters')
    })

    await test.step('Tag = bug leaves 2', async () => {
      await filterButton.click()
      await page.getByRole('group', { name: 'Tags' }).getByRole('checkbox', { name: 'bug' }).check()
      await page.keyboard.press('Escape')
      await expect.poll(() => titlesOf(grid(page))).toEqual(['Fix sign-in redirect', 'Create 404 page'])
      await expect(note).toHaveText('Filters are hiding 23 tasks · Clear filters')
      await axeBothThemes(page)
    })

    await test.step('a task added in the empty To do group stays with "Hidden by filters", until a reload', async () => {
      const todo = group(page, 'To do')
      const saved = creates(page, 1)
      await todo.getByRole('button', { name: 'Add task', exact: true }).click()
      const input = todo.getByRole('textbox', { name: 'Task name' })
      await input.fill('Draft release notes')
      await input.press('Enter')
      const added = row(todo, 'Draft release notes')
      await expect(added).toBeVisible()
      await expect(added.getByText('Hidden by filters')).toBeVisible()
      await saved
      await input.press('Escape')
      await page.reload()
      await expect.poll(() => titlesOf(grid(page))).toEqual(['Fix sign-in redirect', 'Create 404 page'])
      await expect(row(page, 'Draft release notes')).toHaveCount(0)
    })

    await test.step('Assigned to me leaves only Fix sign-in redirect', async () => {
      await page.getByRole('button', { name: 'Assigned to me' }).click()
      await expect.poll(() => titlesOf(grid(page))).toEqual(['Fix sign-in redirect'])
    })

    await stageC('the Board applies the same filters', async () => {
      await page.getByRole('navigation', { name: 'Views' }).getByRole('link', { name: 'Board' }).click()
      const review = page.getByRole('region', { name: /^Review, \d+ tasks?$/ })
      await expect(review.getByRole('article')).toHaveCount(1)
      await expect(review.getByRole('article', { name: /Fix sign-in redirect/ })).toBeVisible()
      await page.getByRole('navigation', { name: 'Views' }).getByRole('link', { name: 'List' }).click()
      await expect(topRows(grid(page)).first()).toBeVisible()
    })

    await test.step('Clear all brings every task back', async () => {
      await page.getByRole('button', { name: 'Clear all' }).click()
      await expect(note).toHaveCount(0)
      await expect(row(page, 'Draft release notes')).toBeVisible()
      await expect.poll(async () => (await titlesOf(grid(page))).length).toBe(20)
    })
  })

  test('filtered-empty state: Clear filters, and Search all lists while searching', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    await page.getByRole('searchbox', { name: 'Search this list' }).fill('zzzz')
    await expect(page.getByText('No tasks match these filters.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Search all lists for “zzzz”' })).toBeVisible()
    await axeBothThemes(page)
    await page.getByRole('button', { name: 'Clear filters' }).click()
    await expect(page.getByRole('searchbox', { name: 'Search this list' })).toHaveValue('')
    await expect(row(page, 'Plan Q1 roadmap')).toBeVisible()
  })

  test('8 subtasks', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const parent = row(page, 'Write onboarding guide')
    const subs = grid(page).locator('[role="row"][data-task-row][aria-level="2"]')

    await test.step('Show subtasks shows 3 subtask rows', async () => {
      await parent.getByRole('button', { name: 'Show subtasks' }).click()
      await expect(parent).toHaveAttribute('aria-expanded', 'true')
      await expect(subs).toHaveCount(3)
      await expect(subs).toHaveText([/Draft outline/, /Collect screenshots/, /Review with Ana/])
      await axeBothThemes(page)
    })

    await test.step('Add subtask creates in place, and the parent shows 1/4', async () => {
      const saved = creates(page, 1)
      await grid(page).getByRole('button', { name: 'Add subtask' }).click()
      const input = grid(page).getByRole('textbox', { name: 'Subtask name' })
      await expect(input).toBeFocused()
      await input.fill('Proofread')
      await input.press('Enter')
      await expect(subs).toHaveCount(4)
      await expect(subs.last()).toHaveAccessibleName('Proofread')
      await expect(parent.getByLabel('1 of 4 subtasks done')).toBeVisible()
      await saved
      await input.press('Escape')
    })

    await test.step('Collapse all, then Expand all', async () => {
      await page.getByRole('button', { name: 'Collapse all subtasks' }).click()
      await expect(subs).toHaveCount(0)
      await expect(parent).toHaveAttribute('aria-expanded', 'false')
      await page.getByRole('button', { name: 'Expand all subtasks' }).click()
      await expect(subs).toHaveCount(4)
      await expect(parent).toHaveAttribute('aria-expanded', 'true')
    })
  })

  test('9 keyboard and selection', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const rows = topRows(grid(page))
    const second = await rows.nth(1).getAttribute('aria-label')

    await test.step('J J K ends on the second row', async () => {
      await rows.first().focus()
      await page.keyboard.press('j')
      await page.keyboard.press('j')
      await page.keyboard.press('k')
      await expect(rows.nth(1)).toBeFocused()
    })

    await test.step('Enter opens the panel; Esc closes it and the row keeps focus', async () => {
      await page.keyboard.press('Enter')
      const panel = page
        .getByRole('complementary', { name: second ?? '', exact: true })
        .or(page.getByRole('dialog', { name: second ?? '', exact: true }))
      await expect(panel).toBeVisible()
      await expect(page).toHaveURL(/[?&]task=/)
      await page.keyboard.press('Escape')
      await expect(panel).toBeHidden()
      await expect(rows.nth(1)).toBeFocused()
    })

    await test.step('X, then Shift+click three rows below selects 4', async () => {
      await page.keyboard.press('x')
      await expect(rows.nth(1)).toHaveAttribute('aria-selected', 'true')
      const target = rows.nth(4)
      const name = await target.getAttribute('aria-label')
      await page.getByRole('checkbox', { name: `Select ${name}` }).click({ modifiers: ['Shift'] })
      await expect(grid(page).locator('[aria-selected="true"]')).toHaveCount(4)
      await stageC('the bulk bar says "4 selected"', async () => {
        await expect(page.getByRole('toolbar', { name: 'Bulk actions' })).toContainText('4 selected')
      })
      await axeBothThemes(page)
    })

    await test.step('Esc clears; Mod+A selects every visible top-level row; Esc clears again', async () => {
      await rows.nth(1).focus()
      await page.keyboard.press('Escape')
      await expect(grid(page).locator('[aria-selected="true"]')).toHaveCount(0)
      await rows.nth(1).focus()
      await page.keyboard.press(`${MOD}+A`)
      const visible = await rows.count()
      await expect(grid(page).locator('[aria-selected="true"]')).toHaveCount(visible)
      await stageC(`the bulk bar says "${visible} selected"`, async () => {
        await expect(page.getByRole('toolbar', { name: 'Bulk actions' })).toContainText(`${visible} selected`)
      })
      await page.keyboard.press('Escape')
      await expect(grid(page).locator('[aria-selected="true"]')).toHaveCount(0)
    })

    await test.step('Alt+ArrowDown swaps Update pricing page with the row below, announced and kept', async () => {
      const todo = group(page, 'To do')
      const before = await titlesOf(todo)
      const at = before.indexOf('Update pricing page')
      const after = [...before]
      ;[after[at], after[at + 1]] = [after[at + 1], after[at]]
      await row(page, 'Update pricing page').focus()
      const saved = page.waitForResponse((r) => r.request().method() === 'POST' && /\/move$/.test(r.url()) && r.ok())
      await page.keyboard.press('Alt+ArrowDown')
      await expect.poll(() => titlesOf(todo)).toEqual(after)
      await expect(row(page, 'Update pricing page')).toBeFocused()
      await expect(page.locator('[aria-live="polite"]')).toHaveText(`Moved down, ${at + 2} of ${before.length}`)
      await saved
      await page.reload()
      await expect.poll(() => titlesOf(group(page, 'To do'))).toEqual(after)
      await expect.poll(() => serverOrder(page, seeded, 'To do')).toEqual(after)
    })

    await test.step('Mod+Enter completes with Undo; Undo reopens it', async () => {
      await row(page, 'Update pricing page').focus()
      await page.keyboard.press(`${MOD}+Enter`)
      const done = toast(page, 'Completed “Update pricing page”')
      await expect(done).toBeVisible()
      const id = seeded.tasks['Update pricing page'].id
      await expect.poll(async () => (await api(page).task(id)).task.statusId).toBe(seeded.lists[LIST].statusIds.Done)
      await done.getByRole('button', { name: 'Undo' }).click()
      await expect(row(group(page, 'To do'), 'Update pricing page')).toBeVisible()
      await expect.poll(async () => (await api(page).task(id)).task.statusId).toBe(seeded.lists[LIST].statusIds['To do'])
    })
  })

  test('12 drag a row into another status group', async ({ page, seeded }) => {
    await gotoList(page, seeded)
    const progress = group(page, 'In progress')
    const before = await titlesOf(progress)
    const [above, below] = [before[1], before[2]]
    const source = row(page, 'Plan Q1 roadmap')
    await source.hover()
    const grip = source.locator('.grip')
    await expect(grip).toBeVisible()
    const target = row(progress, below)
    const saved = page.waitForResponse((r) => r.request().method() === 'POST' && /\/move$/.test(r.url()) && r.ok())
    await dragTo(page, grip, target, { targetPosition: { x: 200, y: 6 } })
    await expect(toast(page, 'Moved to In progress')).toBeVisible()
    const expected = [...before.slice(0, 2), 'Plan Q1 roadmap', ...before.slice(2)]
    await expect.poll(() => titlesOf(progress)).toEqual(expected)
    await saved
    await page.reload()
    await expect.poll(() => titlesOf(group(page, 'In progress'))).toEqual(expected)
    await expect.poll(() => serverOrder(page, seeded, 'In progress')).toEqual(expected)
    expect(expected.indexOf('Plan Q1 roadmap')).toBe(expected.indexOf(above) + 1)
  })
})

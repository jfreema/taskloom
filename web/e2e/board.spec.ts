// The Board view and the bulk bar (spec 11.1 scenarios 10, 11 and 13–15): bulk edit, move and delete with undo on
// the List, then drag, keyboard moves and column operations on the Board. Each screen is checked with axe in both
// themes: the List with the bulk bar showing, and the Board with and without a collapsed column.
import { api, dragTo, expect, expectNoAxeViolations, stageC, test, type Seeded, type Status, type Task, type Uuid } from './fixtures'

// Playwright's Page and Locator types, read off the fixtures' own signatures (scenario files import fixtures only).
type Page = Parameters<typeof api>[0]
type Locator = Parameters<typeof dragTo>[1]

const LIST = 'Website refresh'

const escapeRegExp = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

// ---- Feedback and axe ----

const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** The toast holding `text` (a status, or an alert for errors). */
const toast = (page: Page, text: string): Locator =>
  notifications(page).locator('[role="status"], [role="alert"]').filter({ hasText: text })

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

/** Resolves on the next successful move (POST /api/tasks/{id}/move). Start waiting before the action. */
const nextMove = (page: Page): Promise<unknown> =>
  page.waitForResponse((r) => r.request().method() === 'POST' && /\/api\/tasks\/[^/]+\/move$/.test(new URL(r.url()).pathname) && r.ok())

// ---- Server state ----

/** Where a task is, as the server has it. */
async function placeOf(page: Page, id: Uuid): Promise<{ listId: Uuid; statusId: Uuid }> {
  const { task } = await api(page).task(id)
  return { listId: task.listId, statusId: task.statusId }
}

/** A list's statuses, as the server has them. */
async function statusesOf(page: Page, listId: Uuid): Promise<Status[]> {
  const lists = (await api(page).workspaces()).flatMap((w) => w.spaces.flatMap((s) => s.lists))
  return lists.find((l) => l.id === listId)?.statuses ?? []
}

/** Top-level titles of one status of Website refresh, in the server's order (position). */
async function serverOrder(page: Page, seeded: Seeded, statusId: Uuid): Promise<string[]> {
  const tasks: Task[] = await api(page).listTasks(seeded.lists[LIST].id)
  return tasks
    .filter((t) => t.statusId === statusId && !t.parentTaskId)
    .sort((a, b) => a.position - b.position)
    .map((t) => t.title)
}

// ---- The List and the bulk bar ----

const grid = (page: Page): Locator => page.getByRole('treegrid', { name: `${LIST} tasks` })

/** The top-level task rows of a scope, in order. */
const topRows = (scope: Locator): Locator => scope.locator('[role="row"][data-task-row][aria-level="1"]')

/** A task row of the List by its title. */
const row = (page: Page, title: string): Locator => grid(page).getByRole('row', { name: title, exact: true })

/** A group of the List: the rowgroup named by its label. */
const group = (page: Page, label: string): Locator => grid(page).getByRole('rowgroup', { name: label, exact: true })

const bulkBar = (page: Page): Locator => page.getByRole('toolbar', { name: 'Bulk actions' })

/** Opens Website refresh in the List view and waits for its rows. */
async function gotoList(page: Page, seeded: Seeded): Promise<void> {
  await page.goto(`/l/${seeded.lists[LIST].id}/list`)
  await expect(topRows(grid(page)).first()).toBeVisible()
  await expect(grid(page)).not.toHaveAttribute('aria-busy', 'true')
}

/** Ticks the rows' "Select <title>" checkboxes. */
async function select(page: Page, titles: string[]): Promise<void> {
  for (const title of titles) await page.getByRole('checkbox', { name: `Select ${title}` }).click()
}

/** Bulk bar Move, then Admin in the "Move N tasks" dialog, then "Move". */
async function moveToAdmin(page: Page, count: number): Promise<void> {
  await bulkBar(page).getByRole('button', { name: 'Move', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: `Move ${count} tasks` })
  await expect(dialog).toBeVisible()
  await dialog.getByRole('button', { name: 'List: none, add' }).click()
  await page.getByRole('option', { name: /Admin$/ }).click()
  await dialog.getByRole('button', { name: 'Move', exact: true }).click()
  await expect(dialog).toBeHidden()
}

// ---- The Board ----

const board = (page: Page): Locator => page.getByRole('region', { name: 'Board', exact: true })

/** A column (or a collapsed column's strip) by its status name, whatever its count: region "Review, 6 tasks". */
const column = (page: Page, status: string): Locator =>
  board(page).getByRole('region', { name: new RegExp(`^${escapeRegExp(status)}, \\d+ tasks?$`) })

/** A column by its exact name and count. */
const columnNamed = (page: Page, status: string, count: number): Locator =>
  board(page).getByRole('region', { name: `${status}, ${count} ${count === 1 ? 'task' : 'tasks'}`, exact: true })

/** A card by its title. */
const card = (scope: Locator, title: string): Locator => scope.getByRole('article', { name: title, exact: true })

/** The titles of a column's cards, in order, once they have all mounted (the column is aria-busy until then, S10). */
async function cardTitles(scope: Locator): Promise<string[]> {
  await expect(scope).not.toHaveAttribute('aria-busy', 'true')
  return scope.locator('article[data-card]').evaluateAll((cards) => cards.map((c) => c.getAttribute('aria-label') ?? ''))
}

/** Opens Website refresh in the Board view and waits for its cards. */
async function gotoBoard(page: Page, seeded: Seeded): Promise<void> {
  await page.goto(`/l/${seeded.lists[LIST].id}/board`)
  await expect(board(page).getByRole('article').first()).toBeVisible()
  await expect(board(page).locator('[aria-busy="true"]')).toHaveCount(0)
}

/** Opens a column's menu and picks an item. */
async function columnAction(page: Page, status: string, item: string): Promise<void> {
  await board(page).getByRole('button', { name: `Column actions for ${status}`, exact: true }).click()
  await page.getByRole('menu', { name: `Actions for ${status}` }).getByRole('menuitem', { name: item, exact: true }).click()
}

test.describe('Bulk bar', () => {
  test('10 bulk edit, move and undo', async ({ page, seeded }) => {
    const three = ['Write onboarding guide', 'Plan Q1 roadmap', 'Update pricing page']
    const original = three.map((title) => seeded.tasks[title].priority)
    expect(new Set(original).size, 'the three seeded tasks have different priorities').toBe(3)
    expect(original).not.toContain('Urgent')
    const priorities = async (): Promise<string[]> => {
      const tasks = await api(page).listTasks(seeded.lists[LIST].id)
      return three.map((title) => tasks.find((t) => t.title === title)?.priority ?? '')
    }
    await gotoList(page, seeded)

    await test.step('selecting 3 tasks shows the bar with "3 selected" and every action', async () => {
      await select(page, three)
      await expect(bulkBar(page)).toContainText('3 selected')
      for (const name of ['Status', 'Assignees', 'Due date', 'Priority', 'Tags', 'Move', 'Delete', 'Clear selection']) {
        await expect(bulkBar(page).getByRole('button', { name, exact: true })).toBeVisible()
      }
      await axeBothThemes(page)
    })

    await test.step('Priority, Urgent makes all 3 Urgent with one undo toast', async () => {
      await bulkBar(page).getByRole('button', { name: 'Priority', exact: true }).click()
      await page.getByRole('menu', { name: 'Priority' }).getByRole('menuitemradio', { name: 'Urgent' }).click()
      await expect(toast(page, 'Updated 3 tasks')).toBeVisible()
      await expect(notifications(page).locator('[role="status"]')).toHaveCount(1)
      await expect.poll(priorities).toEqual(['Urgent', 'Urgent', 'Urgent'])
    })

    await test.step('Undo brings the original priorities back, also after a reload', async () => {
      await toast(page, 'Updated 3 tasks').getByRole('button', { name: 'Undo' }).click()
      await expect.poll(priorities).toEqual(original)
      await page.reload()
      await expect(topRows(grid(page)).first()).toBeVisible()
      await expect.poll(priorities).toEqual(original)
      await expect(bulkBar(page)).toBeHidden()
    })

    const pair = ['Plan Q1 roadmap', 'Update pricing page']
    const ids = pair.map((title) => seeded.tasks[title].id)
    const list = seeded.lists[LIST]
    const admin = seeded.lists.Admin
    for (const title of pair) expect(seeded.tasks[title].statusId).toBe(list.statusIds['To do'])

    await test.step('Move 2 To do tasks to Admin: the toast, the rows leave, the bar hides', async () => {
      await select(page, pair)
      await expect(bulkBar(page)).toContainText('2 selected')
      await moveToAdmin(page, 2)
      await expect(toast(page, 'Moved 2 tasks to Admin')).toBeVisible()
      for (const title of pair) await expect(row(page, title)).toHaveCount(0)
      await expect(bulkBar(page)).toBeHidden()
      for (const id of ids) await expect.poll(async () => (await placeOf(page, id)).listId).toBe(admin.id)
    })

    await test.step('Undo brings both back to To do, also after a reload', async () => {
      await toast(page, 'Moved 2 tasks to Admin').getByRole('button', { name: 'Undo' }).click()
      for (const title of pair) await expect(group(page, 'To do').getByRole('row', { name: title, exact: true })).toBeVisible()
      for (const id of ids) {
        await expect.poll(() => placeOf(page, id)).toEqual({ listId: list.id, statusId: list.statusIds['To do'] })
      }
      await page.reload()
      for (const title of pair) await expect(group(page, 'To do').getByRole('row', { name: title, exact: true })).toBeVisible()
    })

    await test.step('moved again, both are in Admin with the status To do after a reload', async () => {
      await clearToasts(page)
      await select(page, pair)
      await moveToAdmin(page, 2)
      await expect(toast(page, 'Moved 2 tasks to Admin')).toBeVisible()
      for (const id of ids) await expect.poll(async () => (await placeOf(page, id)).listId).toBe(admin.id)
      await page.reload()
      await expect(topRows(grid(page)).first()).toBeVisible()
      for (const title of pair) await expect(row(page, title)).toHaveCount(0)
      for (const id of ids) expect(await placeOf(page, id)).toEqual({ listId: admin.id, statusId: admin.statusIds['To do'] })
    })
  })

  test('11 bulk delete: undo, then commit', async ({ page, seeded }) => {
    const pair = ['Plan Q1 roadmap', 'Update pricing page']
    await gotoList(page, seeded)

    await test.step('Delete hides both rows at once, with "Deleted 2 tasks"', async () => {
      await select(page, pair)
      await expect(bulkBar(page)).toContainText('2 selected')
      await bulkBar(page).getByRole('button', { name: 'Delete', exact: true }).click()
      await expect(toast(page, 'Deleted 2 tasks')).toBeVisible()
      for (const title of pair) await expect(row(page, title)).toHaveCount(0)
      await expect(bulkBar(page)).toBeHidden()
    })

    await test.step('Undo brings the rows back', async () => {
      await toast(page, 'Deleted 2 tasks').getByRole('button', { name: 'Undo' }).click()
      for (const title of pair) await expect(row(page, title)).toBeVisible()
      await expect(bulkBar(page)).toBeHidden()
    })

    await test.step('deleted again, they are gone once the toast has closed (9s) and after a reload', async () => {
      await clearToasts(page)
      await select(page, pair)
      const committed = page.waitForResponse(
        (r) => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/tasks/bulk-delete' && r.ok(),
        { timeout: 15_000 },
      )
      await bulkBar(page).getByRole('button', { name: 'Delete', exact: true }).click()
      await expect(toast(page, 'Deleted 2 tasks')).toBeVisible()
      // A hovered toast holds its timer, so the pointer leaves it alone while the 8s run out.
      await page.mouse.move(0, 0)
      await committed
      await expect(toast(page, 'Deleted 2 tasks')).toHaveCount(0)
      await page.reload()
      await expect(topRows(grid(page)).first()).toBeVisible()
      for (const title of pair) await expect(row(page, title)).toHaveCount(0)
      const left = (await api(page).listTasks(seeded.lists[LIST].id)).map((t) => t.title)
      for (const title of pair) expect(left).not.toContain(title)
    })
  })
})

test.describe('Board', () => {
  test('13 board drag', async ({ page, seeded }) => {
    const statusIds = seeded.lists[LIST].statusIds
    await gotoBoard(page, seeded)
    await axeBothThemes(page)
    const todoBefore = await cardTitles(column(page, 'To do'))
    expect(todoBefore).toContain('Update pricing page')
    await expect(columnNamed(page, 'To do', 7)).toBeVisible()
    await expect(columnNamed(page, 'Review', 6)).toBeVisible()

    await test.step('dragging "Update pricing page" to Review updates the counts, with an undo toast', async () => {
      const saved = nextMove(page)
      await dragTo(page, card(board(page), 'Update pricing page'), column(page, 'Review'))
      await expect(card(column(page, 'Review'), 'Update pricing page')).toBeVisible()
      await expect(columnNamed(page, 'To do', 6)).toBeVisible()
      await expect(columnNamed(page, 'Review', 7)).toBeVisible()
      await expect(toast(page, 'Moved to Review').getByRole('button', { name: 'Undo' })).toBeVisible()
      await saved
      const id = seeded.tasks['Update pricing page'].id
      expect((await placeOf(page, id)).statusId).toBe(statusIds.Review)
    })

    await test.step('Undo moves the card back to its place in To do', async () => {
      await toast(page, 'Moved to Review').getByRole('button', { name: 'Undo' }).click()
      await expect.poll(() => cardTitles(column(page, 'To do'))).toEqual(todoBefore)
      await expect(columnNamed(page, 'Review', 6)).toBeVisible()
      await expect.poll(() => serverOrder(page, seeded, statusIds['To do'])).toEqual(todoBefore)
    })

    await test.step('reordering two cards within To do is kept after a reload', async () => {
      await clearToasts(page)
      const todo = column(page, 'To do')
      const [first, second, third] = todoBefore
      const expected = [third, first, second, ...todoBefore.slice(3)]
      const saved = nextMove(page)
      // Onto the top edge of the first card: the third card goes above it.
      await dragTo(page, card(todo, third), card(todo, first), { targetPosition: { x: 120, y: 8 } })
      await expect.poll(() => cardTitles(todo)).toEqual(expected)
      await saved
      await expect(notifications(page).locator('[role="status"]').filter({ hasText: 'Moved' })).toHaveCount(0)
      await page.reload()
      await expect(card(board(page), third)).toBeVisible()
      await expect.poll(() => cardTitles(column(page, 'To do'))).toEqual(expected)
      await expect.poll(() => serverOrder(page, seeded, statusIds['To do'])).toEqual(expected)
    })
  })

  test('14 board keyboard', async ({ page, seeded }) => {
    const statusIds = seeded.lists[LIST].statusIds
    const title = 'Plan Q1 roadmap'
    const live = page.locator('[aria-live="polite"]')
    await gotoBoard(page, seeded)
    let order: string[] = []

    await test.step('Alt+ArrowRight moves the card to In progress and announces it', async () => {
      await card(board(page), title).focus()
      const saved = nextMove(page)
      await page.keyboard.press('Alt+ArrowRight')
      const progress = column(page, 'In progress')
      await expect(card(progress, title)).toBeVisible()
      await expect(card(progress, title)).toBeFocused()
      order = await cardTitles(progress)
      await expect(live).toHaveText(`Moved to In progress, ${order.indexOf(title) + 1} of ${order.length}`)
      await saved
      expect((await placeOf(page, seeded.tasks[title].id)).statusId).toBe(statusIds['In progress'])
    })

    await test.step('Alt+ArrowDown moves it down one place, announced and kept after a reload', async () => {
      const at = order.indexOf(title)
      expect(at, 'the card has a card below it').toBeLessThan(order.length - 1)
      const expected = [...order]
      ;[expected[at], expected[at + 1]] = [expected[at + 1], expected[at]]
      const saved = nextMove(page)
      await page.keyboard.press('Alt+ArrowDown')
      await expect.poll(() => cardTitles(column(page, 'In progress'))).toEqual(expected)
      await expect(card(board(page), title)).toBeFocused()
      await expect(live).toHaveText(`Moved down, ${at + 2} of ${order.length}`)
      await saved
      await page.reload()
      await expect(card(board(page), title)).toBeVisible()
      await expect.poll(() => cardTitles(column(page, 'In progress'))).toEqual(expected)
      await expect.poll(() => serverOrder(page, seeded, statusIds['In progress'])).toEqual(expected)
    })
  })

  test('15 board column operations', async ({ page, seeded }) => {
    const list = seeded.lists[LIST]
    const reviewId = list.statusIds.Review
    const statusOnServer = async (id: Uuid): Promise<Status | undefined> =>
      (await statusesOf(page, list.id)).find((s) => s.id === id)
    // Every task in Review (subtasks too), for the delete-with-move at the end.
    const inReview = (await api(page).listTasks(list.id)).filter((t) => t.statusId === reviewId)
    expect(inReview.filter((t) => !t.parentTaskId)).toHaveLength(6)
    await gotoBoard(page, seeded)

    await test.step('Rename turns Review into "QA"', async () => {
      await columnAction(page, 'Review', 'Rename')
      const name = board(page).getByRole('textbox', { name: 'Status name' })
      await expect(name).toBeFocused()
      await name.fill('QA')
      await name.press('Enter')
      await expect(columnNamed(page, 'QA', 6)).toBeVisible()
      await expect.poll(async () => (await statusOnServer(reviewId))?.name).toBe('QA')
    })

    await test.step('Colour, Pink recolours it', async () => {
      const before = (await statusOnServer(reviewId))?.color
      expect(before).not.toBe('#DB4C8B')
      await board(page).getByRole('button', { name: 'Column actions for QA', exact: true }).click()
      const menu = page.getByRole('menu', { name: 'Actions for QA' })
      await menu.getByRole('menuitemradio', { name: 'Pink', exact: true }).click()
      await expect(menu).toBeHidden()
      await expect.poll(async () => (await statusOnServer(reviewId))?.color.toUpperCase()).toBe('#DB4C8B')
    })

    await test.step('Collapse column leaves a 44px strip that is kept after a reload', async () => {
      await columnAction(page, 'QA', 'Collapse column')
      const strip = column(page, 'QA')
      await expect(strip.getByRole('button', { name: 'Expand QA', exact: true })).toBeVisible()
      await expect.poll(async () => (await strip.boundingBox())?.width).toBe(44)
      await expect(strip.getByRole('article')).toHaveCount(0)
      await axeBothThemes(page)
      await page.reload()
      await expect(board(page).getByRole('article').first()).toBeVisible()
      await expect(column(page, 'QA').getByRole('button', { name: 'Expand QA', exact: true })).toBeVisible()
      await expect.poll(async () => (await column(page, 'QA').boundingBox())?.width).toBe(44)
      expect((await statusOnServer(reviewId))?.color.toUpperCase()).toBe('#DB4C8B')
    })

    await test.step('"Add status" "Blocked" adds a column', async () => {
      await board(page).getByRole('button', { name: 'Add status', exact: true }).click()
      const name = board(page).getByRole('textbox', { name: 'Status name' })
      await expect(name).toBeFocused()
      await name.fill('Blocked')
      await name.press('Enter')
      await expect(columnNamed(page, 'Blocked', 0)).toBeVisible()
      await expect(name).toBeFocused()
      await expect(name).toHaveValue('')
      await name.press('Escape')
      await expect(board(page).getByRole('button', { name: 'Add status', exact: true })).toBeVisible()
      await expect.poll(async () => (await statusesOf(page, list.id)).some((s) => s.name === 'Blocked')).toBe(true)
    })

    await stageC('deleting Blocked asks no "Move its tasks to" question', async () => {
      await columnAction(page, 'Blocked', 'Delete status…')
      const dialog = page.getByRole('alertdialog', { name: 'Delete “Blocked”?' })
      await expect(dialog).toBeVisible()
      await expect(dialog).not.toContainText('Move its')
      await expect(dialog.getByRole('button', { name: /^Move its/ })).toHaveCount(0)
      await dialog.getByRole('button', { name: 'Delete status', exact: true }).click()
      await expect(dialog).toBeHidden()
      await expect(column(page, 'Blocked')).toHaveCount(0)
      await expect.poll(async () => (await statusesOf(page, list.id)).some((s) => s.name === 'Blocked')).toBe(false)
    })

    await stageC('deleting QA moves its 6 tasks to In progress', async () => {
      const progressBefore = (await cardTitles(column(page, 'In progress'))).length
      await column(page, 'QA').getByRole('button', { name: 'Expand QA', exact: true }).click()
      await columnAction(page, 'QA', 'Delete status…')
      const dialog = page.getByRole('alertdialog', { name: 'Delete “QA”?' })
      await expect(dialog).toBeVisible()
      await dialog.getByRole('button', { name: new RegExp(`^Move its ${inReview.length} tasks? to$`) }).click()
      await page.getByRole('option', { name: 'In progress', exact: true }).click()
      await dialog.getByRole('button', { name: 'Delete status', exact: true }).click()
      await expect(dialog).toBeHidden()
      await expect(column(page, 'QA')).toHaveCount(0)
      await expect(columnNamed(page, 'In progress', progressBefore + 6)).toBeVisible()
      await expect
        .poll(async () => {
          const tasks = await api(page).listTasks(list.id)
          return inReview.every((t) => tasks.find((x) => x.id === t.id)?.statusId === list.statusIds['In progress'])
        })
        .toBe(true)
      expect(await statusOnServer(reviewId)).toBeUndefined()
    })
  })
})

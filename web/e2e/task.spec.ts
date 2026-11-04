// The task detail (spec 11.1 scenarios 17–20): the panel in its modal and docked modes, J/K and Esc, Copy link
// and the full page; the description editor; attachments; comments and activity. Each screen is checked with axe
// in both themes.
import { MOD, api, dragTo, expect, expectNoAxeViolations, test } from './fixtures'

// Playwright's Page and Locator types, read off the fixtures' own signatures (scenario files import fixtures only).
type Page = Parameters<typeof api>[0]
type Locator = Parameters<typeof dragTo>[1]

const LIST = 'Website refresh'

/** The task panel: an aside named by the task title when docked, otherwise a dialog (8.10). */
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))

/** A List row by its task title. */
const listRow = (page: Page, title: string): Locator =>
  page.getByRole('treegrid', { name: `${LIST} tasks` }).getByRole('row', { name: title, exact: true })

const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** The toast holding `text` (a status, or an alert for errors). */
const toast = (page: Page, text: string): Locator =>
  notifications(page).locator('[role="status"], [role="alert"]').filter({ hasText: text })

/** True while focus is on the element or inside it. */
const holdsFocus = (locator: Locator): Promise<boolean> => locator.evaluate((el) => el.contains(document.activeElement))

/** Opens the List view of Website refresh and waits for its rows. */
async function gotoList(page: Page, listId: string): Promise<void> {
  await page.goto(`/l/${listId}/list`)
  await expect(listRow(page, 'Write onboarding guide')).toBeVisible()
}

/** Opens a task in the panel by clicking its row's title, and waits for the detail to load. */
async function openFromList(page: Page, title: string): Promise<Locator> {
  await listRow(page, title).getByText(title, { exact: true }).click()
  const panel = taskPanel(page, title)
  await expect(panel).toBeVisible()
  await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()
  return panel
}

/** axe in the light theme, then the dark one (the theme follows the system by default), then back to light. */
async function axeBothThemes(page: Page, include?: string): Promise<void> {
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

test.describe('Task detail', () => {
  test('17 the panel: modal at 1440, J/K, Esc, docked at 1920, Copy link and the full page', async ({ page, seeded }) => {
    const list = seeded.lists[LIST]
    const guide = seeded.tasks['Write onboarding guide']
    const next = seeded.tasks['Draft new homepage copy']
    await gotoList(page, list.id)

    await test.step('a row title opens a modal dialog named by the task, with a scrim and trapped focus', async () => {
      const before = await page.evaluate(() => history.length)
      const panel = await openFromList(page, guide.title)
      await expect(page.getByRole('dialog', { name: guide.title, exact: true })).toHaveAttribute('aria-modal', 'true')
      await expect(page.locator('#app')).toHaveAttribute('inert', '')
      await expect(page).toHaveURL(new RegExp(`[?&]task=${guide.id}`))
      expect(await page.evaluate(() => history.length)).toBe(before)
      expect(await holdsFocus(panel)).toBe(true)
      for (let i = 0; i < 4; i++) {
        await page.keyboard.press('Shift+Tab')
        expect(await holdsFocus(panel), 'Shift+Tab stays in the dialog').toBe(true)
      }
      await panel.locator('[data-task-panel-root]').focus()
      await axeBothThemes(page, '[role="dialog"]')
    })

    await test.step('J opens the next row, K the previous one', async () => {
      await page.keyboard.press('j')
      await expect(taskPanel(page, next.title)).toBeVisible()
      await expect(page).toHaveURL(new RegExp(`[?&]task=${next.id}`))
      await page.keyboard.press('k')
      await expect(taskPanel(page, guide.title)).toBeVisible()
      await expect(page).toHaveURL(new RegExp(`[?&]task=${guide.id}`))
    })

    await test.step('Esc closes the panel and focus returns to the row', async () => {
      await page.keyboard.press('Escape')
      await expect(page.getByRole('dialog')).toHaveCount(0)
      await expect(page).not.toHaveURL(/[?&]task=/)
      await expect.poll(() => holdsFocus(listRow(page, guide.title))).toBe(true)
    })

    await test.step('at 1920×1080 the panel is docked, and its row is current', async () => {
      await page.setViewportSize({ width: 1920, height: 1080 })
      const panel = await openFromList(page, guide.title)
      await expect(page.getByRole('complementary', { name: guide.title, exact: true })).toBeVisible()
      await expect(page.getByRole('dialog')).toHaveCount(0)
      await expect(page.locator('#app')).not.toHaveAttribute('inert', '')
      await expect(listRow(page, guide.title)).toHaveAttribute('aria-current', 'true')
      await expect(panel.getByRole('button', { name: 'Close task' })).toBeVisible()
      await axeBothThemes(page, '[data-task-panel-root]')
    })

    await test.step('clicking another row switches the panel and keeps focus on that row', async () => {
      await listRow(page, next.title).getByText(next.title, { exact: true }).click()
      await expect(page.getByRole('complementary', { name: next.title, exact: true })).toBeVisible()
      await expect(listRow(page, next.title)).toHaveAttribute('aria-current', 'true')
      expect(await holdsFocus(listRow(page, next.title))).toBe(true)
    })

    await test.step('"Copy link" puts the task URL on the clipboard', async () => {
      await taskPanel(page, next.title).getByRole('button', { name: 'Copy link' }).click()
      await expect(toast(page, 'Link copied')).toBeVisible()
      const copied = await page.evaluate(() => navigator.clipboard.readText())
      expect(copied).toMatch(new RegExp(`^https?://[^/]+/t/${next.id}$`))
    })

    await test.step('/t/<id> is the full page: the breadcrumb in the page header and one column', async () => {
      await page.goto(`/t/${next.id}`)
      const main = page.locator('#main')
      const crumbs = main.getByRole('navigation', { name: 'Breadcrumb' })
      await expect(crumbs).toBeVisible()
      await expect(crumbs.getByRole('link', { name: LIST })).toBeVisible()
      await expect(page.getByRole('heading', { level: 1, name: next.title })).toBeAttached()
      await expect(page.getByRole('complementary', { name: next.title })).toHaveCount(0)
      await expect(page.getByRole('dialog')).toHaveCount(0)
      const title = main.getByRole('textbox', { name: 'Task title' })
      const description = main.getByRole('textbox', { name: 'Description' })
      const activity = main.getByRole('region', { name: 'Activity' })
      const assignees = main.getByText('Assignees', { exact: true })
      await expect(title).toHaveValue(next.title)
      await expect(assignees).toBeVisible()
      await expect(description).toBeVisible()
      await expect(activity).toBeVisible()
      await expect(main.getByRole('textbox', { name: 'Write a comment' })).toBeVisible()
      // One column: the properties and the Activity share their left edge, at most 760px wide.
      const [p, a] = await Promise.all([assignees.boundingBox(), activity.boundingBox()])
      expect(p && a).toBeTruthy()
      expect(Math.abs(p!.x - a!.x)).toBeLessThanOrEqual(1)
      expect(a!.width).toBeLessThanOrEqual(760)
      // The header has the page's own actions, and no panel buttons.
      await expect(main.getByRole('button', { name: 'Copy link' })).toBeVisible()
      await expect(main.getByRole('button', { name: 'Open full page' })).toHaveCount(0)
      await axeBothThemes(page, '#main')
    })
  })

  test('18 rich text: markdown shortcuts, Add checklist, autosave, reload, and no HTML injection', async ({ page, seeded }) => {
    const task = seeded.tasks['Plan Q1 roadmap']
    const dialogs: string[] = []
    page.on('dialog', (d) => {
      dialogs.push(d.message())
      void d.dismiss()
    })
    await page.goto(`/t/${task.id}`)
    const main = page.locator('#main')
    const description = main.getByRole('textbox', { name: 'Description' })
    const saved = main.getByText('Saved', { exact: true })

    /** The H2, the bullet list and the checklist holding "check", in that order. */
    const expectStructure = async () => {
      await expect(description.getByRole('heading', { level: 2, name: 'Plan' })).toBeVisible()
      await expect(description.locator('ul:not([data-type="taskList"]) > li')).toHaveText(['item'])
      await expect(description.getByRole('checkbox', { name: 'check' })).toBeVisible()
      await expect(description.locator('ul[data-type="taskList"] > li > div')).toHaveText(['check'])
      expect(await description.evaluate((el) => Array.from(el.children, (c) => c.tagName.toLowerCase()).slice(0, 3))).toEqual([
        'h2',
        'ul',
        'ul',
      ])
    }

    await test.step('type "## Plan", "- item" and a checklist', async () => {
      await description.click()
      await page.keyboard.type('## Plan')
      await page.keyboard.press('Enter')
      await page.keyboard.type('- item')
      await page.keyboard.press('Enter')
      await page.keyboard.press('Enter')
      await main.getByRole('button', { name: 'Add checklist' }).click()
      await page.keyboard.type('check')
      await expectStructure()
      await expect(saved).toBeVisible()
      const saveDone = await api(page).task(task.id)
      expect(saveDone.description).toContain('<h2>Plan</h2>')
      expect(saveDone.description).toContain('data-type="taskList"')
    })

    await test.step('after a reload the structure is the same', async () => {
      await page.reload()
      await expectStructure()
      await expect(saved).toHaveCount(0)
      await axeBothThemes(page, '#main')
    })

    await test.step('typed HTML stays literal text', async () => {
      const injected = '<img src=x onerror=alert(1)>'
      await description.click()
      await page.keyboard.press(`${MOD}+End`)
      await page.keyboard.type(injected)
      await expect(description).toContainText(injected)
      await expect(description.locator('img')).toHaveCount(0)
      await expect(saved).toBeVisible()
      await page.reload()
      await expect(description).toContainText(injected)
      await expect(description.locator('img')).toHaveCount(0)
      expect((await api(page).task(task.id)).description).toContain('&lt;img src=x onerror=alert(1)&gt;')
      expect(dialogs).toEqual([])
    })
  })

  test('19 attachments: upload, lightbox, drop, delete with undo', async ({ page, seeded }) => {
    test.setTimeout(60_000)
    const list = seeded.lists[LIST]
    const task = seeded.tasks['Plan Q1 roadmap']
    await gotoList(page, list.id)
    const panel = await openFromList(page, task.title)
    const attachments = panel.getByRole('region', { name: 'Attachments' })

    await test.step('"Upload" a PNG: a thumbnail', async () => {
      // A 64×48 PNG drawn by the browser.
      const png = await page.evaluate(() => {
        const canvas = document.createElement('canvas')
        canvas.width = 64
        canvas.height = 48
        const g = canvas.getContext('2d')!
        g.fillStyle = 'rgb(61, 191, 174)'
        g.fillRect(0, 0, 64, 48)
        return canvas.toDataURL('image/png').split(',')[1]
      })
      const chooser = page.waitForEvent('filechooser')
      await attachments.getByRole('button', { name: 'Upload' }).click()
      await (await chooser).setFiles({ name: 'screenshot.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') })
      const thumb = attachments.getByRole('img', { name: 'screenshot.png' })
      await expect(thumb).toBeVisible()
      await expect.poll(() => thumb.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(64)
    })

    await test.step('the thumbnail opens the lightbox', async () => {
      await attachments.getByRole('button', { name: 'screenshot.png', exact: true }).click()
      const lightbox = page.getByRole('dialog', { name: 'screenshot.png' })
      await expect(lightbox).toBeVisible()
      const image = lightbox.getByRole('img', { name: 'screenshot.png' })
      await expect(image).toBeVisible()
      await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(64)
      await expect(lightbox.getByRole('link', { name: 'Download' })).toHaveAttribute('download', 'screenshot.png')
      await axeBothThemes(page, '[role="dialog"]')
      await page.keyboard.press('Escape')
      await expect(lightbox).toHaveCount(0)
      await expect(panel).toBeVisible()
    })

    await test.step('a PDF dropped on the panel uploads, with a toast and a tile with its size', async () => {
      const dataTransfer = await page.evaluateHandle(() => {
        const dt = new DataTransfer()
        dt.items.add(new File(['%PDF-1.4\n'.padEnd(2048, ' ')], 'report.pdf', { type: 'application/pdf' }))
        return dt
      })
      await panel.dispatchEvent('dragenter', { dataTransfer })
      await panel.dispatchEvent('dragover', { dataTransfer })
      await expect(panel.getByText('Drop files to upload')).toBeVisible()
      const uploaded = page.waitForResponse((r) => r.url().endsWith(`/api/tasks/${task.id}/attachments`) && r.ok())
      await panel.dispatchEvent('drop', { dataTransfer })
      await expect(panel.getByText('Drop files to upload')).toBeHidden()
      await expect(toast(page, 'report.pdf')).toBeVisible()
      await uploaded
      const tile = attachments.getByRole('link', { name: /report\.pdf/ })
      await expect(tile).toBeVisible()
      await expect(tile).toContainText('2 KB')
      await expect(tile).toHaveAttribute('download', 'report.pdf')
      expect(page.url()).toContain(`/l/${list.id}/list`)
    })

    await test.step('delete the PDF, then Undo: it returns', async () => {
      const tile = attachments.getByRole('listitem').filter({ hasText: 'report.pdf' })
      await tile.hover()
      await attachments.getByRole('button', { name: 'Delete report.pdf' }).click()
      await expect(tile).toHaveCount(0)
      await toast(page, 'Deleted “report.pdf”').getByRole('button', { name: 'Undo' }).click()
      await expect(tile).toBeVisible()
    })

    await test.step('delete it again; once it commits a reload leaves it gone', async () => {
      const tile = attachments.getByRole('listitem').filter({ hasText: 'report.pdf' })
      await tile.hover()
      const committed = page.waitForResponse((r) => r.request().method() === 'DELETE' && r.url().includes('/api/attachments/'), {
        timeout: 15_000,
      })
      await attachments.getByRole('button', { name: 'Delete report.pdf' }).click()
      await expect(tile).toHaveCount(0)
      expect((await committed).status()).toBe(204)
      await page.reload()
      const reloaded = taskPanel(page, task.title).getByRole('region', { name: 'Attachments' })
      await expect(reloaded.getByRole('img', { name: 'screenshot.png' })).toBeVisible()
      await expect(reloaded.getByText('report.pdf')).toHaveCount(0)
      const names = (await api(page).task(task.id)).attachments.map((a) => a.fileName)
      expect(names).toEqual(['screenshot.png'])
    })
  })

  test('20 comments and activity: send, new line, edit, delete with undo, live status change, filter', async ({
    page,
    seeded,
  }) => {
    const list = seeded.lists[LIST]
    const task = seeded.tasks['Plan Q1 roadmap']
    await gotoList(page, list.id)
    const panel = await openFromList(page, task.title)
    const feed = panel.getByRole('region', { name: 'Activity' })
    const composer = panel.getByRole('textbox', { name: 'Write a comment' })
    const commentBody = (text: string) => feed.getByRole('paragraph').filter({ hasText: new RegExp(`^${text}$`) })
    const comment = feed.getByRole('listitem').filter({ has: page.getByRole('paragraph').filter({ hasText: /^First!?$/ }) })

    await test.step('Enter sends the comment', async () => {
      await expect(panel.getByRole('button', { name: 'Send' })).toBeDisabled()
      await composer.fill('First')
      await expect(panel.getByRole('button', { name: 'Send' })).toBeEnabled()
      const posted = page.waitForResponse((r) => r.url().endsWith(`/api/tasks/${task.id}/comments`) && r.request().method() === 'POST')
      await composer.press('Enter')
      await expect(commentBody('First')).toBeVisible()
      await expect(composer).toHaveValue('')
      expect((await posted).ok()).toBe(true)
    })

    await test.step('Shift+Enter adds a new line', async () => {
      await composer.click()
      await page.keyboard.type('line one')
      await page.keyboard.press('Shift+Enter')
      await page.keyboard.type('line two')
      await expect(composer).toHaveValue('line one\nline two')
      await composer.fill('')
    })

    await test.step('edit the comment: "(edited)"', async () => {
      await comment.hover()
      await comment.getByRole('button', { name: 'Comment actions' }).click()
      await page.getByRole('menuitem', { name: 'Edit' }).click()
      const edit = feed.getByRole('textbox', { name: 'Edit comment' })
      await expect(edit).toBeFocused()
      await edit.fill('First!')
      const patched = page.waitForResponse((r) => r.url().includes('/api/comments/') && r.request().method() === 'PATCH')
      await edit.press('Enter')
      await expect(commentBody('First!')).toBeVisible()
      await expect(comment.getByText('(edited)')).toBeVisible()
      expect((await patched).ok()).toBe(true)
    })

    await test.step('delete it, then Undo: it returns', async () => {
      await comment.hover()
      await comment.getByRole('button', { name: 'Comment actions' }).click()
      await page.getByRole('menuitem', { name: 'Delete' }).click()
      await expect(commentBody('First!')).toHaveCount(0)
      await toast(page, 'Comment deleted').getByRole('button', { name: 'Undo' }).click()
      await expect(commentBody('First!')).toBeVisible()
      await expect(comment.getByText('(edited)')).toBeVisible()
    })

    await test.step('a status change shows in the feed within a second, without a reload', async () => {
      await panel.getByRole('button', { name: 'Status: To do, change' }).click()
      const patched = page.waitForResponse((r) => r.url().endsWith(`/api/tasks/${task.id}`) && r.request().method() === 'PATCH')
      await page.getByRole('option', { name: 'In progress' }).click()
      await patched
      await expect(feed.getByText('changed status from To do to In progress')).toBeVisible({ timeout: 1_000 })
      await expect(panel.getByRole('button', { name: 'Status: In progress, change' })).toBeVisible()
      await axeBothThemes(page, '[role="dialog"]')
    })

    await test.step('the Comments filter hides system entries, and it persists', async () => {
      await feed.getByRole('radio', { name: 'Comments' }).click()
      await expect(feed.getByRole('radio', { name: 'Comments' })).toBeChecked()
      await expect(feed.getByText('changed status from To do to In progress')).toHaveCount(0)
      await expect(feed.getByText('created this task')).toHaveCount(0)
      await expect(commentBody('First!')).toBeVisible()
      await page.reload()
      const reloaded = taskPanel(page, task.title).getByRole('region', { name: 'Activity' })
      await expect(reloaded.getByRole('radio', { name: 'Comments' })).toBeChecked()
      await expect(reloaded.getByRole('paragraph').filter({ hasText: /^First!$/ })).toBeVisible()
      await expect(reloaded.getByText('changed status from To do to In progress')).toHaveCount(0)
      await reloaded.getByRole('radio', { name: 'All' }).click()
      await expect(reloaded.getByText('changed status from To do to In progress')).toBeVisible()
    })
  })
})

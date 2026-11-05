// Settings, the hierarchy dialogs and the status editor (spec 11.1 scenarios 24, 25 and 26), plus two checks of the
// same screens that 11.1 leaves out: the role guards of the settings pages (8.13, 9.4) and deleting a status that
// tasks use. Every screen and dialog is checked with axe in both themes; saved values are checked through api(page)
// or after a reload.
import type { Locator, Page, Response } from '@playwright/test'
import { api, expect, expectNoAxeViolations, signIn, stageC, test, uniqueEmail, type Seeded, type Uuid } from './fixtures'

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })
const settingsNav = (page: Page): Locator => page.getByRole('navigation', { name: 'Settings' })
const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })
const membersTable = (page: Page): Locator => page.getByRole('table', { name: 'Members' })
const tagList = (page: Page): Locator => page.getByRole('list', { name: 'Tags' })

/** The Members row whose email cell shows `email`. */
const memberRow = (page: Page, email: string): Locator =>
  membersTable(page).getByRole('row').filter({ has: page.getByRole('cell', { name: email, exact: true }) })

/** A space in the sidebar tree: the item holding its button and its lists. */
const sidebarSpace = (page: Page, name: string): Locator =>
  sidebar(page).getByRole('listitem').filter({ has: page.getByRole('button', { name, exact: true }) })

/** The status editor of a list, and one of its sections. */
const statusEditor = (page: Page, list: string): Locator => page.getByRole('dialog', { name: `Statuses · ${list}` })
const section = (editor: Locator, name: 'Not started' | 'Active' | 'Done'): Locator => editor.getByRole('group', { name })

/** The next answer to `method` on a URL ending with `path` (a string) or matching it (a RegExp). */
function answer(page: Page, method: string, path: string | RegExp): Promise<Response> {
  return page.waitForResponse(
    (r) =>
      r.request().method() === method &&
      (typeof path === 'string' ? new URL(r.url()).pathname.endsWith(path) : path.test(new URL(r.url()).pathname)),
  )
}

/** Runs `act` and waits for the answer it causes; fails when the server refused it. */
async function saved(page: Page, method: string, path: string | RegExp, act: () => Promise<void>): Promise<void> {
  const [res] = await Promise.all([answer(page, method, path), act()])
  expect(res.ok(), `${method} ${new URL(res.url()).pathname} answered ${res.status()}`).toBe(true)
}

/** The status rows of an editor or a section, in order, named by their status. */
async function expectStatuses(scope: Locator, names: string[]): Promise<void> {
  const rows = scope.getByRole('listitem')
  await expect(rows).toHaveCount(names.length)
  for (const [i, name] of names.entries()) await expect(rows.nth(i)).toHaveAccessibleName(name)
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
async function axeBothThemes(page: Page, include?: string | string[]): Promise<void> {
  await clearToasts(page)
  await page.emulateMedia({ colorScheme: 'light' })
  await settled(page)
  await expectNoAxeViolations(page, { include })
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('dark')
  await settled(page)
  await expectNoAxeViolations(page, { include })
  await page.emulateMedia({ colorScheme: 'light' })
  await settled(page)
}

const DIALOGS = ['[role="dialog"]', '[role="alertdialog"]']

/** Opens a list's "..." menu in the sidebar (revealed on hover) and chooses an item. */
async function sidebarListMenu(page: Page, list: string, item: string): Promise<void> {
  await sidebar(page).getByRole('link', { name: list, exact: true }).hover()
  await sidebar(page).getByRole('button', { name: `More actions for ${list}` }).click()
  await page.getByRole('menu', { name: `Actions for ${list}` }).getByRole('menuitem', { name: item }).click()
}

/** Every task of every seeded list. */
async function allTasks(page: Page, seeded: Seeded) {
  const lists = Object.values(seeded.lists)
  return (await Promise.all(lists.map((l) => api(page).listTasks(l.id)))).flat()
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

test.describe('Settings', () => {
  test('24 members, tags and workspaces', async ({ page, seeded }) => {
    const wsId = seeded.workspaceId
    const members = () => api(page).members(wsId)
    const tags = () => api(page).tags(wsId)

    await page.goto('/settings/members')
    await expect(membersTable(page)).toBeVisible()
    await expect(memberRow(page, 'ana@example.com')).toContainText('Invited')
    await axeBothThemes(page)

    await test.step('invite two people as Member: both rows show Invited, and y is named Yan Li', async () => {
      await page.getByRole('button', { name: 'Invite people' }).click()
      const dialog = page.getByRole('dialog', { name: 'Invite people' })
      await dialog.getByRole('textbox', { name: 'Email addresses' }).fill('x@example.com; Yan Li <y@example.com>')
      await expect(dialog.getByText('2 people: x@example.com, Yan Li', { exact: true })).toBeVisible()
      await dialog.getByRole('radio', { name: 'Member' }).check()
      await expect(dialog.getByRole('radio', { name: 'Member' })).toBeChecked()
      await axeBothThemes(page, DIALOGS)
      await dialog.getByRole('button', { name: 'Add 2 people' }).click()
      await expect(dialog).toBeHidden()
      await expect(notifications(page)).toContainText('Added 2 people. They get access when they sign in with these addresses.')

      await expect(memberRow(page, 'x@example.com')).toContainText('Invited')
      await expect(memberRow(page, 'y@example.com')).toContainText('Invited')
      await expect(memberRow(page, 'y@example.com').getByRole('cell').first()).toHaveAccessibleName('Yan Li')
      const invited = (await members()).filter((m) => m.email === 'x@example.com' || m.email === 'y@example.com')
      expect(invited.map((m) => [m.email, m.role, m.pending]).sort()).toEqual([
        ['x@example.com', 'Member', true],
        ['y@example.com', 'Member', true],
      ])
    })

    await test.step('copy the sign-in link for Yan Li', async () => {
      await page.getByRole('button', { name: 'Copy sign-in link for Yan Li' }).click()
      await expect(notifications(page)).toContainText('Link copied')
      expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(new URL(page.url()).origin)
    })

    await test.step('your own row (the Owner) has no role select and no Remove', async () => {
      const mine = memberRow(page, seeded.email)
      await expect(mine).toContainText('(you)')
      await expect(mine).toContainText('Owner')
      await expect(mine.getByRole('button')).toHaveCount(0)
    })

    await test.step('change y to Guest, then remove x with the confirm', async () => {
      await saved(page, 'PATCH', /\/members\/[^/]+$/, async () => {
        await page.getByRole('button', { name: 'Role for Yan Li' }).click()
        await page.getByRole('option', { name: /^Guest/ }).click()
      })
      await expect(page.getByRole('button', { name: 'Role for Yan Li' })).toContainText('Guest')
      expect((await members()).find((m) => m.email === 'y@example.com')?.role).toBe('Guest')

      const x = (await members()).find((m) => m.email === 'x@example.com')
      expect(x).toBeDefined()
      await page.getByRole('button', { name: `Remove ${x!.displayName}` }).click()
      const confirm = page.getByRole('alertdialog', { name: `Remove ${x!.displayName}?` })
      await expect(confirm).toBeVisible()
      await saved(page, 'DELETE', `/members/${x!.userId}`, () => confirm.getByRole('button', { name: 'Remove' }).click())
      await expect(memberRow(page, 'x@example.com')).toHaveCount(0)
      expect((await members()).some((m) => m.email === 'x@example.com')).toBe(false)
    })

    await test.step('create the tag finance, rename it billing, recolour it, then delete it with the confirm', async () => {
      await settingsNav(page).getByRole('link', { name: 'Tags' }).click()
      await expect(tagList(page)).toBeVisible()
      await axeBothThemes(page)

      await page.getByRole('button', { name: 'New tag' }).click()
      const name = page.getByRole('textbox', { name: 'New tag name' })
      await name.fill('finance')
      await saved(page, 'POST', `/workspaces/${wsId}/tags`, () => name.press('Enter'))
      await expect(name).toHaveValue('')
      await name.press('Escape')
      await expect(name).toBeHidden()

      await tagList(page).getByRole('button', { name: 'finance', exact: true }).click()
      const rename = tagList(page).getByRole('textbox', { name: 'Tag name' })
      await rename.fill('billing')
      await saved(page, 'PATCH', /\/tags\/[^/]+$/, () => rename.press('Enter'))
      await expect(tagList(page).getByRole('button', { name: 'billing', exact: true })).toBeVisible()
      const billing = (await tags()).find((t) => t.name === 'billing')
      expect(billing).toBeDefined()
      expect((await tags()).some((t) => t.name === 'finance')).toBe(false)

      await page.getByRole('button', { name: 'Colour for billing' }).click()
      const swatches = page.getByRole('radiogroup', { name: 'Colour for billing' })
      await saved(page, 'PATCH', `/tags/${billing!.id}`, () => swatches.getByRole('radio', { checked: false }).first().click())
      await expect(swatches).toBeHidden()
      expect((await tags()).find((t) => t.id === billing!.id)?.color.toLowerCase()).not.toBe(billing!.color.toLowerCase())

      await page.getByRole('button', { name: 'Delete billing' }).click()
      const confirm = page.getByRole('alertdialog', { name: 'Delete tag “billing”?' })
      await saved(page, 'DELETE', `/tags/${billing!.id}`, () => confirm.getByRole('button', { name: 'Delete tag' }).click())
      await expect(tagList(page).getByRole('listitem').filter({ hasText: 'billing' })).toHaveCount(0)
      expect((await tags()).some((t) => t.id === billing!.id)).toBe(false)
    })

    await test.step('delete the seeded tag ops with the confirm: no task carries it any more', async () => {
      const ops = seeded.tags.ops
      expect((await allTasks(page, seeded)).some((t) => t.tagIds.includes(ops.id))).toBe(true)
      await page.getByRole('button', { name: 'Delete ops' }).click()
      const confirm = page.getByRole('alertdialog', { name: 'Delete tag “ops”?' })
      await saved(page, 'DELETE', `/tags/${ops.id}`, () => confirm.getByRole('button', { name: 'Delete tag' }).click())
      expect((await allTasks(page, seeded)).filter((t) => t.tagIds.includes(ops.id))).toEqual([])
      expect((await tags()).map((t) => t.name).sort()).toEqual(['bug', 'design', 'docs'])
    })

    await test.step('rename the workspace to Doe family in General', async () => {
      await settingsNav(page).getByRole('link', { name: 'General' }).click()
      const name = page.getByRole('textbox', { name: 'Workspace name' })
      await expect(page.getByRole('button', { name: 'Delete workspace' })).toBeVisible()
      await axeBothThemes(page)
      await name.fill('Doe family')
      await saved(page, 'PATCH', `/workspaces/${wsId}`, () => name.press('Enter'))
      await expect(sidebar(page).getByRole('button', { name: 'Workspace: Doe family' })).toBeVisible()
      expect((await api(page).workspaces()).find((w) => w.id === wsId)?.name).toBe('Doe family')
    })

    await test.step('create the workspace Scratch from the workspace menu: the app switches to it', async () => {
      await sidebar(page).getByRole('button', { name: 'Workspace: Doe family' }).click()
      await page.getByRole('menuitem', { name: 'Create workspace' }).click()
      const dialog = page.getByRole('dialog', { name: 'New workspace' })
      await dialog.getByRole('textbox', { name: 'Workspace name' }).fill('Scratch')
      await axeBothThemes(page, DIALOGS)
      await dialog.getByRole('button', { name: 'Create workspace' }).click()
      await expect(dialog).toBeHidden()
      await expect(sidebar(page).getByRole('button', { name: 'Workspace: Scratch' })).toBeVisible()
    })

    await test.step('delete Scratch in General, typing its name: the app switches back to Doe family', async () => {
      await page.goto('/settings/workspace')
      await expect(page.getByRole('textbox', { name: 'Workspace name' })).toHaveValue('Scratch')
      await page.getByRole('button', { name: 'Delete workspace' }).click()
      const confirm = page.getByRole('alertdialog', { name: 'Delete “Scratch”?' })
      const go = confirm.getByRole('button', { name: 'Delete workspace' })
      await expect(go).toBeDisabled()
      await confirm.getByRole('textbox').fill('Scratch')
      await axeBothThemes(page, DIALOGS)
      await go.click()
      await expect(sidebar(page).getByRole('button', { name: 'Workspace: Doe family' })).toBeVisible()
      expect((await api(page).workspaces()).map((w) => w.name)).not.toContain('Scratch')
    })
  })

  test('the settings pages follow the roles: Owner and self guards, read-only for guests', async ({ page, seeded }) => {
    const wsId = seeded.workspaceId
    await api(page).json('PATCH', `/api/workspaces/${wsId}`, { name: 'Doe family' })
    const admin = { email: uniqueEmail('settings-admin'), displayName: 'Ada Admin' }
    const guest = { email: uniqueEmail('settings-guest'), displayName: 'Gus Guest' }
    await api(page).json('POST', `/api/workspaces/${wsId}/members`, { ...admin, role: 'Admin' })
    await api(page).json('POST', `/api/workspaces/${wsId}/members`, { ...guest, role: 'Guest' })

    /** Opens a settings page in Doe family, switching to it with the workspace menu when needed. */
    const open = async (path: string) => {
      await page.goto(path)
      const button = sidebar(page).getByRole('button', { name: /^Workspace: / })
      await expect(button).toBeVisible()
      if ((await button.getAttribute('aria-label')) !== 'Workspace: Doe family') {
        await button.click()
        await page.getByRole('menuitemradio', { name: 'Doe family' }).click()
        await expect(sidebar(page).getByRole('button', { name: 'Workspace: Doe family' })).toBeVisible()
        await page.goto(path)
      }
    }

    await test.step('Owner on a phone: rows stack name and email, then role and Remove, without side scrolling', async () => {
      await page.setViewportSize({ width: 390, height: 844 })
      await page.goto('/settings/members')
      const row = memberRow(page, 'ana@example.com')
      await expect(row).toBeVisible()
      const name = await row.getByText('Ana Moreno', { exact: true }).boundingBox()
      const email = await row.getByText('ana@example.com', { exact: true }).boundingBox()
      const role = await row.getByRole('button', { name: 'Role for Ana Moreno' }).boundingBox()
      const remove = await row.getByRole('button', { name: 'Remove Ana Moreno' }).boundingBox()
      expect(name && email && role && remove).toBeTruthy()
      expect(email!.y).toBeGreaterThanOrEqual(name!.y + name!.height - 1)
      expect(role!.y).toBeGreaterThanOrEqual(email!.y + email!.height - 1)
      expect(Math.abs(role!.y + role!.height / 2 - (remove!.y + remove!.height / 2))).toBeLessThan(4)
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
      await axeBothThemes(page)
      await page.setViewportSize({ width: 1440, height: 900 })
    })

    await test.step('Admin: manages others, but not the Owner or themself, and cannot delete the workspace', async () => {
      await signIn(page, admin)
      await open('/settings/members')
      await expect(page.getByRole('button', { name: 'Invite people' })).toBeVisible()
      await expect(memberRow(page, seeded.email)).toContainText('Owner')
      await expect(memberRow(page, seeded.email).getByRole('button')).toHaveCount(0)
      await expect(memberRow(page, admin.email)).toContainText('(you)')
      await expect(memberRow(page, admin.email).getByRole('button')).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Role for Ana Moreno' })).toBeVisible()
      await expect(page.getByRole('button', { name: 'Remove Ana Moreno' })).toBeVisible()

      await open('/settings/workspace')
      await expect(page.getByRole('textbox', { name: 'Workspace name' })).toHaveValue('Doe family')
      await expect(page.getByRole('button', { name: 'Delete workspace' })).toHaveCount(0)

      await open('/settings/tags')
      await expect(page.getByRole('button', { name: 'Delete design' })).toBeVisible()
    })

    await test.step('Guest: every settings page is read-only', async () => {
      await signIn(page, guest)
      // Hidden, not disabled (9.4): a guest's settings pages hold no controls at all.
      const controls = (region: Locator) => region.locator('button, input, textarea, select, [tabindex="0"]')

      await open('/settings/members')
      await expect(memberRow(page, 'ana@example.com')).toContainText('Invited')
      await expect(controls(page.getByRole('region', { name: 'Members' }))).toHaveCount(0)

      await open('/settings/tags')
      await expect(tagList(page).getByRole('listitem')).toHaveCount(4)
      await expect(tagList(page)).toContainText('design')
      await expect(controls(page.getByRole('region', { name: 'Tags' }))).toHaveCount(0)

      await open('/settings/workspace')
      const general = page.getByRole('region', { name: 'General' })
      await expect(general).toContainText('Doe family')
      await expect(general).toContainText('Guest')
      await expect(controls(general)).toHaveCount(0)
      await expect(sidebar(page).getByRole('button', { name: 'New space' })).toHaveCount(0)
      await axeBothThemes(page)
    })
  })
})

test.describe('Hierarchy', () => {
  test('25 sidebar, new space and lists, rename, move and delete', async ({ page, seeded }) => {
    await page.goto('/')
    await expect(sidebar(page)).toBeVisible()

    await test.step('"[" hides the sidebar, it stays hidden after a reload, and "[" shows it again', async () => {
      await page.getByRole('main').focus()
      await page.keyboard.press('[')
      await expect(sidebar(page)).toBeHidden()
      await expect(page.getByRole('button', { name: 'Show sidebar' })).toBeVisible()
      await page.reload()
      await expect(page.getByRole('button', { name: 'Show sidebar' })).toBeVisible()
      await expect(sidebar(page)).toBeHidden()
      await page.getByRole('main').focus()
      await page.keyboard.press('[')
      await expect(sidebar(page)).toBeVisible()
      await expect(page.getByRole('button', { name: 'Show sidebar' })).toHaveCount(0)
    })

    await test.step('New space "Marketing" in Pink', async () => {
      await sidebar(page).getByRole('button', { name: 'New space' }).click()
      const dialog = page.getByRole('dialog', { name: 'New space' })
      await dialog.getByRole('textbox', { name: 'Name' }).fill('Marketing')
      await dialog.getByRole('radio', { name: 'Pink' }).click()
      await expect(dialog.getByRole('radio', { name: 'Pink' })).toBeChecked()
      await axeBothThemes(page, DIALOGS)
      await saved(page, 'POST', `/workspaces/${seeded.workspaceId}/spaces`, () =>
        dialog.getByRole('button', { name: 'Create space' }).click(),
      )
      await expect(dialog).toBeHidden()
      await expect(sidebar(page).getByRole('button', { name: 'Marketing', exact: true })).toBeVisible()
      const ws = (await api(page).workspaces()).find((w) => w.id === seeded.workspaceId)
      expect(ws?.spaces.find((s) => s.name === 'Marketing')?.color.toUpperCase()).toBe('#DB4C8B')
    })

    let q4: Uuid = ''
    await test.step('"New list in Marketing" "Q4 campaign" with Requests: it opens, with New … Done', async () => {
      await sidebar(page).getByRole('button', { name: 'New list in Marketing' }).click()
      const dialog = page.getByRole('dialog', { name: 'New list' })
      await dialog.getByRole('textbox', { name: 'Name' }).fill('Q4 campaign')
      await expect(dialog.getByRole('button', { name: 'Space' })).toHaveAccessibleDescription(/Marketing/)
      await dialog.getByRole('radio', { name: 'Requests' }).check()
      const requests = dialog.getByRole('radio', { name: 'Requests' })
      await expect(requests).toBeChecked()
      await expect(requests).toHaveAccessibleDescription('New · In progress · Waiting · Done')
      await axeBothThemes(page, DIALOGS)
      await dialog.getByRole('button', { name: 'Create list' }).click()
      await expect(dialog).toBeHidden()
      await expect(page).toHaveURL(/\/l\/[0-9a-f-]{36}\/list$/)
      q4 = /\/l\/([0-9a-f-]{36})\/list$/.exec(page.url())![1]
      await expect(sidebarSpace(page, 'Marketing').getByRole('link', { name: 'Q4 campaign' })).toBeVisible()

      await stageC('the add row of New is focused', async () => {
        await expect(page.getByRole('textbox', { name: 'Task name' })).toBeFocused()
      })

      await sidebarListMenu(page, 'Q4 campaign', 'Edit statuses')
      const editor = statusEditor(page, 'Q4 campaign')
      await expectStatuses(editor, ['New', 'In progress', 'Waiting', 'Done'])
      // The footer's Done closes the editor; it comes after the Done status, whose name is a button too.
      await editor.getByRole('button', { name: 'Done', exact: true }).last().click()
      await expect(editor).toBeHidden()
    })

    await test.step('"Copy test", copying the statuses of Website refresh', async () => {
      await sidebar(page).getByRole('button', { name: 'New list in Marketing' }).click()
      const dialog = page.getByRole('dialog', { name: 'New list' })
      await dialog.getByRole('textbox', { name: 'Name' }).fill('Copy test')
      await dialog.getByRole('radio', { name: 'Copy from another list' }).check()
      await dialog.getByRole('button', { name: /^List: / }).click()
      await page.getByRole('listbox', { name: 'Lists' }).getByRole('option', { name: 'Website refresh' }).click()
      await expect(dialog.getByRole('button', { name: 'List: Website refresh, change' })).toBeVisible()
      await expect(dialog.getByRole('radio', { name: 'Copy from another list' })).toHaveAccessibleDescription(
        'To do · In progress · Review · Done',
      )
      await dialog.getByRole('button', { name: 'Create list' }).click()
      await expect(dialog).toBeHidden()
      await expect(page).not.toHaveURL(new RegExp(`/l/${q4}/`))
      await sidebarListMenu(page, 'Copy test', 'Edit statuses')
      const editor = statusEditor(page, 'Copy test')
      await expectStatuses(editor, ['To do', 'In progress', 'Review', 'Done'])
      await page.keyboard.press('Escape')
      await expect(editor).toBeHidden()
    })

    await test.step('rename Q4 campaign to Q4 launch in the header, move it to Work, then delete it', async () => {
      await sidebar(page).getByRole('link', { name: 'Q4 campaign' }).click()
      await expect(page).toHaveURL(new RegExp(`/l/${q4}/list$`))
      const header = page.getByRole('main')
      await header.getByRole('heading', { level: 1 }).getByRole('button', { name: 'Q4 campaign' }).click()
      const name = header.getByRole('textbox', { name: 'List name' })
      await name.fill('Q4 launch')
      await saved(page, 'PATCH', `/lists/${q4}`, () => name.press('Enter'))
      await expect(header.getByRole('heading', { level: 1 })).toContainText('Q4 launch')
      await expect(sidebar(page).getByRole('link', { name: 'Q4 launch' })).toBeVisible()

      await header.getByRole('button', { name: 'More actions for Q4 launch' }).click()
      await page.getByRole('menuitem', { name: 'Move to space…' }).click()
      const dialog = page.getByRole('dialog', { name: 'Move “Q4 launch”' })
      await dialog.getByRole('button', { name: 'Space' }).click()
      await page.getByRole('option', { name: 'Work', exact: true }).click()
      await axeBothThemes(page, DIALOGS)
      await saved(page, 'PATCH', `/lists/${q4}`, () => dialog.getByRole('button', { name: 'Move' }).click())
      await expect(dialog).toBeHidden()
      await expect(sidebarSpace(page, 'Work').getByRole('link', { name: 'Q4 launch' })).toBeVisible()
      await expect(sidebarSpace(page, 'Marketing').getByRole('link', { name: 'Q4 launch' })).toHaveCount(0)
      const ws = (await api(page).workspaces()).find((w) => w.id === seeded.workspaceId)
      const work = ws?.spaces.find((s) => s.id === seeded.spaces.Work.id)
      expect(work?.lists.map((l) => l.name)).toContain('Q4 launch')

      await header.getByRole('button', { name: 'More actions for Q4 launch' }).click()
      await page.getByRole('menuitem', { name: 'Delete list' }).click()
      const confirm = page.getByRole('alertdialog', { name: 'Delete “Q4 launch”?' })
      await saved(page, 'DELETE', `/lists/${q4}`, () => confirm.getByRole('button', { name: 'Delete list' }).click())
      await expect(page).toHaveURL((url) => url.pathname === '/')
      await expect(sidebar(page).getByRole('link', { name: 'Q4 launch' })).toHaveCount(0)
    })
  })
})

test.describe('Status editor', () => {
  test('26 guards, adding, moving between sections and Alt+arrows', async ({ page, seeded }) => {
    const website = seeded.lists['Website refresh']
    await page.goto(`/l/${website.id}/list`)
    await expect(page.getByRole('heading', { level: 1, name: 'Website refresh' })).toBeVisible()

    await page.getByRole('main').getByRole('button', { name: 'More actions for Website refresh' }).click()
    await page.getByRole('menuitem', { name: 'Edit statuses' }).click()
    const editor = statusEditor(page, 'Website refresh')
    await expectStatuses(editor, ['To do', 'In progress', 'Review', 'Done'])
    await expectStatuses(section(editor, 'Not started'), ['To do'])
    await expectStatuses(section(editor, 'Active'), ['In progress', 'Review'])
    await expectStatuses(section(editor, 'Done'), ['Done'])

    await test.step('Done is the only Done status: its "Delete…" is disabled with the hint', async () => {
      await section(editor, 'Done').getByRole('button', { name: 'More actions for Done' }).click()
      const menu = page.getByRole('menu', { name: 'Actions for Done' })
      const del = menu.getByRole('menuitem', { name: 'Delete…' })
      await expect(del).toHaveAttribute('aria-disabled', 'true')
      await expect(del).toHaveAccessibleDescription('A list needs at least one Done status.')
      await expect(menu.getByRole('menuitem', { name: 'Move to Active' })).toHaveAttribute('aria-disabled', 'true')
      await page.keyboard.press('Escape')
      await expect(menu).toBeHidden()
      await expect(editor).toBeVisible()
    })

    await test.step('add Shipped to Done, then move Done to Active with its row menu', async () => {
      await editor.getByRole('button', { name: 'Add status to Done' }).click()
      const field = editor.getByRole('textbox', { name: 'New status in Done' })
      await field.fill('Shipped')
      await saved(page, 'POST', `/lists/${website.id}/statuses`, () => field.press('Enter'))
      await page.keyboard.press('Escape')
      await expect(field).toBeHidden()
      await expectStatuses(section(editor, 'Done'), ['Done', 'Shipped'])

      await section(editor, 'Done').getByRole('button', { name: 'More actions for Done' }).click()
      const move = page.getByRole('menu', { name: 'Actions for Done' }).getByRole('menuitem', { name: 'Move to Active' })
      await expect(move).not.toHaveAttribute('aria-disabled', 'true')
      await saved(page, 'PATCH', /\/statuses\/[^/]+$/, () => move.click())
      await expectStatuses(section(editor, 'Active'), ['In progress', 'Review', 'Done'])
      await expectStatuses(section(editor, 'Done'), ['Shipped'])
    })

    await test.step('Alt+ArrowUp on Review moves it above In progress and announces it', async () => {
      const review = section(editor, 'Active').getByRole('listitem', { name: 'Review' })
      await review.focus()
      await saved(page, 'PATCH', `/statuses/${website.statusIds.Review}`, () => page.keyboard.press('Alt+ArrowUp'))
      await expectStatuses(section(editor, 'Active'), ['Review', 'In progress', 'Done'])
      await expect(section(editor, 'Active').getByRole('listitem', { name: 'Review' })).toBeFocused()
      await expect(page.locator('[aria-live="polite"]').filter({ hasText: 'Review moved up, 1 of 3' })).toHaveCount(1)
      await axeBothThemes(page, DIALOGS)
    })

    await test.step('after a reload the editor shows the saved order', async () => {
      await page.reload()
      await expect(page.getByRole('heading', { level: 1, name: 'Website refresh' })).toBeVisible()
      await sidebarListMenu(page, 'Website refresh', 'Edit statuses')
      await expectStatuses(section(editor, 'Not started'), ['To do'])
      await expectStatuses(section(editor, 'Active'), ['Review', 'In progress', 'Done'])
      await expectStatuses(section(editor, 'Done'), ['Shipped'])
    })
  })

  test('deleting a status moves its tasks to the status you choose', async ({ page, seeded }) => {
    const website = seeded.lists['Website refresh']
    const { 'In progress': inProgress, Review: review } = website.statusIds
    const before = await api(page).listTasks(website.id)
    const moving = before.filter((t) => t.statusId === inProgress).map((t) => t.id)
    expect(moving.length).toBeGreaterThan(0)

    await page.goto(`/l/${website.id}/list`)
    await expect(page.getByRole('heading', { level: 1, name: 'Website refresh' })).toBeVisible()
    await sidebarListMenu(page, 'Website refresh', 'Edit statuses')
    const editor = statusEditor(page, 'Website refresh')
    const row = section(editor, 'Active').getByRole('listitem', { name: 'In progress' })
    await expect(row).toContainText(`${moving.length} tasks`)

    await row.getByRole('button', { name: 'More actions for In progress' }).click()
    await page.getByRole('menu', { name: 'Actions for In progress' }).getByRole('menuitem', { name: 'Delete…' }).click()
    const confirm = page.getByRole('alertdialog', { name: 'Delete “In progress”?' })
    await expect(confirm.getByRole('button', { name: 'Cancel' })).toBeFocused()
    await confirm.getByRole('button', { name: `Move its ${moving.length} tasks to` }).click()
    await page.getByRole('option', { name: 'Review', exact: true }).click()
    await expect(confirm.getByRole('button', { name: `Move its ${moving.length} tasks to` })).toHaveAccessibleDescription(/Review/)
    await axeBothThemes(page, DIALOGS)
    await saved(page, 'DELETE', `/statuses/${inProgress}`, () => confirm.getByRole('button', { name: 'Delete status' }).click())
    await expect(confirm).toBeHidden()
    await expectStatuses(section(editor, 'Active'), ['Review'])
    await expect(editor).toBeVisible()

    const after = await api(page).listTasks(website.id)
    expect(after.filter((t) => t.statusId === inProgress)).toEqual([])
    expect(after.filter((t) => moving.includes(t.id)).every((t) => t.statusId === review)).toBe(true)
  })
})

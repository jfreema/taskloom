// Look 2 (more visual personality): profile photos in every avatar and Settings › Profile, a list's icon and colour,
// the compact density, and runs of small changes folded in the activity feed. Each new screen is checked with axe.
import { MOD, api, expect, expectNoAxeViolations, signIn, test, uniqueEmail, type Me, type Member } from './fixtures'

type Page = Parameters<typeof api>[0]
type Locator = Awaited<ReturnType<Page['locator']>>

const LIST = 'Website refresh'

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })

/** The task panel: an aside named by the task title when docked, otherwise a dialog (8.10). */
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))

/** A picture the browser draws: `width`×`height`, half teal and half orange (left and right, or top and bottom), as PNG bytes. */
async function picture(page: Page, width: number, height: number, split: 'columns' | 'rows' = 'columns'): Promise<Buffer> {
  const base64 = await page.evaluate(
    ([w, h, rows]) => {
      const canvas = document.createElement('canvas')
      canvas.width = w
      canvas.height = h
      const ctx = canvas.getContext('2d')!
      ctx.fillStyle = '#14998A'
      ctx.fillRect(0, 0, w, h)
      ctx.fillStyle = '#D2661B'
      if (rows) ctx.fillRect(0, h / 2, w, h / 2)
      else ctx.fillRect(w / 2, 0, w / 2, h)
      return canvas.toDataURL('image/png').split(',')[1]
    },
    [width, height, split === 'rows'] as const,
  )
  return Buffer.from(base64, 'base64')
}

/** The size the browser decoded an avatar photo at, once it has loaded. */
const naturalSize = (img: Locator): Promise<[number, number]> =>
  img.evaluate(async (el: HTMLImageElement) => {
    await el.decode()
    return [el.naturalWidth, el.naturalHeight] as [number, number]
  })

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

test.describe('Look 2', () => {
  test('profile photo: upload a 256-pixel square, see it in the sidebar, change it, remove it', async ({ page, seeded }) => {
    await page.goto('/settings/profile')
    const main = page.getByRole('main')
    await expect(main.getByRole('heading', { name: 'Profile' })).toBeVisible()
    await expect(main.getByRole('button', { name: 'Upload photo' })).toBeVisible()
    await expect(main.getByRole('button', { name: 'Remove photo' })).toHaveCount(0)
    await expect(main.getByText('Until you choose one, Taskloom uses your Google or Microsoft photo')).toBeVisible()
    await expect(main.locator('.ui-avatar img')).toHaveCount(0)

    // A wide picture is cropped to its middle and sent as a 256-pixel JPEG.
    const upload = page.waitForResponse((r) => r.url().endsWith('/api/me/photo') && r.request().method() === 'POST')
    await page.getByTestId('photo-input').setInputFiles({ name: 'me.png', mimeType: 'image/png', buffer: await picture(page, 800, 400) })
    expect((await upload).ok()).toBe(true)
    const me = await api(page).me()
    expect(me.photoUrl).toMatch(new RegExp(`^/api/users/${seeded.me.id}/photo\\?v=[0-9a-f]{16}$`))
    expect(me.photoChosen).toBe(true)
    const photo = await api(page).get(me.photoUrl!)
    expect(photo.headers()['content-type']).toBe('image/jpeg')

    const profilePhoto = main.locator('.ui-avatar img')
    await expect(profilePhoto).toHaveAttribute('src', me.photoUrl!)
    expect(await naturalSize(profilePhoto)).toEqual([256, 256])
    await expect(main.getByRole('button', { name: 'Change photo' })).toBeVisible()
    await expect(main.getByText('Shown with your name on tasks, comments and in the sidebar.', { exact: true })).toBeVisible()
    // The sidebar's account button shows it too, without a reload.
    await expect(page.getByRole('button', { name: /^Account:/ }).locator('img')).toHaveAttribute('src', me.photoUrl!)
    await expectNoAxeViolations(page)

    // Something that isn't a picture is refused here, and the photo stays.
    await page.getByTestId('photo-input').setInputFiles({ name: 'notes.png', mimeType: 'image/png', buffer: Buffer.from('not a picture') })
    await expect(main.getByRole('alert')).toHaveText("That file isn't a picture Taskloom can read. Try a JPEG or PNG.")
    expect((await api(page).me()).photoUrl).toBe(me.photoUrl)

    // A new picture gets a new address; Remove goes back to the initials, also after a reload.
    await page.getByTestId('photo-input').setInputFiles({ name: 'tall.png', mimeType: 'image/png', buffer: await picture(page, 300, 900, 'rows') })
    await expect.poll(async () => (await api(page).me()).photoUrl).not.toBe(me.photoUrl)
    await expect(main.getByRole('alert')).toHaveCount(0)
    await main.getByRole('button', { name: 'Remove photo' }).click()
    await expect(main.locator('.ui-avatar img')).toHaveCount(0)
    await expect(main.getByRole('button', { name: 'Upload photo' })).toBeVisible()
    expect(await api(page).me()).toMatchObject({ photoUrl: null, photoChosen: true })
    await page.reload()
    await expect(page.getByRole('button', { name: /^Account:/ })).toBeVisible()
    await expect(page.getByRole('button', { name: /^Account:/ }).locator('img')).toHaveCount(0)
  })

  test("other people's photos show on their avatars wherever they appear", async ({ page, seeded, browser }) => {
    // A new teammate signs in elsewhere and uploads a photo.
    const email = uniqueEmail('look-teammate')
    const member = await api(page).json<Member>('POST', `/api/workspaces/${seeded.workspaceId}/members`, {
      email,
      role: 'Member',
      displayName: 'Pia Photo',
    })
    const other = await browser.newContext()
    const theirs = await other.newPage()
    await signIn(theirs, { email, displayName: 'Pia Photo' })
    await theirs.goto('/settings/profile')
    const res = await theirs.request.post('/api/me/photo', {
      headers: { 'X-Taskloom': '1' },
      multipart: { file: { name: 'pia.png', mimeType: 'image/png', buffer: await picture(theirs, 256, 256) } },
    })
    expect(res.ok()).toBe(true)
    const piaPhoto = ((await res.json()) as Me).photoUrl!
    await other.close()

    const task = seeded.tasks['Write onboarding guide']
    await api(page).patchTask(task.id, { assigneeIds: [...task.assigneeIds, member.userId] })
    await page.goto(`/l/${seeded.lists[LIST].id}/list`)
    const row = page.getByRole('treegrid', { name: `${LIST} tasks` }).getByRole('row', { name: task.title, exact: true })
    await expect(row.locator(`img[src="${piaPhoto}"]`)).toBeVisible()
    // Without a photo, initials as before.
    await expect(row.locator('.ui-avatar').first().locator('.initials')).toBeVisible()

    // In the task panel's assignees too, and the browser fetches the photo once and then keeps it.
    await row.getByText(task.title, { exact: true }).click()
    const panel = taskPanel(page, task.title)
    await expect(panel.locator(`img[src="${piaPhoto}"]`).first()).toBeVisible()
    const cached = await page.request.get(piaPhoto)
    expect(cached.headers()['cache-control']).toContain('immutable')
  })

  test('list icon and colour: pick them in the dialog, see them in the sidebar, header and palette, then go plain', async ({
    page,
    seeded,
  }) => {
    const list = seeded.lists['Admin']
    await page.goto(`/l/${list.id}/list`)
    const row = sidebar(page).locator(`[data-list-id="${list.id}"]`)
    await expect(row.locator('[data-list-glyph]')).toHaveAttribute('data-icon', 'list')

    await row.hover()
    await sidebar(page).getByRole('button', { name: 'More actions for Admin' }).click()
    await page.getByRole('menuitem', { name: 'Icon and colour…' }).click()
    const dialog = page.getByRole('dialog', { name: 'Icon and colour for “Admin”' })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('radiogroup', { name: 'Icon' }).getByRole('radio', { name: 'List', exact: true })).toHaveAttribute('aria-checked', 'true')
    await expect(dialog.getByRole('button', { name: 'Use plain icon' })).toBeDisabled()

    await dialog.getByRole('radiogroup', { name: 'Colour' }).getByRole('radio', { name: 'Pink' }).click()
    const icons = dialog.getByRole('radiogroup', { name: 'Icon' })
    // The keyboard moves through the grid: Right, then Down a row of seven.
    await icons.getByRole('radio', { name: 'List', exact: true }).focus()
    await page.keyboard.press('ArrowRight')
    await expect(icons.getByRole('radio', { name: 'Checklist' })).toBeFocused()
    await page.keyboard.press('ArrowDown')
    await expect(icons.getByRole('radio', { name: 'Bookmark' })).toBeFocused()
    await page.keyboard.press('Space')
    await expect(icons.getByRole('radio', { name: 'Bookmark' })).toHaveAttribute('aria-checked', 'true')
    await icons.getByRole('radio', { name: 'Rocket' }).click()
    await expect(dialog.locator('.preview [data-list-glyph]')).toHaveAttribute('data-icon', 'rocket')
    await expectNoAxeViolations(page, { include: '[role="dialog"]' })

    const saved = page.waitForResponse((r) => r.url().endsWith(`/api/lists/${list.id}`) && r.request().method() === 'PATCH')
    await dialog.getByRole('button', { name: 'Save' }).click()
    expect((await saved).ok()).toBe(true)
    await expect(dialog).toHaveCount(0)
    const glyph = row.locator('[data-list-glyph]')
    await expect(glyph).toHaveAttribute('data-icon', 'rocket')
    await expect(glyph).toHaveCSS('color', 'rgb(219, 76, 139)')
    const stored = (await api(page).workspaces())[0].spaces.flatMap((s) => s.lists).find((l) => l.id === list.id)
    expect(stored).toMatchObject({ color: '#DB4C8B', icon: 'rocket' })

    // The page header and the palette show it, also after a reload.
    await page.reload()
    await expect(page.locator('header [data-list-glyph]').first()).toHaveAttribute('data-icon', 'rocket')
    await page.keyboard.press(`${MOD}+K`)
    await page.getByRole('combobox').fill('Admin')
    await expect(page.getByRole('option', { name: /^Admin/ }).locator('[data-list-glyph]')).toHaveAttribute('data-icon', 'rocket')
    await page.keyboard.press('Escape')

    // "Use plain icon" puts the grey list icon back.
    await row.hover()
    await sidebar(page).getByRole('button', { name: 'More actions for Admin' }).click()
    await page.getByRole('menuitem', { name: 'Icon and colour…' }).click()
    await dialog.getByRole('button', { name: 'Use plain icon' }).click()
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect(glyph).toHaveAttribute('data-icon', 'list')
    await expect.poll(async () => (await api(page).workspaces())[0].spaces.flatMap((s) => s.lists).find((l) => l.id === list.id))
      .toMatchObject({ color: null, icon: null })
  })

  test('guests can see list icons but not change them', async ({ page, seeded, browser }) => {
    const list = seeded.lists['Admin']
    await api(page).json('PATCH', `/api/lists/${list.id}`, { icon: 'star', color: '#B07A0A' })
    const email = uniqueEmail('look-guest')
    await api(page).json('POST', `/api/workspaces/${seeded.workspaceId}/members`, { email, role: 'Guest' })
    const context = await browser.newContext()
    const guest = await context.newPage()
    await signIn(guest, { email })
    await guest.goto(`/l/${list.id}/list`)
    const row = sidebar(guest).locator(`[data-list-id="${list.id}"]`)
    await expect(row.locator('[data-list-glyph]')).toHaveAttribute('data-icon', 'star')
    await row.hover()
    await sidebar(guest).getByRole('button', { name: 'More actions for Admin' }).click()
    await expect(guest.getByRole('menuitem', { name: 'Copy link' })).toBeVisible()
    await expect(guest.getByRole('menuitem', { name: 'Icon and colour…' })).toHaveCount(0)
    await context.close()
  })

  test('compact density tightens list and sidebar rows, and is kept after a reload', async ({ page, seeded }) => {
    const rowHeight = async (): Promise<number> =>
      (await page.getByRole('treegrid', { name: `${LIST} tasks` }).getByRole('row', { name: 'Write onboarding guide', exact: true }).boundingBox())!
        .height

    await page.goto(`/l/${seeded.lists[LIST].id}/list`)
    await expect(page.getByRole('treegrid')).not.toHaveAttribute('aria-busy', 'true')
    expect(await rowHeight()).toBe(36)

    await page.goto('/settings/appearance')
    const density = page.getByRole('radiogroup', { name: 'Density' })
    await expect(density.getByRole('radio', { name: /Comfortable/ })).toBeChecked()
    await expectNoAxeViolations(page)
    await density.getByRole('radio', { name: /Compact/ }).check()
    await expect(page.locator('html')).toHaveAttribute('data-density', 'compact')
    expect(await page.evaluate(() => localStorage.getItem('taskloom.density'))).toBe('compact')

    await page.goto(`/l/${seeded.lists[LIST].id}/list`)
    await expect(page.getByRole('treegrid')).not.toHaveAttribute('aria-busy', 'true')
    expect(await rowHeight()).toBe(30)
    expect((await sidebar(page).locator(`[data-list-id="${seeded.lists[LIST].id}"]`).boundingBox())!.height).toBeLessThanOrEqual(26)
    // Set before the app starts, so a reload never shows the comfortable rows first.
    await page.reload()
    expect(await page.evaluate(() => document.documentElement.getAttribute('data-density'))).toBe('compact')
    await expect(page.getByRole('treegrid')).not.toHaveAttribute('aria-busy', 'true')
    await expectNoAxeViolations(page)

    await page.goto('/settings/appearance')
    await page.getByRole('radiogroup', { name: 'Density' }).getByRole('radio', { name: /Comfortable/ }).check()
    await expect(page.locator('html')).not.toHaveAttribute('data-density', /.*/)
  })

  test('compact density keeps 44px rows on touch screens', { tag: '@phone' }, async ({ page, seeded }) => {
    await page.addInitScript(() => localStorage.setItem('taskloom.density', 'compact'))
    await page.goto(`/l/${seeded.lists[LIST].id}/list`)
    await expect(page.locator('html')).toHaveAttribute('data-density', 'compact')
    const row = page.getByRole('treegrid', { name: `${LIST} tasks` }).getByRole('row', { name: 'Write onboarding guide', exact: true })
    await expect(row).toBeVisible()
    expect((await row.boundingBox())!.height).toBeGreaterThanOrEqual(44)
  })

  test('activity: a run of small changes folds into one line that opens to the single changes', async ({ page, seeded }) => {
    const listId = seeded.lists[LIST].id
    const task = await api(page).json<{ id: string; title: string }>('POST', `/api/lists/${listId}/tasks`, { title: 'Fold my changes' })
    // Four changes in a row by one person, a comment, then two more.
    await api(page).patchTask(task.id, { priority: 'High' })
    await api(page).patchTask(task.id, { dueDate: '2026-12-01' })
    await api(page).patchTask(task.id, { statusId: seeded.lists[LIST].statusIds['In progress'] })
    await api(page).patchTask(task.id, { priority: 'Urgent' })
    await api(page).json('POST', `/api/tasks/${task.id}/comments`, { body: '<p>Halfway there</p>' })
    await api(page).patchTask(task.id, { title: 'Fold my changes, please' })
    await api(page).patchTask(task.id, { priority: 'Low' })

    await page.goto(`/t/${task.id}`)
    const feed = page.getByRole('region', { name: 'Activity' })
    const run = feed.locator('[data-activity-run]')
    await expect(run).toHaveCount(1)
    await expect(run).toContainText('Jared Doe updated the priority, due date and status')
    const toggle = run.getByRole('button', { name: 'Show 4 changes' })
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await expect(feed.getByText('set priority to High')).toHaveCount(0)
    // After the comment, two changes stay as two lines.
    await expect(feed.getByText('renamed this from “Fold my changes” to “Fold my changes, please”')).toBeVisible()
    await expect(feed.getByText('set priority to Low')).toBeVisible()

    await toggle.click()
    await expect(run.getByRole('button', { name: 'Hide 4 changes' })).toHaveAttribute('aria-expanded', 'true')
    const items = run.getByRole('listitem')
    await expect(items).toHaveCount(4)
    await expect(items.nth(0)).toContainText('set priority to High')
    await expect(items.nth(1)).toContainText('set the due date to')
    await expect(items.nth(2)).toContainText('changed status from To do to In progress')
    await expect(items.nth(3)).toContainText('set priority to Urgent')
    await expectNoAxeViolations(page, { include: '[aria-label="Activity"], section:has(> .header)' })

    // The Comments filter shows only the comment, as before.
    await feed.getByRole('radio', { name: 'Comments' }).click()
    await expect(feed.locator('[data-activity-run]')).toHaveCount(0)
    await expect(feed.getByText('Halfway there')).toBeVisible()
    await feed.getByRole('radio', { name: 'All' }).click()
  })
})

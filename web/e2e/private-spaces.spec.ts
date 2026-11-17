// Private spaces (Security 3): "Share space…" makes a space private to the people you tick and back, a private space
// shows a lock in the sidebar, assignee pickers in it offer only its people, New space can start private, and
// Settings › General lists every private space for admins, with Join. Dialogs are checked with axe in both themes;
// saved values are checked through api(page).
import type { Locator, Page, Response } from '@playwright/test'
import { api, expect, expectNoAxeViolations, test, type Space, type Uuid } from './fixtures'

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })
const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** Runs `act` and waits for the answer it causes on a path ending with `path`; fails when the server refused it. */
async function saved(page: Page, method: string, path: string, act: () => Promise<void>): Promise<void> {
  const [res] = await Promise.all([
    page.waitForResponse((r: Response) => r.request().method() === method && new URL(r.url()).pathname.endsWith(path)),
    act(),
  ])
  expect(res.ok(), `${method} ${new URL(res.url()).pathname} answered ${res.status()}`).toBe(true)
}

/** axe in the light theme, then the dark one, then back to light, once no transition or toast is running. */
async function axeBothThemes(page: Page, include: string[]): Promise<void> {
  for (const dismiss of await notifications(page).getByRole('button', { name: 'Dismiss' }).all()) {
    await dismiss.click({ timeout: 2000 }).catch(() => undefined)
  }
  const settled = () =>
    page.waitForFunction(() => document.getAnimations().every((a) => !(a instanceof CSSTransition) || a.playState !== 'running'))
  for (const [i, scheme] of (['light', 'dark', 'light'] as const).entries()) {
    await page.emulateMedia({ colorScheme: scheme })
    await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe(scheme)
    await settled()
    if (i < 2) await expectNoAxeViolations(page, { include })
  }
}

const DIALOGS = ['[role="dialog"]', '[role="alertdialog"]']

/** Opens a space's "..." menu in the sidebar (revealed on hover) and chooses an item. */
async function spaceMenu(page: Page, space: string, item: string): Promise<void> {
  await sidebar(page).getByRole('button', { name: new RegExp(`^${space}(, private)?$`) }).hover()
  await sidebar(page).getByRole('button', { name: `More actions for ${space}` }).click()
  await page.getByRole('menu', { name: `Actions for ${space}` }).getByRole('menuitem', { name: item }).click()
}

async function spaceById(page: Page, workspaceId: Uuid, spaceId: Uuid): Promise<Space | undefined> {
  const ws = (await api(page).workspaces()).find((w) => w.id === workspaceId)
  return ws?.spaces.find((s) => s.id === spaceId)
}

test.describe('private spaces', () => {
  test('share a space with chosen people, assign only them, and show it to everyone again', async ({ page, seeded }) => {
    const work = seeded.spaces.Work
    const ana = seeded.members['Ana Moreno']
    const task = seeded.tasks['Plan Q1 roadmap']
    await page.goto('/')
    await expect(sidebar(page)).toBeVisible()

    const dialog = page.getByRole('dialog', { name: 'Share “Work”' })
    await test.step('Share space… opens with Everyone chosen', async () => {
      await spaceMenu(page, 'Work', 'Share space…')
      await expect(dialog).toBeVisible()
      await expect(dialog.getByRole('radio', { name: 'Everyone' })).toBeChecked()
      await expect(dialog.getByRole('listbox')).toHaveCount(0)
      await axeBothThemes(page, DIALOGS)
    })

    await test.step('Private: only I am in it; ticking Ana adds her', async () => {
      await saved(page, 'PATCH', `/api/spaces/${work.id}`, () => dialog.getByRole('radio', { name: 'Private' }).check())
      const people = dialog.getByRole('listbox', { name: 'People who can see this space' })
      await expect(people).toBeVisible()
      await expect(dialog.getByText('1 person can see this space.', { exact: false })).toBeVisible()
      await expect(people.getByRole('option', { name: /Jared Doe/ })).toHaveAttribute('aria-selected', 'true')
      await expect(people.getByRole('option', { name: /Ana Moreno/ })).toHaveAttribute('aria-selected', 'false')
      await saved(page, 'POST', `/api/spaces/${work.id}/members`, () => people.getByRole('option', { name: /Ana Moreno/ }).click())
      await expect(people.getByRole('option', { name: /Ana Moreno/ })).toHaveAttribute('aria-selected', 'true')
      const space = await spaceById(page, seeded.workspaceId, work.id)
      expect(space?.isPrivate).toBe(true)
      expect(space?.memberIds).toEqual([seeded.me.id, ana.userId])
      await axeBothThemes(page, DIALOGS)
      await dialog.getByRole('button', { name: 'Done' }).click()
      await expect(dialog).toBeHidden()
      await expect(sidebar(page).getByRole('button', { name: 'Work, private', exact: true })).toBeVisible()
    })

    await test.step("a task in Work offers only the space's people as assignees", async () => {
      await page.goto(`/t/${task.id}`)
      const main = page.locator('#main')
      await main.getByRole('button', { name: /^Assignees:/ }).click()
      const options = page.getByRole('listbox', { name: 'Assignees' })
      await expect(options.getByRole('option', { name: /Ana Moreno/ })).toBeVisible()
      await expect(options.getByRole('option', { name: /Sam Lee/ })).toHaveCount(0)
      await page.keyboard.press('Escape')
    })

    await test.step('Everyone again, after the confirm: its people are cleared', async () => {
      await page.goto('/')
      await spaceMenu(page, 'Work', 'Share space…')
      await dialog.getByRole('radio', { name: 'Everyone' }).check()
      const confirm = page.getByRole('alertdialog', { name: 'Show “Work” to everyone?' })
      await expect(confirm).toBeVisible()
      await saved(page, 'PATCH', `/api/spaces/${work.id}`, () => confirm.getByRole('button', { name: 'Show to everyone' }).click())
      await expect(dialog.getByRole('radio', { name: 'Everyone' })).toBeChecked()
      await dialog.getByRole('button', { name: 'Done' }).click()
      await expect(sidebar(page).getByRole('button', { name: 'Work', exact: true })).toBeVisible()
      const space = await spaceById(page, seeded.workspaceId, work.id)
      expect(space?.isPrivate).toBe(false)
      expect(space?.memberIds).toEqual([])
    })
  })

  test('a new private space, and joining one from Settings › General', async ({ page, seeded }) => {
    const ana = seeded.members['Ana Moreno']
    await page.goto('/')
    await expect(sidebar(page)).toBeVisible()

    await test.step('New space "Finance", Private', async () => {
      await sidebar(page).getByRole('button', { name: 'New space' }).click()
      const dialog = page.getByRole('dialog', { name: 'New space' })
      await dialog.getByRole('textbox', { name: 'Name' }).fill('Finance')
      await dialog.getByRole('switch', { name: 'Private' }).click()
      await expect(dialog.getByRole('switch', { name: 'Private' })).toBeChecked()
      await axeBothThemes(page, DIALOGS)
      await saved(page, 'POST', `/workspaces/${seeded.workspaceId}/spaces`, () => dialog.getByRole('button', { name: 'Create space' }).click())
      // The workspace had lists, so New list doesn't follow.
      await expect(dialog).toBeHidden()
      await expect(sidebar(page).getByRole('button', { name: 'Finance, private', exact: true })).toBeVisible()
      const ws = (await api(page).workspaces()).find((w) => w.id === seeded.workspaceId)
      const finance = ws?.spaces.find((s) => s.name === 'Finance')
      expect(finance?.isPrivate).toBe(true)
      expect(finance?.memberIds).toEqual([seeded.me.id])
    })

    let hidden: Uuid = ''
    await test.step('a private space I am not in is listed in Settings › General, and Join brings it into the sidebar', async () => {
      // Made through the API: a private space with Ana, which I then leave.
      const created = await api(page).json<Space>('POST', `/api/workspaces/${seeded.workspaceId}/spaces`, {
        name: 'Board minutes',
        isPrivate: true,
        memberIds: [ana.userId],
      })
      hidden = created.id
      await api(page).json('DELETE', `/api/spaces/${hidden}/members/${seeded.me.id}`)
      await page.goto('/settings/workspace')
      const section = page.getByRole('region', { name: 'Private spaces' })
      await expect(section).toBeVisible()
      const row = section.getByRole('listitem').filter({ hasText: 'Board minutes' })
      await expect(row).toContainText('1 person')
      await expect(section.getByRole('listitem').filter({ hasText: 'Finance' })).toContainText("You're in it")
      await expect(sidebar(page).getByRole('button', { name: 'Board minutes, private' })).toHaveCount(0)
      await axeBothThemes(page, ['#main'])
      await saved(page, 'POST', `/api/spaces/${hidden}/join`, () => row.getByRole('button', { name: 'Join Board minutes' }).click())
      await expect(row).toContainText("You're in it")
      await expect(row).toContainText('2 people')
      await expect(sidebar(page).getByRole('button', { name: 'Board minutes, private', exact: true })).toBeVisible()
      expect((await spaceById(page, seeded.workspaceId, hidden))?.memberIds).toEqual([ana.userId, seeded.me.id])
    })
  })
})

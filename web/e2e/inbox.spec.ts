// The Inbox, @mentions and notification settings (Features 2). The seed leaves three unread rows in the Inbox
// (Ana's comment, a "Due today" reminder and an "Overdue" one); a teammate invited by the test and signed in in a
// second browser context adds an assignment and a mention. Checked here: the sidebar's unread count, the rows' wording and order, reading by
// opening a task, "Mark as unread", "Mark all as read", the Unread and All views, the @ list in the comment box
// (keys, who is offered, what is saved and who is told), and Settings › Notifications. Screens are checked with axe
// in both themes; saved values through api(page). The server runs with Jobs__Enabled=false, so no reminder arrives
// in the middle of a test.
import path from 'node:path'
import type { Browser, Locator, Page, Response } from '@playwright/test'
import { api, expect, expectNoAxeViolations, signIn, test, uniqueEmail, type Api, type InboxPage, type Seeded, type Uuid } from './fixtures'

const LIST = 'Website refresh'
const THEMES = ['light', 'dark'] as const

const sidebar = (page: Page): Locator => page.getByRole('navigation', { name: 'Sidebar' })
const inboxLink = (page: Page): Locator => sidebar(page).getByRole('link', { name: /^Inbox/ })
const badge = (page: Page): Locator => sidebar(page).getByTestId('inbox-unread')
const main = (page: Page): Locator => page.getByRole('main')
/** Every Inbox row's button, in the order shown; its name is the row in one sentence. */
const rowOpeners = (page: Page): Locator => main(page).locator('[data-inbox-row] > button')
const inboxRow = (page: Page, text: string | RegExp): Locator => main(page).locator('[data-inbox-row]').filter({ hasText: text })
const taskPanel = (page: Page, title: string): Locator =>
  page.getByRole('complementary', { name: title, exact: true }).or(page.getByRole('dialog', { name: title, exact: true }))
const mentionList = (page: Page): Locator => page.getByRole('listbox', { name: 'People to mention' })
const notifications = (page: Page): Locator => page.getByRole('region', { name: 'Notifications' })

/** Runs `act` and waits for the answer it causes on a path ending with `pathEnd`; fails when the server refused it. */
async function saved(page: Page, method: string, pathEnd: string, act: () => Promise<unknown>): Promise<void> {
  const [res] = await Promise.all([
    page.waitForResponse((r: Response) => r.request().method() === method && new URL(r.url()).pathname.endsWith(pathEnd)),
    act(),
  ])
  expect(res.ok(), `${method} ${new URL(res.url()).pathname} answered ${res.status()}`).toBe(true)
}

async function setTheme(page: Page, theme: (typeof THEMES)[number]): Promise<void> {
  await page.emulateMedia({ colorScheme: theme, reducedMotion: 'reduce' })
  await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe(theme)
  await page.waitForFunction(() => document.getAnimations().every((a) => !(a instanceof CSSTransition) || a.playState !== 'running'))
}

/** axe in the light theme, then the dark one, then back to light, with no toast fading out. */
async function axeBothThemes(page: Page, include?: string | string[]): Promise<void> {
  for (const dismiss of await notifications(page).getByRole('button', { name: 'Dismiss' }).all()) {
    await dismiss.click({ timeout: 2000 }).catch(() => undefined)
  }
  for (const theme of THEMES) {
    await setTheme(page, theme)
    await expectNoAxeViolations(page, { include })
  }
  await setTheme(page, 'light')
}

/** Saves web/e2e/screenshots/<screen>-<theme>-<width>.png in both themes (the review screenshots, see platform.spec.ts). */
async function shots(page: Page, screen: string): Promise<void> {
  for (const theme of THEMES) {
    await setTheme(page, theme)
    await page.mouse.move(0, 0)
    await page.evaluate(async () => {
      await document.fonts.ready
    })
    await expect(page.locator('[aria-busy="true"]')).toHaveCount(0)
    const width = page.viewportSize()?.width ?? 0
    const file = path.join(test.info().project.testDir, 'screenshots', `${screen}-${theme}-${width}.png`)
    await page.screenshot({ path: file, animations: 'disabled', caret: 'hide' })
  }
  await setTheme(page, 'light')
}

const TEAMMATE = 'Tess Okafor'

/**
 * A second person in the workspace, invited as a Member and signed in in their own browser context; they act through
 * the API only. (Never the seeded Ana or Sam: every test's workspace shares them, and another test expects them to be
 * "Invited", which signing in would end.)
 */
async function asTeammate(browser: Browser, page: Page, seeded: Seeded): Promise<{ tess: Api; tessId: Uuid; close: () => Promise<void> }> {
  const email = uniqueEmail('inbox-teammate')
  await api(page).json('POST', `/api/workspaces/${seeded.workspaceId}/members`, { email, role: 'Member', displayName: TEAMMATE })
  const context = await browser.newContext()
  const theirs = await context.newPage()
  const user = await signIn(theirs, { email, displayName: TEAMMATE })
  expect(user.displayName).toBe(TEAMMATE)
  return { tess: api(theirs), tessId: user.id, close: () => context.close() }
}

const unreadOf = async (client: Api, workspaceId: Uuid): Promise<number> =>
  (await client.json<{ unread: number }>('GET', `/api/workspaces/${workspaceId}/notifications/count`)).unread

const inboxOf = (client: Api, workspaceId: Uuid): Promise<InboxPage> =>
  client.json<InboxPage>('GET', `/api/workspaces/${workspaceId}/notifications`)

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
})

test.describe('Inbox', () => {
  test('lists what happened, counts what is unread, and opening a task reads its rows', async ({ page, seeded, browser }) => {
    const roadmap = seeded.tasks['Plan Q1 roadmap']
    const pricing = seeded.tasks['Update pricing page']
    const onboarding = seeded.tasks['Write onboarding guide']
    const { tess, close } = await asTeammate(browser, page, seeded)

    await test.step('a teammate assigns me a task and mentions me in a comment', async () => {
      await tess.patchTask(roadmap.id, { assigneeIds: [seeded.me.id] })
      await tess.json('POST', `/api/tasks/${pricing.id}/comments`, { body: `<@${seeded.me.id}> can you check the numbers?` })
      expect(await unreadOf(api(page), seeded.workspaceId)).toBe(5)
    })

    await test.step('the sidebar shows the count; G then I opens the Inbox', async () => {
      await page.goto('/')
      await expect(inboxLink(page)).toHaveAccessibleName('Inbox, 5 unread')
      await expect(badge(page)).toHaveText('5')
      await expect(main(page)).toBeFocused()
      await page.keyboard.press('g')
      await page.keyboard.press('i')
      await expect(page).toHaveURL(/\/inbox$/)
      await expect(page.getByRole('heading', { name: 'Inbox', level: 1 })).toBeVisible()
      await expect(inboxLink(page)).toHaveAttribute('aria-current', 'page')
    })

    await test.step('newest first: the mention, the assignment, then the seeded comment and reminders', async () => {
      await expect(rowOpeners(page)).toHaveCount(5)
      const names = await rowOpeners(page).evaluateAll((els) => els.map((el) => el.getAttribute('aria-label') ?? ''))
      expect(names.slice(0, 4)).toEqual([
        'Unread. Tess Okafor mentioned you: Update pricing page. “@Jared Doe can you check the numbers?”',
        'Unread. Tess Okafor assigned this to you: Plan Q1 roadmap',
        'Unread. Ana Moreno commented: Write onboarding guide. “Looks good, can we add screenshots?”',
        'Unread. Due today: Write onboarding guide',
      ])
      expect(names[4]).toMatch(/^Unread\. Overdue, was due .+: Fix sign-in redirect$/)
      await expect(main(page).getByRole('radio', { name: /^Unread/ })).toBeChecked()
      // Each row says where its task lives.
      await expect(inboxRow(page, 'assigned this to you')).toContainText(LIST)
      await axeBothThemes(page)
      await shots(page, 'inbox')
    })

    await test.step("opening a row's task reads everything about that task", async () => {
      await saved(page, 'POST', '/api/notifications/read', () => inboxRow(page, 'Looks good, can we add screenshots?').getByRole('button').first().click())
      const panel = taskPanel(page, onboarding.title)
      await expect(panel).toBeVisible()
      await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()
      await expect(badge(page)).toHaveText('3')
      expect(await unreadOf(api(page), seeded.workspaceId)).toBe(3)
      await page.keyboard.press('Escape')
      await expect(panel).toBeHidden()
      // Read rows stay on screen in the Unread view until the page is left.
      await expect(rowOpeners(page)).toHaveCount(5)
      await expect(inboxRow(page, 'Looks good, can we add screenshots?').getByRole('button').first()).toHaveAccessibleName(/^Ana Moreno commented/)
      await expect(inboxRow(page, 'Due today').getByRole('button').first()).toHaveAccessibleName(/^Due today/)
      await expect(inboxRow(page, 'assigned this to you').getByRole('button').first()).toHaveAccessibleName(/^Unread\. /)
    })

    await test.step('"Mark as unread" on a row puts it back', async () => {
      const row = inboxRow(page, 'Due today')
      await row.hover()
      await saved(page, 'POST', '/api/notifications/read', () => row.getByRole('button', { name: 'Mark as unread' }).click())
      await expect(row.getByRole('button').first()).toHaveAccessibleName(/^Unread\. Due today/)
      await expect(badge(page)).toHaveText('4')
      await expect(row.getByRole('button', { name: 'Mark as read' })).toBeVisible()
    })

    await test.step('"Mark all as read" clears the count', async () => {
      await saved(page, 'POST', '/notifications/read-all', () => page.getByRole('button', { name: 'Mark all as read' }).click())
      await expect(badge(page)).toHaveCount(0)
      await expect(inboxLink(page)).toHaveAccessibleName('Inbox')
      await expect(page.getByRole('button', { name: 'Mark all as read' })).toBeDisabled()
      expect(await unreadOf(api(page), seeded.workspaceId)).toBe(0)
    })

    await test.step('after a reload Unread is empty and All still lists everything', async () => {
      await page.reload()
      await expect(main(page).getByRole('heading', { name: "You're all caught up." })).toBeVisible()
      await axeBothThemes(page, 'main')
      await main(page).getByRole('button', { name: 'Show everything' }).click()
      await expect(main(page).getByRole('radio', { name: 'All' })).toBeChecked()
      await expect(rowOpeners(page)).toHaveCount(5)
      await expect(rowOpeners(page).first()).toHaveAccessibleName(/^Tess Okafor mentioned you/)
    })

    await test.step('a change while the Inbox is open arrives with the next count', async () => {
      await tess.patchTask(roadmap.id, { priority: 'Urgent' })
      // The count is asked for when the tab is shown again (and every minute).
      await page.evaluate(() => window.dispatchEvent(new Event('focus')))
      await expect.poll(() => unreadOf(api(page), seeded.workspaceId)).toBe(1)
      await page.reload()
      await expect(badge(page)).toHaveText('1')
      await main(page).getByRole('radio', { name: /^Unread/ }).click()
      await expect(rowOpeners(page)).toHaveCount(1)
      await expect(rowOpeners(page).first()).toHaveAccessibleName('Unread. Tess Okafor set priority to Urgent: Plan Q1 roadmap')
    })

    await close()
  })

  test('on a phone the Inbox is one tap from the navigation and opens tasks as a sheet @phone', async ({ page, seeded }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Open navigation' }).tap()
    await expect(inboxLink(page)).toHaveAccessibleName('Inbox, 3 unread')
    await inboxLink(page).tap()
    await expect(page).toHaveURL(/\/inbox$/)
    await expect(sidebar(page)).toBeHidden()
    await expect(rowOpeners(page)).toHaveCount(3)
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
    await axeBothThemes(page)

    await saved(page, 'POST', '/api/notifications/read', () => inboxRow(page, 'Overdue').getByRole('button').first().tap())
    const sheet = page.getByRole('dialog', { name: 'Fix sign-in redirect', exact: true })
    await expect(sheet).toBeVisible()
    await expect.poll(() => unreadOf(api(page), seeded.workspaceId)).toBe(2)
  })
})

test.describe('@mentions', () => {
  test('@ lists the people who can see the task; the comment keeps the mention and tells them', async ({ page, seeded, browser }) => {
    const roadmap = seeded.tasks['Plan Q1 roadmap']
    const work = seeded.spaces.Work
    const sam = seeded.members['Sam Lee']
    const { tess, tessId, close } = await asTeammate(browser, page, seeded)
    const panel = taskPanel(page, roadmap.title)
    const composer = panel.getByRole('textbox', { name: 'Write a comment' })
    let commentId = ''

    await test.step('open the task', async () => {
      await page.goto(`/l/${roadmap.listId}/list?task=${roadmap.id}`)
      await expect(panel).toBeVisible()
      await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()
    })

    await test.step('"@" lists everyone but me; typing narrows it; Esc closes the list, not the panel', async () => {
      await composer.click()
      await composer.pressSequentially('@')
      await expect(mentionList(page).getByRole('option')).toHaveText([/Ana Moreno/, /Sam Lee/, /Tess Okafor/])
      await axeBothThemes(page, '[role="listbox"]')
      await composer.pressSequentially('sa')
      await expect(mentionList(page).getByRole('option')).toHaveText([/Sam Lee/])
      await page.keyboard.press('Escape')
      await expect(mentionList(page)).toBeHidden()
      await expect(panel).toBeVisible()
      await expect(composer).toBeFocused()
      // The list stays closed for this "@".
      await composer.pressSequentially('m')
      await expect(mentionList(page)).toBeHidden()
      await composer.fill('')
    })

    await test.step('Down and Up choose, Enter picks a person; Enter then sends', async () => {
      await composer.pressSequentially('Hi @')
      const option = (name: RegExp): Locator => mentionList(page).getByRole('option', { name })
      await expect(option(/Ana Moreno/)).toHaveAttribute('aria-selected', 'true')
      await page.keyboard.press('ArrowUp')
      await expect(option(/Tess Okafor/)).toHaveAttribute('aria-selected', 'true')
      await page.keyboard.press('ArrowDown')
      await expect(option(/Ana Moreno/)).toHaveAttribute('aria-selected', 'true')
      await composer.pressSequentially('te')
      await expect(mentionList(page).getByRole('option')).toHaveText([/Tess Okafor/])
      await page.keyboard.press('Enter')
      await expect(mentionList(page)).toBeHidden()
      await expect(composer).toHaveValue('Hi @Tess Okafor ')
      await composer.pressSequentially('please review')
      await saved(page, 'POST', `/api/tasks/${roadmap.id}/comments`, () => page.keyboard.press('Enter'))
      await expect(composer).toHaveValue('')

      const comment = panel.getByRole('listitem').filter({ hasText: 'please review' })
      await expect(comment.locator('.mention')).toHaveText('@Tess Okafor')
      await expect(comment).toContainText('Hi @Tess Okafor please review')
      const detail = await api(page).task(roadmap.id)
      const last = detail.comments[detail.comments.length - 1]
      expect(last.body).toBe(`Hi <@${tessId}> please review`)
      expect(last.mentions).toEqual([{ userId: tessId, displayName: TEAMMATE }])
      commentId = last.id
    })

    await test.step('the teammate finds the mention in their Inbox', async () => {
      const theirs = (await inboxOf(tess, seeded.workspaceId)).items
      expect(theirs.map((n) => [n.kind, n.taskTitle, n.actor?.displayName, n.to])).toEqual([
        ['Mentioned', roadmap.title, 'Jared Doe', 'Hi @Tess Okafor please review'],
      ])
    })

    await test.step('editing shows the names again, offers the @ list and keeps both mentions', async () => {
      const comment = panel.getByRole('listitem').filter({ hasText: 'please review' })
      await comment.hover()
      await comment.getByRole('button', { name: 'Comment actions' }).click()
      await page.getByRole('menuitem', { name: 'Edit' }).click()
      const editor = panel.getByRole('textbox', { name: 'Edit comment' })
      await expect(editor).toHaveValue('Hi @Tess Okafor please review')
      await editor.press('End')
      await editor.pressSequentially(' with @sam')
      await expect(mentionList(page).getByRole('option')).toHaveText([/Sam Lee/])
      await page.keyboard.press('Tab')
      await expect(editor).toHaveValue('Hi @Tess Okafor please review with @Sam Lee ')
      await saved(page, 'PATCH', `/api/comments/${commentId}`, () => page.keyboard.press('Enter'))
      await expect(comment.locator('.mention')).toHaveText(['@Tess Okafor', '@Sam Lee'])
      await expect(comment).toContainText('(edited)')
      const detail = await api(page).task(roadmap.id)
      const edited = detail.comments.find((c) => c.id === commentId)
      expect(edited?.body).toBe(`Hi <@${tessId}> please review with <@${sam.userId}>`)
      expect(edited?.mentions.map((m) => m.displayName)).toEqual([TEAMMATE, 'Sam Lee'])
    })

    await test.step('in a private space only its people are offered, and the server refuses anyone else', async () => {
      await api(page).json('PATCH', `/api/spaces/${work.id}`, { isPrivate: true })
      await api(page).json('POST', `/api/spaces/${work.id}/members`, { userIds: [sam.userId] })
      await page.reload()
      await expect(panel.getByRole('textbox', { name: 'Description' })).toBeVisible()
      await composer.click()
      await composer.pressSequentially('@')
      await expect(mentionList(page).getByRole('option')).toHaveText([/Sam Lee/])
      await composer.fill('')
      await expect(mentionList(page)).toBeHidden()
      const refused = await api(page).post(`/api/tasks/${roadmap.id}/comments`, { data: { body: `<@${tessId}> are you there?` } })
      expect(refused.status()).toBe(400)
      expect(((await refused.json()) as { detail?: string }).detail).toBe('Only people who can see this task can be mentioned.')
    })

    await close()
  })
})

test.describe('Notification settings', () => {
  test('Settings › Notifications saves the email choices and shows the time zone', async ({ page, seeded }) => {
    await page.goto('/settings/notifications')
    const region = page.getByRole('region', { name: 'Notification settings' })
    await expect(region.getByRole('heading', { name: 'Notifications', level: 2 })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Settings' }).getByRole('link', { name: 'Notifications' })).toHaveAttribute('aria-current', 'page')

    await test.step('this server only logs email, and says so', async () => {
      await expect(region.getByRole('status').filter({ hasText: "Email isn't set up on this server yet" })).toBeVisible()
      await expect(region.getByText(`Sent to ${seeded.email}.`)).toBeVisible()
    })

    const mentions = region.getByRole('switch', { name: 'Assignments and mentions' })
    const reminders = region.getByRole('switch', { name: 'Due-date reminders' })
    await test.step('both emails are on at first; turning reminders off is saved', async () => {
      await expect(mentions).toBeChecked()
      await expect(reminders).toBeChecked()
      await saved(page, 'PATCH', '/api/me/notifications', () => reminders.click())
      await expect(reminders).not.toBeChecked()
      expect(await api(page).me()).toMatchObject({ emailMentions: true, emailReminders: false })
      await page.reload()
      await expect(reminders).not.toBeChecked()
      await expect(mentions).toBeChecked()
    })

    await test.step("reminders follow the browser's time zone, which the server now has", async () => {
      await expect(region.getByText(/Reminders follow UTC/)).toBeVisible()
      await expect.poll(async () => (await api(page).me()).timeZone).toBe('UTC')
    })

    await axeBothThemes(page)
  })
})

// Self-test of the shared fixtures (fixtures.ts). It needs the API but not the web app, so it also runs with
// TASKLOOM_E2E_URL pointing straight at an API snapshot:  e2e.sh <name> http://localhost:<api port> e2e/smoke.spec.ts
import type { Page } from '@playwright/test'
import {
  ApiError,
  STAGE,
  api,
  day,
  dragTo,
  expect,
  expectNoAxeViolations,
  gotoMonth,
  seed,
  signIn,
  stageC,
  test,
  type Task,
} from './fixtures'

const WEBSITE = 'Website refresh'
const ADMIN = 'Admin'
const PERSONAL = 'Tasks'

// 11.3: every named task and subtask, with its list and status.
const NAMED: [title: string, list: string, status: string][] = [
  ['Write onboarding guide', WEBSITE, 'In progress'],
  ['Fix sign-in redirect', WEBSITE, 'Review'],
  ['Plan Q1 roadmap', WEBSITE, 'To do'],
  ['Update pricing page', WEBSITE, 'To do'],
  ['Set up analytics', WEBSITE, 'Done'],
  ['Draft outline', WEBSITE, 'Done'],
  ['Collect screenshots', WEBSITE, 'To do'],
  ['Review with Ana', WEBSITE, 'To do'],
  ['Send invoice to Contoso', ADMIN, 'To do'],
  ['Renew domain names', ADMIN, 'In progress'],
  ['Book team offsite', ADMIN, 'To do'],
  ['Archive old receipts', ADMIN, 'Done'],
  ['Call the printer company', PERSONAL, 'To do'],
  ['Read Q3 report', PERSONAL, 'To do'],
  ['Renew passport', PERSONAL, 'In progress'],
]

test.describe('sign-in, seed and api(page)', () => {
  test('seeded signs a fresh user in and returns the 11.3 dataset by name', async ({ page, seeded }) => {
    expect(seeded.email).toMatch(/^e2e-smoke-sign-in-seed-and-api-page-seeded-[a-z0-9-]*-\d{13}@example\.com$/)
    expect(seeded.me).toMatchObject({ email: seeded.email, displayName: 'Jared Doe' })

    // The session cookie is in the page's browser context, so the browser itself is signed in.
    expect((await page.context().cookies()).map((c) => c.name)).toContain('taskloom.auth')
    const me = await page.goto('/api/me')
    expect(await me?.json()).toEqual(seeded.me)

    // Structure: Work (new) with Website refresh (Review preset) and Admin (Simple); Personal / Tasks reused.
    expect(Object.keys(seeded.spaces).sort()).toEqual(['Personal', 'Work'])
    expect(seeded.spaces.Work.color).toBe('#3D7DF5')
    expect(Object.keys(seeded.lists).sort()).toEqual([ADMIN, PERSONAL, WEBSITE])
    expect(seeded.lists[WEBSITE].spaceId).toBe(seeded.spaces.Work.id)
    expect(seeded.lists[ADMIN].spaceId).toBe(seeded.spaces.Work.id)
    expect(seeded.lists[PERSONAL].spaceId).toBe(seeded.spaces.Personal.id)
    expect(Object.keys(seeded.lists[WEBSITE].statusIds)).toEqual(['To do', 'In progress', 'Review', 'Done'])
    expect(Object.keys(seeded.lists[ADMIN].statusIds)).toEqual(['To do', 'In progress', 'Done'])

    // Counts: named tasks + subtasks + 48 fillers (20, 16 and 12 per list).
    const tasks = Object.values(seeded.tasks)
    const inList = (list: string) => tasks.filter((t) => t.listId === seeded.lists[list].id)
    expect(tasks).toHaveLength(63)
    expect(inList(WEBSITE)).toHaveLength(28)
    expect(inList(ADMIN)).toHaveLength(20)
    expect(inList(PERSONAL)).toHaveLength(15)

    for (const [title, list, status] of NAMED) {
      const { id: listId, statusIds } = seeded.lists[list]
      expect(seeded.tasks[title], title).toMatchObject({ listId, statusId: statusIds[status] })
    }
    // The scenario facts: Website refresh has 6 top-level tasks in Review and 6 in In progress.
    const topLevelIn = (status: string) =>
      inList(WEBSITE).filter((t) => t.parentTaskId === null && t.statusId === seeded.lists[WEBSITE].statusIds[status])
    expect(topLevelIn('Review')).toHaveLength(6)
    expect(topLevelIn('In progress')).toHaveLength(6)

    const guide = seeded.tasks['Write onboarding guide']
    const ana = seeded.members['Ana Moreno']
    expect(guide).toMatchObject({
      dueDate: day(0).iso,
      priority: 'High',
      assigneeIds: [seeded.me.id, ana.userId],
      hasDescription: true,
      subtaskCount: 3,
      subtaskDoneCount: 1,
      commentCount: 2,
    })
    expect(tasks.filter((t) => t.parentTaskId === guide.id).map((t) => t.title).sort()).toEqual(
      ['Collect screenshots', 'Draft outline', 'Review with Ana'],
    )
    expect(seeded.tasks['Renew domain names'].dueDate).toBe(day(1).iso)
    expect(seeded.tasks['Send invoice to Contoso'].dueDate).toBe(day(-2).iso)
    expect(seeded.tasks['Book team offsite'].dueDate).toBeNull()

    expect(seeded.members['Jared Doe']).toMatchObject({ userId: seeded.me.id, role: 'Owner', pending: false })
    expect(ana).toMatchObject({ email: 'ana@example.com', role: 'Member', pending: true })
    expect(seeded.members['Sam Lee']).toMatchObject({ email: 'sam@example.com', role: 'Guest', pending: true })

    if (Object.keys(seeded.tags).length) {
      expect(Object.keys(seeded.tags).sort()).toEqual(['bug', 'design', 'docs', 'ops'])
      expect(guide.tagIds).toEqual([seeded.tags.design.id, seeded.tags.docs.id])
      expect(seeded.tasks['Fix sign-in redirect'].tagIds).toEqual([seeded.tags.bug.id])
    } else {
      // An API without the tag endpoints (#15) yet: seed() says so instead of failing.
      expect(test.info().annotations.map((a) => a.description)).toContainEqual(expect.stringContaining('no tag endpoints'))
      expect(guide.tagIds).toHaveLength(2)
    }
  })

  test('api(page) sends X-Taskloom, so a PATCH saves and reads back', async ({ page, seeded }) => {
    const task = seeded.tasks['Plan Q1 roadmap']
    expect(task).toMatchObject({ priority: 'Normal', dueDate: day(3).iso })

    // Without the header the API refuses the call (5.1): the wrapper is what makes the calls below work.
    const bare = await page.request.patch(`/api/tasks/${task.id}`, { data: { priority: 'Urgent' } })
    expect(bare.status()).toBe(400)
    expect((await bare.json()).detail).toBe('Missing X-Taskloom header.')

    const res = await api(page).patch(`/api/tasks/${task.id}`, { data: { priority: 'Urgent', dueDate: day(5).iso } })
    expect(res.status()).toBe(200)
    expect((await api(page).task(task.id)).task).toMatchObject({ id: task.id, priority: 'Urgent', dueDate: day(5).iso })

    const cleared = await api(page).patchTask(task.id, { priority: null, dueDate: null })
    expect(cleared).toMatchObject({ priority: 'None', dueDate: null })
    const listed = (await api(page).listTasks(seeded.lists[WEBSITE].id)).find((t: Task) => t.id === task.id)
    expect(listed).toMatchObject({ priority: 'None', dueDate: null })

    const comment = await api(page).post(`/api/tasks/${task.id}/comments`, { data: { body: 'Saved through api(page)' } })
    expect(comment.status()).toBe(200)
    expect((await api(page).task(task.id)).comments.map((c) => c.body)).toEqual(['Saved through api(page)'])

    // A refused call throws ApiError with the ProblemDetails sentence.
    const error = await api(page).patchTask(task.id, { title: '   ' }).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 400, detail: expect.stringMatching(/\.$/) })
  })

  test('seed() adds another copy and returns only what it added', async ({ page, seeded }) => {
    const again = await seed(page, { fillers: 0 })
    expect(again.workspaceId).toBe(seeded.workspaceId)
    expect(again.workspace.spaces.filter((s) => s.name === 'Work')).toHaveLength(2)
    // The 12 named tasks and 3 subtasks, all new; no fillers.
    expect(Object.keys(again.tasks)).toHaveLength(15)
    const firstIds = new Set(Object.values(seeded.tasks).map((t) => t.id))
    expect(Object.values(again.tasks).filter((t) => firstIds.has(t.id))).toEqual([])
    // The new Work and its lists win the names; Personal / Tasks is reused.
    expect(again.spaces.Work.id).not.toBe(seeded.spaces.Work.id)
    expect(again.lists[WEBSITE].id).not.toBe(seeded.lists[WEBSITE].id)
    expect(again.lists[PERSONAL].id).toBe(seeded.lists[PERSONAL].id)
    expect(again.tasks['Renew passport'].listId).toBe(seeded.lists[PERSONAL].id)
  })

  test('signIn() reports a refused sign-in with the API sentence', async ({ page }) => {
    const error = await signIn(page, { email: 'not-an-email' }).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 400, detail: 'Enter a valid email address.' })
  })
})

// The console rule's own failing tests must fail, and only because of the rule. The rule depends on `context`, so an
// override of `context` is torn down after it: there, a failure that the rule reported (and nothing else) is turned
// into an expected one with testInfo.fail(), and a test that the rule let pass fails.
const RULE_MUST_REPORT = 'console-rule-must-report'
const expectRuleToReport = (text: string) => test.info().annotations.push({ type: RULE_MUST_REPORT, description: text })
const failsByConsoleRule = test.extend({
  context: async ({ context }, use, testInfo) => {
    await use(context)
    const wanted = testInfo.annotations.filter((a) => a.type === RULE_MUST_REPORT).map((a) => a.description ?? '')
    const messages = testInfo.errors.map((e) => e.message ?? '')
    const byRule = (m: string) => m.includes('The console rule (spec 11.1) failed this test') && wanted.every((w) => m.includes(w))
    if (messages.length === 1 && byRule(messages[0])) testInfo.fail(true, 'failed by the console rule, as it should')
    else if (!messages.length) throw new Error(`The console rule let this test pass; it should have reported: ${wanted.join('; ')}`)
  },
})

test.describe('console rule', () => {
  failsByConsoleRule('fails a test whose page logs a console error (an expected failure)', async ({ page }) => {
    expectRuleToReport('console.error: smoke-marker logged')
    await page.setContent('<p>console</p>')
    await page.evaluate(() => console.error('smoke-marker logged'))
  })

  failsByConsoleRule('fails a test whose page throws (an expected failure)', async ({ page }) => {
    expectRuleToReport('pageerror: Error: smoke-marker thrown')
    await page.setContent('<p>pageerror</p>')
    await Promise.all([
      page.waitForEvent('pageerror'),
      page.evaluate(() => {
        setTimeout(() => {
          throw new Error('smoke-marker thrown')
        })
      }),
    ])
  })

  test('lets "Failed to load resource" through', async ({ page }) => {
    const seen: string[] = []
    page.on('console', (m) => {
      if (m.type() === 'error') seen.push(m.text())
    })
    await page.goto('/healthz')
    expect(await page.evaluate(async () => (await fetch('/api/me')).status)).toBe(401)
    await expect.poll(() => seen).toContainEqual(expect.stringMatching(/^Failed to load resource: .*401/))
  })

  test.describe('with allowConsoleErrors', () => {
    test.use({ allowConsoleErrors: [/^smoke-allowed \d+$/, 'ResizeObserver loop'] })

    test('lets the allowed patterns through, for console and page errors', async ({ page }) => {
      await page.setContent('<p>allowed</p>')
      await page.evaluate(() => {
        console.error('smoke-allowed 1')
        console.error('ResizeObserver loop completed with undelivered notifications.')
      })
      await Promise.all([
        page.waitForEvent('pageerror'),
        page.evaluate(() => {
          setTimeout(() => {
            throw new Error('ResizeObserver loop limit exceeded')
          })
        }),
      ])
    })
  })
})

const DOT = 'data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs='

test.describe('expectNoAxeViolations', () => {
  test('passes on an accessible page', async ({ page }) => {
    await page.setContent(`<!doctype html><html lang="en"><head><title>Accessible</title></head><body><main>
      <h1>Fixture self-test</h1>
      <label>Task name <input type="text"></label>
      <button>Save</button>
      <img src="${DOT}" alt="A dot">
    </main></body></html>`)
    await expectNoAxeViolations(page)
  })

  test('fails on an inaccessible page and names each rule; include limits the check', async ({ page }) => {
    // Buttons get 32px targets: wcag22aa includes target-size (24px, or enough space around), which is serious.
    await page.setContent(`<!doctype html><html lang="en"><head><title>Inaccessible</title>
      <style>button { min-width: 32px; min-height: 32px; margin: 8px }</style></head><body><main>
      <h1>Fixture self-test</h1>
      <div id="fine"><button>Labelled</button></div>
      <div id="broken">
        <button></button>
        <img src="${DOT}">
        <input type="text">
        <p style="color:#c8c8c8;background:#ffffff">Faint text</p>
      </div>
    </main></body></html>`)
    const error = await expectNoAxeViolations(page).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(Error)
    const message = (error as Error).message
    expect(message).toMatch(/^4 serious or critical axe violation/)
    for (const rule of ['button-name (critical)', 'image-alt (critical)', 'label (critical)', 'color-contrast (serious)']) {
      expect(message).toContain(rule)
    }
    // Each rule names its help text and the offending elements.
    expect(message).toContain('button-name (critical): Buttons must have discernible text. #broken > button')

    await expectNoAxeViolations(page, { include: '#fine' })
    await expect(expectNoAxeViolations(page, { include: ['#fine', '#broken'] })).rejects.toThrow(/button-name/)
  })
})

test.describe('dragTo', () => {
  // Native HTML5 drag and drop, the events Pragmatic drag and drop listens to.
  const DRAG_PAGE = `<!doctype html><html lang="en"><head><title>Drag</title><style>
      body { margin: 0 }
      #source { position: absolute; left: 40px; top: 40px; width: 160px; height: 40px; background: #ddd }
      #target { position: absolute; left: 400px; top: 300px; width: 240px; height: 160px; background: #eef }
    </style></head><body>
    <div id="source" draggable="true">Card</div>
    <div id="target">Column</div>
    <script>
      // Every drag event on the page, in target coordinates (Chromium reports whole pixels).
      window.events = []
      const source = document.getElementById('source'), target = document.getElementById('target')
      const log = (e) => {
        const box = target.getBoundingClientRect()
        const on = e.target.id || e.target.nodeName.toLowerCase()
        events.push({ type: e.type, on, x: e.clientX - box.left, y: e.clientY - box.top })
      }
      for (const type of ['dragstart', 'dragenter', 'dragover', 'drop']) window.addEventListener(type, log, true)
      source.addEventListener('dragstart', (e) => e.dataTransfer.setData('text/plain', 'card'))
      for (const type of ['dragenter', 'dragover']) target.addEventListener(type, (e) => e.preventDefault())
      target.addEventListener('drop', (e) => {
        e.preventDefault()
        events.push({ type: 'data', on: e.dataTransfer.getData('text/plain'), x: 0, y: 0 })
      })
    </script></body></html>`
  type DragEvent = { type: string; on: string; x: number; y: number }
  const events = (page: Page) => page.evaluate(() => (window as unknown as { events: DragEvent[] }).events)

  // The drag starts on the first step; each step then fires dragenter or dragover where the pointer is, so `steps`
  // steps and the 2px move give steps + 1 positions. The last step lands exactly on the drop point (x, y), where
  // Chromium fires only dragenter when it enters the target there; the 2px move (along the drag, positive here) then
  // fires the dragover that a drop needs, and the drop happens at that point.
  const expectDrag = (log: DragEvent[], x: number, y: number, steps: number) => {
    expect(log[0]).toMatchObject({ type: 'dragstart', on: 'source' })
    const moves = log.filter((e) => e.type === 'dragenter' || e.type === 'dragover')
    const positions = new Set(moves.map((e) => `${e.x},${e.y}`))
    expect(positions.size, 'pointer positions during the drag').toBeGreaterThanOrEqual(steps + 1)
    const atDropPoint = expect.objectContaining({ on: 'target', x, y })
    expect(moves, 'a drag event on the target exactly at the drop point').toContainEqual(atDropPoint)
    const overs = log.filter((e) => e.type === 'dragover')
    const [lastOver, drop, data] = [overs.at(-1)!, log.at(-2)!, log.at(-1)!]
    for (const event of [lastOver, drop]) {
      expect(event, `${event.type} 2px past the drop point`).toMatchObject({ on: 'target' })
      const justPast = event.x > x && event.x <= x + 2 && event.y > y && event.y <= y + 2
      expect(justPast, `${event.type} at (${event.x}, ${event.y})`).toBe(true)
    }
    expect(drop.type).toBe('drop')
    expect(data).toEqual({ type: 'data', on: 'card', x: 0, y: 0 })
  }

  test('drags with real pointer moves: ≥ 10 steps to the target, 2px more, then the drop', async ({ page }) => {
    await page.setContent(DRAG_PAGE)
    await dragTo(page, page.locator('#source'), page.locator('#target'))
    expectDrag(await events(page), 120, 80, 10) // the target's centre, after the default 10 steps
  })

  test('drops at targetPosition', async ({ page }) => {
    await page.setContent(DRAG_PAGE)
    await dragTo(page, page.locator('#source'), page.locator('#target'), { targetPosition: { x: 20, y: 6 }, steps: 15 })
    expectDrag(await events(page), 20, 6, 15)
  })
})

test.describe('calendar helpers', () => {
  test('day() gives T + offset in the formats lib/dates.ts shows', async ({ page }) => {
    const now = new Date()
    expect(day().iso).toBe(now.toISOString().slice(0, 10))
    const tomorrow = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1))
    expect(day(1).iso).toBe(tomorrow.toISOString().slice(0, 10))

    // Compare with what the browser (en-US, UTC) formats the way lib/dates.ts does, across month and year ends.
    await page.setContent('<p>dates</p>')
    for (const offset of [-400, -32, -1, 0, 1, 2, 31, 58, 400]) {
      const d = day(offset)
      const shown = await page.evaluate((iso) => {
        const [y, m, dd] = iso.split('-').map(Number)
        const date = new Date(y, m - 1, dd)
        const f = (o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(undefined, o).format(date)
        const year = y === new Date().getFullYear() ? {} : { year: 'numeric' as const }
        return {
          label: f({ weekday: 'long', month: 'long', day: 'numeric' }),
          short: f({ month: 'short', day: 'numeric', ...year }),
          medium: f({ weekday: 'short', month: 'short', day: 'numeric', ...year }),
          month: f({ month: 'long', year: 'numeric' }),
        }
      }, d.iso)
      expect({ label: d.label, short: d.short, medium: d.medium, month: d.month }, `day(${offset})`).toEqual(shown)
      expect(`${d.label}, 2 tasks`).toMatch(d.cell)
    }

    // A cell name never matches another day's: the 1st and the 15th share a weekday ("Sunday, November 1" is a
    // prefix of "Sunday, November 15").
    const toFirst = Array.from({ length: 31 }, (_, i) => i + 1).find((i) => day(i).iso.endsWith('-01'))!
    const [first, fifteenth] = [day(toFirst), day(toFirst + 14)]
    expect(fifteenth.label.startsWith(first.label)).toBe(true)
    expect(`${fifteenth.label}, 0 tasks`).not.toMatch(first.cell)
    expect(`${first.label}, 0 tasks`).toMatch(first.cell)
  })

  test('gotoMonth() pages to the month and ignores a date picker in the overlay root', async ({ page }) => {
    const t = day()
    const [year, month] = t.iso.split('-').map(Number)
    const monthName = new Intl.DateTimeFormat('en-US', { month: 'long', timeZone: 'UTC' })
    const names = Array.from({ length: 12 }, (_, m) => monthName.format(Date.UTC(2026, m, 1)))
    await page.setContent(`<!doctype html><html lang="en"><head><title>Calendar</title></head><body>
      <div id="app"><div>
        <button aria-label="Previous month" id="prev">&lt;</button>
        <button aria-label="Next month" id="next">&gt;</button>
        <button>Today</button>
        <span id="title"></span>
      </div></div>
      <div id="overlay-root"><div role="dialog" aria-label="Due date">
        <span>January 1999</span>
        <button aria-label="Previous month" onclick="window.misused = true">&lt;</button>
        <button aria-label="Next month" onclick="window.misused = true">&gt;</button>
      </div></div>
      <script>
        const names = ${JSON.stringify(names)}
        let shown = ${year} * 12 + ${month - 1}
        window.clicks = 0
        const render = () => {
          document.getElementById('title').textContent = names[shown % 12] + ' ' + Math.floor(shown / 12)
        }
        // Re-render a moment later, as Vue does, so gotoMonth() must wait for the title to change.
        const go = (step) => { window.clicks++; shown += step; setTimeout(render, 40) }
        document.getElementById('prev').onclick = () => go(-1)
        document.getElementById('next').onclick = () => go(1)
        render()
      </script></body></html>`)
    const title = page.locator('#title')
    await gotoMonth(page, t.iso)
    await expect(title).toHaveText(t.month)

    const later = day(70)
    await gotoMonth(page, later.iso)
    await expect(title).toHaveText(later.month)
    const earlier = day(-70)
    await gotoMonth(page, earlier.iso)
    await expect(title).toHaveText(earlier.month)

    const monthIndex = (iso: string) => Number(iso.slice(0, 4)) * 12 + Number(iso.slice(5, 7))
    const monthsBetween = (a: string, b: string) => monthIndex(b) - monthIndex(a)
    const state = await page.evaluate(() => {
      const w = window as unknown as { clicks: number; misused?: boolean }
      return { clicks: w.clicks, misused: w.misused === true }
    })
    expect(state.misused).toBe(false)
    expect(state.clicks).toBe(monthsBetween(t.iso, later.iso) + monthsBetween(earlier.iso, later.iso))
  })
})

test.describe('stages and projects', () => {
  test('stageC() runs a (C) step in stage C and skips it in stage B', async () => {
    let ran = false
    await stageC('a step that needs another screen', async () => {
      ran = true
    })
    expect(ran).toBe(STAGE === 'C')
  })

  test('desktop project: Chromium at 1440×900, UTC, en-US, with clipboard access', async ({ page, browserName, isMobile }) => {
    expect({ browserName, isMobile, viewport: page.viewportSize() }).toEqual({
      browserName: 'chromium',
      isMobile: false,
      viewport: { width: 1440, height: 900 },
    })
    await page.goto('/healthz')
    const env = await page.evaluate(async () => {
      await navigator.clipboard.writeText('copied by the smoke test')
      return {
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        language: navigator.language,
        clipboard: await navigator.clipboard.readText(),
        finePointer: matchMedia('(pointer: fine)').matches,
      }
    })
    expect(env).toEqual({ timeZone: 'UTC', language: 'en-US', clipboard: 'copied by the smoke test', finePointer: true })
  })

  test('phone project: a 390×844 touch screen', { tag: '@phone' }, async ({ page, browserName, isMobile, hasTouch }) => {
    expect({ browserName, isMobile, hasTouch, viewport: page.viewportSize() }).toEqual({
      browserName: 'chromium',
      isMobile: true,
      hasTouch: true,
      viewport: { width: 390, height: 844 },
    })
    await page.setContent('<!doctype html><meta name="viewport" content="width=device-width, initial-scale=1"><p>phone</p>')
    const env = await page.evaluate(() => ({
      width: innerWidth,
      coarsePointer: matchMedia('(pointer: coarse)').matches,
      noHover: matchMedia('(hover: none)').matches,
      touch: navigator.maxTouchPoints > 0,
    }))
    expect(env).toEqual({ width: 390, coarsePointer: true, noHover: true, touch: true })
  })
})

// Efficiency 1 (compress and cache downloads), checked against the real build the API serves: the build's Brotli
// copies reach the browser, files under /assets are kept for a year, index.html is checked on every visit, and the
// API's JSON is compressed. Only the built app has these, so the whole test is stage C.
import { api, expect, signIn, stageC, test, uniqueEmail } from './fixtures'

const KEEP = 'public, max-age=31536000, immutable'

test('Efficiency 1: the built app arrives compressed, /assets is kept for a year and index.html is always checked', async ({
  page,
}) => {
  await stageC('the app files', async () => {
    const assets: { url: string; encoding?: string; cacheControl?: string }[] = []
    page.on('response', (res) => {
      if (new URL(res.url()).pathname.startsWith('/assets/'))
        assets.push({ url: res.url(), encoding: res.headers()['content-encoding'], cacheControl: res.headers()['cache-control'] })
    })
    const index = await page.goto('/login')
    expect(index?.headers()['cache-control']).toBe('no-cache')
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

    // Every script and style sheet came as the build's Brotli copy; fonts as they are. All of /assets is kept.
    const text = assets.filter((a) => /\.(js|css)$/.test(new URL(a.url).pathname))
    expect(text.length).toBeGreaterThan(0)
    for (const a of text) expect(a.encoding, a.url).toBe('br')
    for (const a of assets) expect(a.cacheControl, a.url).toBe(KEEP)

    // The copies match the files they stand for (Playwright's request decodes them).
    const script = text.find((a) => a.url.endsWith('.js'))!.url
    const asBrotli = await page.request.get(script, { headers: { 'Accept-Encoding': 'br' } })
    const plain = await page.request.get(script, { headers: { 'Accept-Encoding': 'identity' } })
    expect(asBrotli.headers()['content-encoding']).toBe('br')
    expect(plain.headers()['content-encoding']).toBeUndefined()
    expect(await asBrotli.body()).toEqual(await plain.body())
    expect((await page.request.get(`${script}.br`)).status()).toBe(404)
  })

  await stageC("the API's JSON", async () => {
    await signIn(page, { email: uniqueEmail('delivery') })
    const res = await api(page).get('/api/workspaces', { headers: { 'Accept-Encoding': 'gzip, deflate, br' } })
    expect(res.ok()).toBe(true)
    expect(res.headers()['content-encoding']).toBe('br')
    expect(Array.isArray(await res.json())).toBe(true)
  })
})

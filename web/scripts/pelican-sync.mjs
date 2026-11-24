#!/usr/bin/env node
// Keeps the Pelican style's palette (src/styles/pelican.css, section 1) in step with a Pelican release.
//
//   npm run pelican:sync -- 2.5.0              compare with @la-ots/pelican@2.5.0 (fetched from jsDelivr)
//   npm run pelican:sync -- path/to/pelican.min.css [--as 2.5.0]
//   npm run pelican:sync -- 2.5.0 --write      also rewrite the palette block and its version
//
// The palette is Pelican's --theme-<family>-<shade> custom properties (and --theme-focus-color) whose values are
// plain colours, copied as --pl-<name> between the "pelican-palette:start <version>" and "pelican-palette:end"
// markers. The rest of pelican.css names them, so new values flow through. What the script can't judge is listed:
// a role Pelican points at another shade (say --theme-link-color moving from info-800) changes the mapping in
// section 2, so its lines from the release's :root are printed for a read-through against the "(role)" notes.
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const target = resolve(here, '../src/styles/pelican.css')
const args = process.argv.slice(2)
const write = args.includes('--write')
const asIndex = args.indexOf('--as')
const asVersion = asIndex >= 0 ? args[asIndex + 1] : undefined
const source = args.find((a, i) => !a.startsWith('--') && !(asIndex >= 0 && i === asIndex + 1))
if (!source) {
  console.error('Usage: npm run pelican:sync -- <version | path/to/pelican.min.css [--as <version>]> [--write]')
  process.exit(2)
}

async function loadRelease(src) {
  if (existsSync(src)) {
    // An unpacked npm tarball keeps its package.json two folders up from dist/css.
    const pkg = resolve(dirname(src), '../../package.json')
    const version = asVersion ?? (existsSync(pkg) ? JSON.parse(readFileSync(pkg, 'utf8')).version : undefined)
    if (!version && write) throw new Error('Say which release the file is from: --as <version>')
    return { css: readFileSync(src, 'utf8'), version: version ?? 'a local file' }
  }
  if (!/^\d+\.\d+\.\d+(-[\w.]+)?$/.test(src)) throw new Error(`Not a file or a version: ${src}`)
  const url = `https://cdn.jsdelivr.net/npm/@la-ots/pelican@${src}/dist/css/pelican.min.css`
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`)
  return { css: await res.text(), version: src }
}

const PALETTE_NAME = /^(?:[a-z]+-(?:\d+|white|black)|focus-color)$/

/** The palette's names and colours, in the order the release declares them (first declaration wins). */
function releasePalette(css) {
  const out = new Map()
  for (const m of css.matchAll(/--theme-([a-z0-9-]+):\s*(#[0-9a-f]{3,8})\b/gi)) {
    if (PALETTE_NAME.test(m[1]) && !out.has(m[1])) out.set(m[1], m[2].toLowerCase())
  }
  return out
}

/** Pelican's role tokens (--theme-x: var(--theme-y)) that section 2 follows, as the release declares them. */
function releaseRoles(css) {
  const roles = ['primary', 'accent', 'success', 'info', 'warning', 'danger', 'link-color', 'ui', 'ui-light', 'text-normal',
    'body-bg', 'border-color-lighter', 'form-border-color', 'text-headings-color', 'sidebar-wrapper-bg-color',
    'sidebar-item-bg-color', 'topbar-bg-color', 'card-bg-color', 'card-nested-bg-color', 'border-width', 'font-family']
  const out = []
  for (const r of roles) {
    const m = new RegExp(`--theme-${r}:\\s*([^;}]+)`).exec(css)
    out.push(`  --theme-${r}: ${m ? m[1].trim() : '(gone)'}`)
  }
  return out
}

const MARK = /\/\* pelican-palette:start ([^*]*?) \*\/\n([\s\S]*?)\n(\s*)\/\* pelican-palette:end \*\//

function ourPalette(text) {
  const m = MARK.exec(text)
  if (!m) throw new Error(`No pelican-palette markers in ${target}`)
  const values = new Map()
  for (const d of m[2].matchAll(/--pl-([a-z0-9-]+):\s*(#[0-9a-f]{3,8})/gi)) values.set(d[1], d[2].toLowerCase())
  return { version: m[1].trim(), values, indent: m[3] }
}

/** The block as this script writes it: each family on one line or split evenly over two (six values at most to a line). */
function renderBlock(palette, indent) {
  const families = new Map()
  for (const [name, value] of palette) {
    const family = name === 'focus-color' ? 'focus' : name.replace(/-(?:\d+|white|black)$/, '')
    if (!families.has(family)) families.set(family, [])
    families.get(family).push(`--pl-${name}: ${value};`)
  }
  const lines = []
  for (const decls of families.values()) {
    const per = Math.ceil(decls.length / Math.ceil(decls.length / 6))
    for (let i = 0; i < decls.length; i += per) lines.push(indent + decls.slice(i, i + per).join(' '))
  }
  return lines.join('\n')
}

const release = await loadRelease(source)
const theirs = releasePalette(release.css)
if (theirs.size === 0) throw new Error('No --theme-* colours found: is this pelican.min.css?')
const text = readFileSync(target, 'utf8')
const ours = ourPalette(text)

const changed = [...theirs].filter(([n, v]) => ours.values.has(n) && ours.values.get(n) !== v)
const added = [...theirs.keys()].filter((n) => !ours.values.has(n))
const removed = [...ours.values.keys()].filter((n) => !theirs.has(n))

console.log(`pelican.css has Pelican ${ours.version}; comparing with ${release.version}.`)
if (!changed.length && !added.length && !removed.length) console.log('The palette is the same.')
for (const [n, v] of changed) console.log(`  changed  --pl-${n}: ${ours.values.get(n)} -> ${v}`)
for (const n of added) console.log(`  new      --pl-${n}: ${theirs.get(n)}`)
for (const n of removed) console.log(`  gone     --pl-${n} (still named in pelican.css? search for it before writing)`)
console.log('\nRoles section 2 follows, as this release declares them (compare with the notes in pelican.css):')
console.log(releaseRoles(release.css).join('\n'))

if (write) {
  const block = renderBlock(theirs, ours.indent)
  const next = text.replace(MARK, `/* pelican-palette:start ${release.version} */\n${block}\n${ours.indent}/* pelican-palette:end */`)
  if (next === text) console.log('\nNothing to write.')
  else {
    writeFileSync(target, next)
    console.log(`\nWrote the ${release.version} palette to src/styles/pelican.css. Update the version in its header comment too.`)
  }
} else if (changed.length || added.length || removed.length || ours.version !== release.version) {
  console.log('\nRun again with --write to update the palette block.')
}

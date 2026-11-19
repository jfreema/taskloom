<script setup lang="ts">
// Settings › Appearance (spec 8.13).
// - "Theme": UiRadioCards (System, Light, Dark) bound to useTheme. Each card shows a 96×56 mini
//   mock painted with that theme's canvas, surface and accent; System shows both halves. The colours
//   come from the token rules in styles/tokens.css (read once through the CSSOM), so this file holds
//   no colour values and the previews follow any change to the tokens.
// - "Density" (Look 2): UiRadioCards (Comfortable, Compact) bound to useDensity, each with a mock of
//   rows at that spacing.
// - "Single-key shortcuts": UiSwitch bound to shortcutsEnabled (taskloom.shortcuts.singleKey,
//   default on).
// All are saved in this browser only.
import { computed, useId } from 'vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiRadioCards, { type RadioCardOption } from '../../components/ui/UiRadioCards.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useDensity } from '../../composables/useDensity'
import { shortcutsEnabled } from '../../composables/useShortcut'
import { useTheme } from '../../composables/useTheme'
import type { ThemePreference } from '../../types'

interface Palette {
  canvas: string
  surface: string
  accent: string
  line: string
  ink: string
}

const TOKEN: Record<keyof Palette, string> = {
  canvas: '--bg-canvas',
  surface: '--bg-surface',
  accent: '--accent',
  line: '--border-default',
  ink: '--text-tertiary',
}

function paletteFrom(style: CSSStyleDeclaration): Palette | null {
  const out = {} as Palette
  for (const key of Object.keys(TOKEN) as (keyof Palette)[]) {
    const v = style.getPropertyValue(TOKEN[key]).trim()
    if (!v) return null
    out[key] = v
  }
  return out
}

/** The light (`:root`) and dark (`:root[data-theme="dark"]`) token rules, found in the loaded stylesheets. */
function readPalettes(): { light: Palette; dark: Palette } {
  let light: Palette | null = null
  let dark: Palette | null = null
  const visit = (rules: CSSRuleList): void => {
    for (const rule of Array.from(rules)) {
      if (light && dark) return
      if (rule instanceof CSSStyleRule) {
        const selector = rule.selectorText.replace(/\s+/g, '')
        if (!light && selector === ':root') light = paletteFrom(rule.style)
        else if (!dark && /^:root\[data-theme=(["']?)dark\1\]$/.test(selector)) dark = paletteFrom(rule.style)
      } else if (rule instanceof CSSGroupingRule && !(rule instanceof CSSMediaRule)) {
        visit(rule.cssRules)
      }
    }
  }
  for (const sheet of Array.from(document.styleSheets)) {
    try {
      visit(sheet.cssRules)
    } catch {
      // A cross-origin sheet can't be read; the tokens are never in one.
    }
  }
  // Should a rule be missing, both previews fall back to the live theme rather than break.
  const live = paletteFrom(getComputedStyle(document.documentElement))
  const fallback: Palette = live ?? { canvas: 'Canvas', surface: 'Canvas', accent: 'AccentColor', line: 'GrayText', ink: 'GrayText' }
  return { light: light ?? fallback, dark: dark ?? fallback }
}

const palettes = readPalettes()

const { preference, setPreference } = useTheme()

const theme = computed<string>({
  get: () => preference.value,
  set: (v) => {
    if (v === 'system' || v === 'light' || v === 'dark') setPreference(v satisfies ThemePreference)
  },
})

const options: RadioCardOption[] = [
  { value: 'system', label: 'System', description: 'Follows your device' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
]

const { density, setDensity } = useDensity()

const densityModel = computed<string>({
  get: () => density.value,
  set: (v) => {
    if (v === 'comfortable' || v === 'compact') setDensity(v)
  },
})

const densityOptions: RadioCardOption[] = [
  { value: 'comfortable', label: 'Comfortable', description: 'Room to breathe' },
  { value: 'compact', label: 'Compact', description: 'More tasks on screen' },
]

/** The mock's rows (their middles) and the gap between them: four rows 11 apart, or six 7.5 apart. */
function densityRows(value: string): { ys: number[]; gap: number } {
  const gap = value === 'compact' ? 7.5 : 11
  const count = value === 'compact' ? 6 : 4
  return { ys: Array.from({ length: count }, (_, i) => 8 + i * gap), gap }
}

const uid = useId()
const clipId = `${uid}-half`

// One mock: the sidebar's lines on the canvas, and a surface panel with a title, an accent button
// and three rows, each with an accent check.
const ROWS = [20, 29, 38] as const

function halves(value: string): { palette: Palette; clip?: string }[] {
  if (value === 'light') return [{ palette: palettes.light }]
  if (value === 'dark') return [{ palette: palettes.dark }]
  return [{ palette: palettes.light }, { palette: palettes.dark, clip: `url(#${clipId})` }]
}
</script>

<template>
  <SettingsPage title="Appearance" description="How Taskloom looks and listens to keys. Saved in this browser.">
    <section class="block" aria-labelledby="appearance-theme">
      <h3 id="appearance-theme" class="block-title">Theme</h3>
      <UiRadioCards v-model="theme" name="theme" aria-label="Theme" :options="options">
        <template #preview="{ option }">
          <span class="stage">
            <svg class="mock" viewBox="0 0 96 56" aria-hidden="true" focusable="false">
              <defs v-if="option.value === 'system'">
                <clipPath :id="clipId">
                  <rect x="48" y="0" width="48" height="56" />
                </clipPath>
              </defs>
              <g v-for="(h, i) in halves(option.value)" :key="i" :clip-path="h.clip">
                <rect width="96" height="56" rx="6" :fill="h.palette.canvas" />
                <rect x="6" y="9" width="14" height="3" rx="1.5" :fill="h.palette.ink" opacity="0.55" />
                <rect x="6" y="16" width="17" height="3" rx="1.5" :fill="h.palette.line" />
                <rect x="6" y="23" width="12" height="3" rx="1.5" :fill="h.palette.line" />
                <rect x="6" y="30" width="15" height="3" rx="1.5" :fill="h.palette.line" />
                <rect x="27" y="5" width="64" height="46" rx="4" :fill="h.palette.surface" />
                <rect x="32" y="10" width="22" height="4" rx="2" :fill="h.palette.ink" opacity="0.7" />
                <rect x="77" y="9.5" width="10" height="5" rx="2" :fill="h.palette.accent" />
                <g v-for="y in ROWS" :key="y">
                  <circle cx="35" :cy="y + 1.5" r="2" :fill="h.palette.accent" />
                  <rect x="40" :y="y" :width="y === 29 ? 26 : 36" height="3" rx="1.5" :fill="h.palette.line" />
                </g>
                <rect x="0.5" y="0.5" width="95" height="55" rx="5.5" fill="none" :stroke="h.palette.line" />
              </g>
              <line v-if="option.value === 'system'" x1="48" y1="0.5" x2="48" y2="55.5" :stroke="palettes.dark.line" />
            </svg>
          </span>
        </template>
      </UiRadioCards>
    </section>

    <section class="block" aria-labelledby="appearance-density">
      <h3 id="appearance-density" class="block-title">Density</h3>
      <p class="block-text">How tightly lists, Home and Board cards are packed. Touch screens keep larger rows.</p>
      <UiRadioCards v-model="densityModel" name="density" aria-label="Density" :options="densityOptions">
        <template #preview="{ option }">
          <span class="stage">
            <svg class="mock density-mock" viewBox="0 0 96 56" aria-hidden="true" focusable="false">
              <rect class="d-surface" width="96" height="56" rx="6" />
              <g v-for="(y, i) in densityRows(option.value).ys" :key="y">
                <circle class="d-accent" cx="12" :cy="y" r="2.25" />
                <rect class="d-line" x="19" :y="y - 1.5" :width="i % 2 ? 44 : 58" height="3" rx="1.5" />
                <line class="d-rule" x1="6" :y1="y + densityRows(option.value).gap / 2" x2="90" :y2="y + densityRows(option.value).gap / 2" />
              </g>
              <rect class="d-frame" x="0.5" y="0.5" width="95" height="55" rx="5.5" />
            </svg>
          </span>
        </template>
      </UiRadioCards>
    </section>

    <section class="block" aria-labelledby="appearance-keyboard">
      <h3 id="appearance-keyboard" class="block-title">Keyboard</h3>
      <UiSwitch
        v-model="shortcutsEnabled"
        label="Single-key shortcuts"
        description="Use keys like N, S and J without Ctrl or Cmd. Turn off if they get in your way."
      />
    </section>
  </SettingsPage>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.block + .block {
  padding-top: var(--space-6);
  border-top: 1px solid var(--border-subtle);
}

.block-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.block-text {
  margin-top: calc(var(--space-2) * -1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.stage {
  display: flex;
  justify-content: center;
  padding: var(--space-2) 0;
  border-radius: var(--radius-sm);
  background: var(--bg-subtle);
}

/* Painted with the live theme's tokens. */
.density-mock .d-surface { fill: var(--bg-surface); }
.density-mock .d-accent { fill: var(--accent); }
.density-mock .d-line { fill: var(--border-default); }
.density-mock .d-rule { stroke: var(--border-subtle); stroke-width: 0.75; }
.density-mock .d-frame { fill: none; stroke: var(--border-default); }

.mock {
  display: block;
  width: 100%;
  max-width: 96px;
  height: auto;
  aspect-ratio: 96 / 56;
}
</style>

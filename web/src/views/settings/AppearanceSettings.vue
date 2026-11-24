<script setup lang="ts">
// Settings › Appearance (spec 8.13).
// - "Style" (Look 3): UiRadioCards (Loom, Pelican) bound to useLook. Each card shows a 96×56 mock of
//   that style in the theme showing now: Loom's inset panel, or Pelican's black top bar, navy sidebar
//   and grey page.
// - "Theme": UiRadioCards (System, Light, Dark) bound to useTheme, with mocks of the current style in
//   light and dark; System shows both halves. Both styles have light and dark colours.
//   The mocks' colours come from the token rules in styles/tokens.css and styles/pelican.css, read
//   once through the CSSOM (lib/tokenPalettes.ts), so this file holds no colour values and the
//   previews follow any change to the tokens.
// - "Density" (Look 2): UiRadioCards (Comfortable, Compact) bound to useDensity, each with a mock of
//   rows at that spacing.
// - "Single-key shortcuts": UiSwitch bound to shortcutsEnabled (taskloom.shortcuts.singleKey,
//   default on).
// All are saved in this browser only.
import { computed, useId } from 'vue'
import LookMock, { type MockPalette } from '../../components/settings/LookMock.vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiRadioCards, { type RadioCardOption } from '../../components/ui/UiRadioCards.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useDensity } from '../../composables/useDensity'
import { useLook, type Look } from '../../composables/useLook'
import { shortcutsEnabled } from '../../composables/useShortcut'
import { useTheme } from '../../composables/useTheme'
import { readTokens, type Mode } from '../../lib/tokenPalettes'
import type { ThemePreference } from '../../types'

const ROOT_TOKENS = [
  '--bg-canvas', '--bg-page', '--bg-surface', '--accent', '--border-default', '--text-tertiary',
  '--brandbar-bg', '--brandbar-rule', '--brandbar-fg', '--inset',
] as const
const SIDEBAR_TOKENS = ['--bg-canvas', '--border-default', '--bg-selected', '--text-primary'] as const

function paletteOf(look: Look, mode: Mode): MockPalette {
  const t = readTokens(look, mode, ROOT_TOKENS)
  const side = readTokens(look, mode, SIDEBAR_TOKENS, '.app-sidebar')
  return {
    canvas: t['--bg-canvas'],
    page: t['--bg-page'],
    surface: t['--bg-surface'],
    accent: t['--accent'],
    line: t['--border-default'],
    ink: t['--text-tertiary'],
    sidebar: side['--bg-canvas'],
    sidebarLine: side['--border-default'],
    sidebarActive: side['--bg-selected'],
    sidebarText: side['--text-primary'],
    bar: t['--brandbar-bg'],
    barRule: t['--brandbar-rule'],
    barText: t['--brandbar-fg'],
    inset: parseFloat(t['--inset']) > 0,
  }
}

const palettes: Record<Look, Record<Mode, MockPalette>> = {
  loom: { light: paletteOf('loom', 'light'), dark: paletteOf('loom', 'dark') },
  pelican: { light: paletteOf('pelican', 'light'), dark: paletteOf('pelican', 'dark') },
}

const { look, setLook } = useLook()
const { preference, resolved, setPreference } = useTheme()

const lookModel = computed<string>({
  get: () => look.value,
  set: (v) => {
    if (v === 'loom' || v === 'pelican') setLook(v)
  },
})

const lookOptions: RadioCardOption[] = [
  { value: 'loom', label: 'Loom', description: "Taskloom's own" },
  { value: 'pelican', label: 'Pelican', description: "Louisiana's design system" },
]

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

/** A theme card's mock: the current style in that theme; System clips the dark one to the right half. */
function halves(value: string): { palette: MockPalette; clip?: string }[] {
  const p = palettes[look.value]
  if (value === 'light') return [{ palette: p.light }]
  if (value === 'dark') return [{ palette: p.dark }]
  return [{ palette: p.light }, { palette: p.dark, clip: `url(#${clipId})` }]
}

/** A style card's mock: that style in the theme showing now. */
const lookPalette = (value: string): MockPalette => palettes[value === 'pelican' ? 'pelican' : 'loom'][resolved.value]
</script>

<template>
  <SettingsPage title="Appearance" description="How Taskloom looks and listens to keys. Saved in this browser.">
    <section class="block" aria-labelledby="appearance-style">
      <h3 id="appearance-style" class="block-title">Style</h3>
      <p class="block-text">Pelican dresses Taskloom in the State of Louisiana's design system: Public Sans, a black top bar and a navy sidebar.</p>
      <UiRadioCards v-model="lookModel" name="look" aria-label="Style" :options="lookOptions">
        <template #preview="{ option }">
          <span class="stage">
            <svg class="mock" viewBox="0 0 96 56" aria-hidden="true" focusable="false">
              <LookMock :palette="lookPalette(option.value)" />
            </svg>
          </span>
        </template>
      </UiRadioCards>
    </section>

    <section class="block" aria-labelledby="appearance-theme">
      <h3 id="appearance-theme" class="block-title">Theme</h3>
      <p class="block-text">Light and dark work with either style.</p>
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
                <LookMock :palette="h.palette" />
              </g>
              <line v-if="option.value === 'system'" x1="48" y1="0.5" x2="48" y2="55.5" :style="{ stroke: palettes[look].dark.line }" />
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

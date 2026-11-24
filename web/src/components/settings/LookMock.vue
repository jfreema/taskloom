<script setup lang="ts">
// A 96×56 picture of the app in one style and theme, drawn inside Settings › Appearance's previews (an
// SVG <g>, so a caller can clip two of them into a half-and-half "System" card). The colours come in
// resolved (lib/tokenPalettes.ts), so this file holds none.
// Loom: the sidebar's lines on the canvas, and an inset rounded panel. Pelican: a black top bar with a
// gold rule, a navy sidebar with its blue current row, and a grey page under a white title band.
import { computed, useId } from 'vue'

export interface MockPalette {
  canvas: string
  page: string
  surface: string
  accent: string
  line: string
  ink: string
  sidebar: string
  sidebarLine: string
  sidebarActive: string
  sidebarText: string
  /** Pelican's top bar, its rule and its text; empty without one. */
  bar: string
  barRule: string
  barText: string
  /** Loom's inset content panel; Pelican's is edge to edge. */
  inset: boolean
}

const props = defineProps<{ palette: MockPalette }>()

const p = computed(() => props.palette)
const top = computed(() => (p.value.bar ? 8.25 : 0))
const ROWS = [0, 9, 18] as const
const clipId = `${useId()}-frame`
</script>

<template>
  <g>
    <defs>
      <clipPath :id="clipId"><rect width="96" height="56" rx="6" /></clipPath>
    </defs>
    <g :clip-path="`url(#${clipId})`">
      <rect width="96" height="56" :style="{ fill: p.canvas }" />

      <!-- Loom: lines on the canvas, and the inset panel with its rows. -->
      <template v-if="p.inset">
        <rect x="6" y="9" width="14" height="3" rx="1.5" :style="{ fill: p.ink }" opacity="0.55" />
        <rect x="6" y="16" width="17" height="3" rx="1.5" :style="{ fill: p.line }" />
        <rect x="6" y="23" width="12" height="3" rx="1.5" :style="{ fill: p.line }" />
        <rect x="6" y="30" width="15" height="3" rx="1.5" :style="{ fill: p.line }" />
        <rect x="27" y="5" width="64" height="46" rx="4" :style="{ fill: p.page }" />
        <rect x="32" y="10" width="22" height="4" rx="2" :style="{ fill: p.ink }" opacity="0.7" />
        <rect x="77" y="9.5" width="10" height="5" rx="2" :style="{ fill: p.accent }" />
        <g v-for="dy in ROWS" :key="dy">
          <circle cx="35" :cy="21.5 + dy" r="2" :style="{ fill: p.accent }" />
          <rect x="40" :y="20 + dy" :width="dy === 9 ? 26 : 36" height="3" rx="1.5" :style="{ fill: p.line }" />
        </g>
      </template>

      <!-- Pelican: the top bar, the sidebar, a title band and a card on the page. -->
      <template v-else>
        <template v-if="p.bar">
          <rect width="96" height="7" :style="{ fill: p.bar }" />
          <rect y="7" width="96" height="1.25" :style="{ fill: p.barRule }" />
          <rect x="4" y="2.5" width="12" height="2" rx="1" :style="{ fill: p.barText }" />
        </template>
        <rect x="0" :y="top" width="24" :height="56 - top" :style="{ fill: p.sidebar }" />
        <rect x="4" :y="top + 5" width="13" height="2.5" rx="1.25" :style="{ fill: p.sidebarLine }" />
        <rect x="0" :y="top + 10.5" width="24" height="5.5" :style="{ fill: p.sidebarActive }" />
        <rect x="4" :y="top + 12" width="15" height="2.5" rx="1.25" :style="{ fill: p.sidebarText }" />
        <rect x="4" :y="top + 19" width="11" height="2.5" rx="1.25" :style="{ fill: p.sidebarLine }" />
        <rect x="4" :y="top + 25" width="14" height="2.5" rx="1.25" :style="{ fill: p.sidebarLine }" />
        <rect x="24" :y="top" width="72" height="9" :style="{ fill: p.surface }" />
        <rect x="24" :y="top + 9" width="72" height="0.75" :style="{ fill: p.line }" />
        <rect x="28" :y="top + 3" width="20" height="3.5" rx="1" :style="{ fill: p.accent }" />
        <rect x="28.25" :y="top + 14.25" width="63.5" height="27" rx="1" :style="{ fill: p.surface, stroke: p.line }" stroke-width="0.5" />
        <rect x="28" :y="top + 14" width="64" height="1.5" :style="{ fill: p.accent }" />
        <g v-for="dy in ROWS" :key="dy">
          <rect x="32" :y="top + 20 + dy * 0.8" width="3" height="3" rx="0.5" :style="{ fill: 'none', stroke: p.ink }" stroke-width="0.6" />
          <rect x="38" :y="top + 20.6 + dy * 0.8" :width="dy === 9 ? 28 : 40" height="1.75" rx="0.875" :style="{ fill: p.line }" />
        </g>
      </template>
    </g>
    <rect x="0.5" y="0.5" width="95" height="55" rx="5.5" fill="none" :style="{ stroke: p.line }" />
  </g>
</template>

<script setup lang="ts">
// The one shared tooltip (spec 7.10), driven by the v-tooltip directive (tooltip.ts) and mounted
// once in App.vue. 12/16 on --bg-inverse, max width 280, with an optional inverse UiKbd.
import { nextTick, ref, watch } from 'vue'
import { computePosition, flip, offset, shift } from '@floating-ui/vue'
import { TOOLTIP_ID, tooltipState } from './tooltip'
import UiKbd from './UiKbd.vue'

const el = ref<HTMLElement | null>(null)
const pos = ref({ x: 0, y: 0, placed: false })
let run = 0

async function place(): Promise<void> {
  const anchor = tooltipState.anchor
  const floating = el.value
  if (!tooltipState.visible || !anchor || !floating) return
  const ticket = ++run
  const { x, y } = await computePosition(anchor, floating, {
    strategy: 'fixed',
    placement: tooltipState.placement,
    middleware: [offset(6), flip({ padding: 8 }), shift({ padding: 8 })],
  })
  if (ticket !== run) return
  pos.value = { x: Math.round(x), y: Math.round(y), placed: true }
}

watch(
  () => [tooltipState.visible, tooltipState.anchor, tooltipState.text, tooltipState.kbd, tooltipState.placement],
  async () => {
    if (!tooltipState.visible) {
      pos.value = { ...pos.value, placed: false }
      return
    }
    await nextTick()
    await place()
  },
)
</script>

<template>
  <Teleport to="#overlay-root">
    <div
      :id="TOOLTIP_ID"
      ref="el"
      role="tooltip"
      class="ui-tooltip surface-inverse"
      :class="{ 'is-visible': tooltipState.visible && pos.placed }"
      :style="{ transform: `translate3d(${pos.x}px, ${pos.y}px, 0)` }"
    >
      <span class="text">{{ tooltipState.text }}</span>
      <UiKbd v-if="tooltipState.kbd" :keys="tooltipState.kbd" variant="inverse" />
    </div>
  </Teleport>
</template>

<style scoped>
.ui-tooltip {
  position: fixed;
  top: 0;
  left: 0;
  z-index: var(--z-tooltip);
  display: flex;
  align-items: center;
  gap: var(--space-1-5);
  max-width: 280px;
  padding: var(--space-1) var(--space-2);
  border-radius: var(--radius-sm);
  box-shadow: var(--shadow-3);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  pointer-events: none;
  visibility: hidden;
  opacity: 0;
  transition:
    opacity var(--dur-2) var(--ease-out),
    visibility 0s linear var(--dur-2);
}

.ui-tooltip.is-visible {
  visibility: visible;
  opacity: 1;
  transition:
    opacity var(--dur-2) var(--ease-out),
    visibility 0s;
}

.text {
  min-width: 0;
  overflow-wrap: anywhere;
}
</style>

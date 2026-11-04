<script setup lang="ts">
// Status icon (7.2, 7.11). The shape carries the category, so colour is never the only cue:
// - Not started: an empty ring.
// - Active: the ring and a pie filled clockwise from 12 o'clock to (i+1)/(n+1), where n is the
//   number of Active statuses in the list and i this status's index among them (display order).
// - Done: a filled disc with a white check.
// Moving into Done pops the icon and draws the check in (7.8; a plain change under reduced motion).
import { computed, ref, watch } from 'vue'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status } from '../../types'

const props = withDefaults(
  defineProps<{
    status: Status
    size?: 14 | 16 | 18 | 20
    /** aria-hidden (the default); otherwise role="img" named by the status. */
    decorative?: boolean
  }>(),
  { size: 16, decorative: true },
)

const workspaces = useWorkspaces()

/** The pie's share for an Active status. */
const fraction = computed(() => {
  if (props.status.category !== 'Active') return 0
  const active = workspaces.statusesFor(props.status.listId).filter((s) => s.category === 'Active')
  const i = active.findIndex((s) => s.id === props.status.id)
  if (i === -1) return 0.5
  return (i + 1) / (active.length + 1)
})

const CX = 8
const CY = 8
const PIE_R = 3.75

/** A wedge from 12 o'clock, clockwise. */
const piePath = computed(() => {
  const f = Math.min(Math.max(fraction.value, 0), 0.999)
  const angle = -Math.PI / 2 + f * 2 * Math.PI
  const x = CX + PIE_R * Math.cos(angle)
  const y = CY + PIE_R * Math.sin(angle)
  const large = f > 0.5 ? 1 : 0
  return `M${CX} ${CY} L${CX} ${CY - PIE_R} A${PIE_R} ${PIE_R} 0 ${large} 1 ${x.toFixed(3)} ${y.toFixed(3)} Z`
})

const popping = ref(false)
watch(
  () => props.status.category,
  (now, before) => {
    if (now === 'Done' && before !== undefined && before !== 'Done') popping.value = true
  },
)
</script>

<template>
  <svg
    class="status-icon"
    :class="{ 'is-popping': popping }"
    :style="{ '--c': status.color }"
    :width="size"
    :height="size"
    viewBox="0 0 16 16"
    fill="none"
    focusable="false"
    :role="decorative ? undefined : 'img'"
    :aria-label="decorative ? undefined : status.name"
    :aria-hidden="decorative ? 'true' : undefined"
    @animationend="popping = false"
  >
    <template v-if="status.category === 'Done'">
      <circle :cx="CX" :cy="CY" r="7" class="disc" />
      <path d="M5 8.2 7 10.2 11 6" class="check" pathLength="1" />
    </template>
    <template v-else>
      <circle :cx="CX" :cy="CY" r="6.25" class="ring" />
      <path v-if="status.category === 'Active'" :d="piePath" class="pie" />
    </template>
  </svg>
</template>

<style scoped>
.status-icon {
  display: block;
  flex-shrink: 0;
  overflow: visible;
}

.ring {
  stroke: var(--c);
  stroke-width: 1.5;
}

.pie,
.disc {
  fill: var(--c);
}

/* A white check in both themes (--danger-fg is #FFFFFF in light and dark). */
.check {
  stroke: var(--danger-fg);
  stroke-width: 1.75;
  stroke-linecap: round;
  stroke-linejoin: round;
  fill: none;
}

@media (prefers-reduced-motion: no-preference) {
  .status-icon.is-popping {
    animation: status-pop var(--dur-4) var(--ease-out);
  }

  .is-popping .check {
    stroke-dasharray: 1;
    animation: status-check var(--dur-4) var(--ease-out);
  }
}

@keyframes status-pop {
  0% {
    transform: scale(0.8);
  }
  60% {
    transform: scale(1.12);
  }
  100% {
    transform: scale(1);
  }
}

@keyframes status-check {
  from {
    stroke-dashoffset: 1;
  }
  to {
    stroke-dashoffset: 0;
  }
}
</style>

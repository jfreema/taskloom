<script setup lang="ts">
// Complete toggle (7.2 "Status versus complete", 7.11): completes and reopens a task.
// - Open: a 1.5px ring in the status colour (--border-control without one) that always holds an
//   outline check in --text-tertiary; hover fills the ring with --bg-hover and darkens the check.
// - Done: an --accent disc with an --accent-fg check, which pops and draws in when it becomes done
//   (7.8; a plain colour change under reduced motion).
// role="checkbox" named "Complete “<title>”". The ring is `size` px inside a 24px target (44px on
// touch). `readonly` (guests, a task being deleted) renders the mark with no button.
import { computed, ref, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    done: boolean
    /** The task title. */
    label: string
    /** The task's status colour. */
    color?: string
    size?: 16 | 18 | 20
    disabled?: boolean
    readonly?: boolean
  }>(),
  { color: undefined, size: 18, disabled: false, readonly: false },
)

const emit = defineEmits<{ toggle: [] }>()

const popping = ref(false)
watch(
  () => props.done,
  (now, before) => {
    if (now && !before) popping.value = true
  },
)

const style = computed(() => ({
  '--ring': props.color ?? 'var(--border-control)',
  '--mark': `${props.size}px`,
}))

function onClick(): void {
  if (props.disabled) return
  emit('toggle')
}
</script>

<template>
  <span
    v-if="readonly"
    class="complete-toggle is-static"
    :class="{ 'is-done': done }"
    :style="style"
    role="img"
    :aria-label="done ? 'Completed' : 'Not completed'"
  >
    <svg class="mark" viewBox="0 0 16 16" fill="none" focusable="false" aria-hidden="true">
      <circle v-if="done" class="disc" cx="8" cy="8" r="7" />
      <circle v-else class="ring" cx="8" cy="8" r="6.25" />
      <path class="check" d="M5 8.2 7 10.2 11 6" pathLength="1" />
    </svg>
  </span>
  <button
    v-else
    type="button"
    role="checkbox"
    class="complete-toggle"
    :class="{ 'is-done': done, 'is-popping': popping, 'is-disabled': disabled }"
    :style="style"
    :aria-checked="done ? 'true' : 'false'"
    :aria-label="`Complete “${label}”`"
    :aria-disabled="disabled ? 'true' : undefined"
    @click.stop="onClick"
  >
    <svg class="mark" viewBox="0 0 16 16" fill="none" focusable="false" aria-hidden="true" @animationend="popping = false">
      <circle v-if="done" class="disc" cx="8" cy="8" r="7" />
      <circle v-else class="ring" cx="8" cy="8" r="6.25" />
      <path class="check" d="M5 8.2 7 10.2 11 6" pathLength="1" />
    </svg>
  </button>
</template>

<style scoped>
.complete-toggle {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: max(var(--control-xs), var(--mark));
  height: max(var(--control-xs), var(--mark));
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: transparent;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
}

.complete-toggle.is-static {
  cursor: default;
}

.complete-toggle.is-disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.mark {
  display: block;
  width: var(--mark);
  height: var(--mark);
  overflow: visible;
}

.ring {
  fill: transparent;
  stroke: var(--ring);
  stroke-width: 1.5;
  transition: fill var(--dur-1) var(--ease-out);
}

.check {
  stroke: var(--text-tertiary);
  stroke-width: 1.75;
  stroke-linecap: round;
  stroke-linejoin: round;
  transition: stroke var(--dur-1) var(--ease-out);
}

.complete-toggle:not(.is-static, .is-disabled):hover .ring {
  fill: var(--bg-hover);
}

.complete-toggle:not(.is-static, .is-disabled):hover .check {
  stroke: var(--text-secondary);
}

.disc {
  fill: var(--accent);
}

.is-done .check {
  stroke: var(--accent-fg);
}

@media (prefers-reduced-motion: no-preference) {
  .is-popping .mark {
    animation: complete-pop var(--dur-4) var(--ease-out);
  }

  .is-popping .check {
    stroke-dasharray: 1;
    animation: complete-check var(--dur-4) var(--ease-out);
  }
}

@keyframes complete-pop {
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

@keyframes complete-check {
  from {
    stroke-dashoffset: 1;
  }
  to {
    stroke-dashoffset: 0;
  }
}

/* Fingers get a 44px target (7.5). */
@media (pointer: coarse) {
  .complete-toggle:not(.is-static)::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: var(--touch-min);
    height: var(--touch-min);
    transform: translate(-50%, -50%);
  }
}
</style>

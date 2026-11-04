<script setup lang="ts">
// Icon-only button (spec 7.10). `label` is required: it is the accessible name and the tooltip.
// `pressed` sets aria-pressed when defined. Below 44px the hit area grows on coarse pointers.
import { computed, type Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import UiIcon from './UiIcon.vue'

const props = withDefaults(
  defineProps<{
    icon: Component
    label: string
    size?: 'xs' | 'sm' | 'md' | 'lg'
    variant?: 'ghost' | 'secondary' | 'primary' | 'danger-ghost'
    pressed?: boolean
    /** Shown in the tooltip. */
    kbd?: string
    tooltip?: boolean
    disabled?: boolean
    to?: RouteLocationRaw
    tone?: 'default' | 'inverse'
  }>(),
  {
    size: 'sm',
    variant: 'ghost',
    pressed: undefined,
    kbd: undefined,
    tooltip: true,
    disabled: false,
    to: undefined,
    tone: 'default',
  },
)

const emit = defineEmits<{ click: [e: MouseEvent] }>()

const iconSize = computed(() => ({ xs: 14, sm: 16, md: 16, lg: 20 } as const)[props.size])
const tip = computed(() => (props.tooltip ? { text: props.label, kbd: props.kbd } : null))
const classes = computed(() => [
  'ui-icon-button',
  `ui-icon-button--${props.variant}`,
  `ui-icon-button--${props.size}`,
  {
    'ui-icon-button--inverse': props.tone === 'inverse',
    'is-pressed': props.pressed === true,
    'is-disabled': props.disabled,
  },
])

function onClick(e: MouseEvent): void {
  if (props.disabled) {
    e.preventDefault()
    return
  }
  emit('click', e)
}

function onLinkClick(e: MouseEvent, navigate: (e?: MouseEvent) => unknown): void {
  if (props.disabled) {
    e.preventDefault()
    return
  }
  emit('click', e)
  if (!e.defaultPrevented) void navigate(e)
}
</script>

<template>
  <RouterLink v-if="to" v-slot="{ href, navigate }" :to="to" custom>
    <a
      v-tooltip="tip"
      :class="classes"
      :href="disabled ? undefined : href"
      :role="disabled ? 'link' : undefined"
      :aria-label="label"
      :aria-disabled="disabled ? 'true' : undefined"
      @click="onLinkClick($event, navigate)"
    >
      <UiIcon :icon="icon" :size="iconSize" />
    </a>
  </RouterLink>
  <button
    v-else
    v-tooltip="tip"
    type="button"
    :class="classes"
    :aria-label="label"
    :aria-pressed="pressed === undefined ? undefined : pressed ? 'true' : 'false'"
    :disabled="disabled"
    @click="onClick"
  >
    <UiIcon :icon="icon" :size="iconSize" />
  </button>
</template>

<style scoped>
.ui-icon-button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: var(--control-sm);
  height: var(--control-sm);
  padding: 0;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background-color: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.ui-icon-button--xs {
  width: var(--control-xs);
  height: var(--control-xs);
  border-radius: var(--radius-xs);
}

.ui-icon-button--md {
  width: var(--control-md);
  height: var(--control-md);
}

.ui-icon-button--lg {
  width: var(--control-lg);
  height: var(--control-lg);
  border-radius: var(--radius-md);
}

/* Ghost */
.ui-icon-button--ghost:not(.is-disabled):hover {
  background-color: var(--bg-hover);
  color: var(--text-primary);
}

.ui-icon-button--ghost:not(.is-disabled):active {
  background-color: var(--bg-pressed);
  color: var(--text-primary);
}

/* Secondary */
.ui-icon-button--secondary {
  background-color: var(--bg-surface);
  border-color: var(--border-default);
  box-shadow: var(--shadow-1);
  color: var(--text-primary);
}

.ui-icon-button--secondary:not(.is-disabled):hover {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
}

.ui-icon-button--secondary:not(.is-disabled):active {
  background-image: linear-gradient(var(--bg-pressed), var(--bg-pressed));
}

/* Primary */
.ui-icon-button--primary {
  background-color: var(--accent);
  color: var(--accent-fg);
}

.ui-icon-button--primary:not(.is-disabled):hover {
  background-color: var(--accent-hover);
}

.ui-icon-button--primary:not(.is-disabled):active {
  background-color: var(--accent-pressed);
}

/* Danger ghost */
.ui-icon-button--danger-ghost {
  color: var(--danger-text);
}

.ui-icon-button--danger-ghost:not(.is-disabled):hover,
.ui-icon-button--danger-ghost:not(.is-disabled):active {
  background-color: var(--danger-subtle);
}

/* A toggle that is on. */
.ui-icon-button.is-pressed:not(.ui-icon-button--primary) {
  background-color: var(--bg-selected);
  color: var(--accent-text);
}

/* Inverse tone, on --bg-inverse. */
.ui-icon-button--inverse {
  color: var(--text-inverse-secondary);
}

.ui-icon-button--inverse:not(.is-disabled):hover,
.ui-icon-button--inverse:not(.is-disabled):active {
  background-color: color-mix(in srgb, var(--text-inverse) 12%, transparent);
  color: var(--text-inverse);
}

.ui-icon-button--inverse:focus-visible {
  outline-color: var(--focus-ring-inverse);
}

.ui-icon-button.is-disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

@media (pointer: coarse) {
  .ui-icon-button::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: max(100%, var(--touch-min));
    height: max(100%, var(--touch-min));
    transform: translate(-50%, -50%);
  }
}
</style>

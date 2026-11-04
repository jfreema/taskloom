<script setup lang="ts">
// Text button (spec 7.10). `to` renders a link with the same look. `click` never fires while
// loading or disabled. Loading keeps the width, shows a spinner and sets aria-busy.
import { computed, type Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import UiIcon from './UiIcon.vue'
import UiKbd from './UiKbd.vue'
import UiSpinner from './UiSpinner.vue'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-ghost' | 'link'
    size?: 'xs' | 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit'
    icon?: Component
    iconRight?: Component
    loading?: boolean
    disabled?: boolean
    block?: boolean
    /** Renders a RouterLink with the same look. */
    to?: RouteLocationRaw
    /** Renders a plain link for full-page navigation (sign-in providers, downloads). */
    href?: string
    /** A trailing key hint, hidden below 640px. */
    kbd?: string
    /** 'inverse' is for ghost and link buttons on --bg-inverse (toasts, bulk bar). */
    tone?: 'default' | 'inverse'
  }>(),
  {
    variant: 'secondary',
    size: 'md',
    type: 'button',
    icon: undefined,
    iconRight: undefined,
    loading: false,
    disabled: false,
    block: false,
    to: undefined,
    href: undefined,
    kbd: undefined,
    tone: 'default',
  },
)

const emit = defineEmits<{ click: [e: MouseEvent] }>()

const iconSize = computed(() => (props.size === 'xs' ? 14 : 16))
const classes = computed(() => [
  'ui-button',
  `ui-button--${props.variant}`,
  `ui-button--${props.size}`,
  {
    'ui-button--block': props.block,
    'ui-button--inverse': props.tone === 'inverse',
    'is-disabled': props.disabled,
    'is-loading': props.loading,
  },
])

function onClick(e: MouseEvent): void {
  if (props.disabled || props.loading) {
    e.preventDefault()
    e.stopImmediatePropagation()
    return
  }
  emit('click', e)
}

function onAnchorClick(e: MouseEvent): void {
  if (props.disabled || props.loading) {
    e.preventDefault()
    return
  }
  emit('click', e)
}

function onLinkClick(e: MouseEvent, navigate: (e?: MouseEvent) => unknown): void {
  if (props.disabled || props.loading) {
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
      :class="classes"
      :href="disabled ? undefined : href"
      :role="disabled ? 'link' : undefined"
      :aria-disabled="disabled || loading ? 'true' : undefined"
      :aria-busy="loading ? 'true' : undefined"
      @click="onLinkClick($event, navigate)"
    >
      <span v-if="loading && !icon" class="overlay"><UiSpinner :size="iconSize" /></span>
      <UiSpinner v-if="loading && icon" :size="iconSize" />
      <UiIcon v-else-if="icon" :icon="icon" :size="iconSize" />
      <span v-if="$slots.default" class="label"><slot /></span>
      <UiIcon v-if="iconRight" :icon="iconRight" :size="iconSize" />
      <span v-if="kbd" class="kbd" aria-hidden="true"><UiKbd :keys="kbd" /></span>
    </a>
  </RouterLink>
  <a
    v-else-if="href"
    :class="classes"
    :href="disabled ? undefined : href"
    :role="disabled ? 'link' : undefined"
    :aria-disabled="disabled || loading ? 'true' : undefined"
    :aria-busy="loading ? 'true' : undefined"
    @click="onAnchorClick"
  >
    <span v-if="loading && !icon" class="overlay"><UiSpinner :size="iconSize" /></span>
    <UiSpinner v-if="loading && icon" :size="iconSize" />
    <UiIcon v-else-if="icon" :icon="icon" :size="iconSize" />
    <span v-if="$slots.default" class="label"><slot /></span>
    <UiIcon v-if="iconRight" :icon="iconRight" :size="iconSize" />
    <span v-if="kbd" class="kbd" aria-hidden="true"><UiKbd :keys="kbd" /></span>
  </a>
  <button
    v-else
    :type="type"
    :class="classes"
    :disabled="disabled"
    :aria-disabled="loading ? 'true' : undefined"
    :aria-busy="loading ? 'true' : undefined"
    @click="onClick"
  >
    <span v-if="loading && !icon" class="overlay"><UiSpinner :size="iconSize" /></span>
    <UiSpinner v-if="loading && icon" :size="iconSize" />
    <UiIcon v-else-if="icon" :icon="icon" :size="iconSize" />
    <span v-if="$slots.default" class="label"><slot /></span>
    <UiIcon v-if="iconRight" :icon="iconRight" :size="iconSize" />
    <span v-if="kbd" class="kbd" aria-hidden="true"><UiKbd :keys="kbd" /></span>
  </button>
</template>

<style scoped>
.ui-button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-1-5);
  height: var(--control-md);
  padding: 0 var(--space-3);
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background-color: transparent;
  background-image: none;
  color: var(--text-primary);
  font-family: var(--font-sans);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  text-decoration: none;
  cursor: pointer;
  user-select: none;
  -webkit-tap-highlight-color: transparent;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Sizes: 24 / 28 / 32 / 40; padding 8 / 10 / 12 / 16. */
.ui-button--xs {
  height: var(--control-xs);
  padding: 0 var(--space-2);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.ui-button--sm {
  height: var(--control-sm);
  padding: 0 10px;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.ui-button--lg {
  height: var(--control-lg);
  padding: 0 var(--space-4);
  border-radius: var(--radius-md);
}

.ui-button--block {
  display: flex;
  width: 100%;
}

/* Primary */
.ui-button--primary {
  background-color: var(--accent);
  color: var(--accent-fg);
}

.ui-button--primary:not(.is-disabled):not(.is-loading):hover {
  background-color: var(--accent-hover);
}

.ui-button--primary:not(.is-disabled):not(.is-loading):active {
  background-color: var(--accent-pressed);
}

/* Secondary */
.ui-button--secondary {
  background-color: var(--bg-surface);
  border-color: var(--border-default);
  box-shadow: var(--shadow-1);
}

.ui-button--secondary:not(.is-disabled):not(.is-loading):hover {
  background-image: linear-gradient(var(--bg-hover), var(--bg-hover));
}

.ui-button--secondary:not(.is-disabled):not(.is-loading):active {
  background-image: linear-gradient(var(--bg-pressed), var(--bg-pressed));
}

/* Ghost */
.ui-button--ghost {
  color: var(--text-secondary);
}

.ui-button--ghost:not(.is-disabled):not(.is-loading):hover {
  background-color: var(--bg-hover);
  color: var(--text-primary);
}

.ui-button--ghost:not(.is-disabled):not(.is-loading):active {
  background-color: var(--bg-pressed);
  color: var(--text-primary);
}

/* Danger */
.ui-button--danger {
  background-color: var(--danger);
  color: var(--danger-fg);
}

.ui-button--danger:not(.is-disabled):not(.is-loading):hover,
.ui-button--danger:not(.is-disabled):not(.is-loading):active {
  background-color: var(--danger-hover);
}

/* Danger ghost */
.ui-button--danger-ghost {
  color: var(--danger-text);
}

.ui-button--danger-ghost:not(.is-disabled):not(.is-loading):hover,
.ui-button--danger-ghost:not(.is-disabled):not(.is-loading):active {
  background-color: var(--danger-subtle);
}

/* Link: no padding or height. */
.ui-button--link {
  height: auto;
  padding: 0;
  border: 0;
  border-radius: var(--radius-xs);
  color: var(--accent-text);
}

.ui-button--link:not(.is-disabled):not(.is-loading):hover {
  text-decoration: underline;
  text-underline-offset: 2px;
}

/* Inverse tone, on --bg-inverse. */
.ui-button--inverse.ui-button--ghost {
  color: var(--text-inverse);
}

.ui-button--inverse.ui-button--link {
  color: var(--text-inverse-accent);
}

.ui-button--inverse:not(.is-disabled):not(.is-loading):hover,
.ui-button--inverse:not(.is-disabled):not(.is-loading):active {
  background-color: color-mix(in srgb, var(--text-inverse) 12%, transparent);
  color: inherit;
}

.ui-button--inverse.ui-button--ghost:not(.is-disabled):not(.is-loading):hover {
  color: var(--text-inverse);
}

.ui-button--inverse.ui-button--link:not(.is-disabled):not(.is-loading):hover {
  background-color: transparent;
  color: var(--text-inverse-accent);
}

.ui-button--inverse:focus-visible {
  outline-color: var(--focus-ring-inverse);
}

/* States */
.ui-button.is-disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.ui-button.is-loading {
  cursor: progress;
}

.overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Opacity, not visibility: the hidden label stays the button's accessible name. */
.ui-button.is-loading:has(.overlay) > :not(.overlay) {
  opacity: 0;
}

/* Key hints on filled buttons follow the label colour, on the button's own fill: any tint
   lowers the 11px cap's contrast (white on the light accent drops from 5.7:1 to 4.3:1). */
.ui-button--primary .kbd :deep(.cap),
.ui-button--danger .kbd :deep(.cap) {
  color: inherit;
  border-color: color-mix(in srgb, currentColor 30%, transparent);
  background: transparent;
}

.kbd {
  display: inline-flex;
  margin-left: var(--space-0-5);
  margin-right: calc(var(--space-1) * -1);
}

@media (max-width: 639px) {
  .kbd {
    display: none;
  }
}

/* At least 44px tall to the finger (7.5). */
@media (pointer: coarse) {
  .ui-button::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: max(100%, var(--touch-min));
    transform: translateY(-50%);
  }
}
</style>

<script setup lang="ts">
// Internal to the field components: the button that opens a picker. Popover trigger props (ref,
// aria-haspopup, aria-expanded, click, ArrowDown) and the caller's class fall through to it.
// - icon: a 24px square for an icon alone (List rows, cards).
// - cell: a 28px ghost cell for a value in a table column.
// - ghost: a 32px ghost row that wraps (task panel property values).
// - chip: a 28px round outlined chip (quick create, the panel's dates).
// - select: a full-width field that looks like UiSelect (dialogs and forms).
// - round: no padding, round (avatar stacks, status pills that bring their own look).
import type { TooltipValue } from '../ui/tooltip'

withDefaults(
  defineProps<{
    variant: 'icon' | 'cell' | 'ghost' | 'chip' | 'select' | 'round'
    /** The accessible name, "<Field>: <value>, change" (fieldLabel.ts). */
    label: string
    empty?: boolean
    tooltip?: TooltipValue
  }>(),
  { empty: false, tooltip: undefined },
)
</script>

<template>
  <button
    v-tooltip="tooltip"
    type="button"
    class="field-trigger"
    :class="[`field-trigger--${variant}`, { 'is-empty': empty }]"
    :aria-label="label"
  >
    <slot />
  </button>
</template>

<style scoped>
.field-trigger {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  max-width: 100%;
  padding: 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
  -webkit-tap-highlight-color: transparent;
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.field-trigger:hover,
.field-trigger[aria-expanded="true"] {
  background: var(--bg-hover);
}

.field-trigger.is-empty {
  color: var(--text-tertiary);
}

/* ---- icon ---- */
.field-trigger--icon {
  justify-content: center;
  flex-shrink: 0;
  width: var(--control-xs);
  height: var(--control-xs);
}

/* ---- cell ---- */
.field-trigger--cell {
  height: var(--control-sm);
  padding: 0 var(--space-1-5);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

/* ---- ghost ---- */
.field-trigger--ghost {
  flex-wrap: wrap;
  row-gap: var(--space-1);
  column-gap: var(--space-2);
  min-height: var(--control-md);
  padding: var(--space-1) var(--space-2);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

/* ---- chip ---- */
.field-trigger--chip {
  flex-shrink: 0;
  height: var(--control-sm);
  padding: 0 10px;
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  white-space: nowrap;
}

.field-trigger--chip:hover,
.field-trigger--chip[aria-expanded="true"] {
  background: color-mix(in srgb, var(--text-primary) 4%, var(--bg-surface));
}

.field-trigger--chip.is-empty {
  color: var(--text-secondary);
}

/* ---- select ---- */
.field-trigger--select {
  display: flex;
  gap: var(--space-2);
  width: 100%;
  height: var(--control-md);
  padding: 0 var(--space-2) 0 10px;
  border: 1px solid var(--border-default);
  border-bottom-color: var(--border-control);
  background: var(--bg-surface);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.field-trigger--select:hover,
.field-trigger--select[aria-expanded="true"] {
  background: var(--bg-surface);
  border-color: color-mix(in srgb, var(--border-control) 45%, var(--border-default));
  border-bottom-color: var(--border-control);
}

/* ---- round ---- */
.field-trigger--round {
  flex-shrink: 0;
  border-radius: var(--radius-full);
}

.field-trigger--round:hover,
.field-trigger--round[aria-expanded="true"] {
  background: transparent;
}

/* Fingers get a 44px target around small triggers (7.5). */
@media (pointer: coarse) {
  .field-trigger--icon::after,
  .field-trigger--round::after,
  .field-trigger--cell::after,
  .field-trigger--chip::after {
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

<script setup lang="ts">
// Round chip (spec 7.10). As a toggle (`selected` defined) it sets aria-pressed. A removable chip
// is a group of two buttons: the chip itself and its "Remove …" button.
import { computed, type Component } from 'vue'
import { X } from '@lucide/vue'
import UiIcon from './UiIcon.vue'

const props = withDefaults(
  defineProps<{
    label?: string
    icon?: Component
    selected?: boolean
    removable?: boolean
    removeLabel?: string
    size?: 'sm' | 'md'
    as?: 'button' | 'span'
  }>(),
  { label: undefined, icon: undefined, selected: undefined, removable: false, removeLabel: undefined, size: 'sm', as: 'button' },
)

const emit = defineEmits<{ click: [e: MouseEvent]; remove: [e: MouseEvent] }>()

const removeText = computed(() => props.removeLabel ?? `Remove ${props.label ?? ''}`.trim())
const pressed = computed(() => (props.as === 'button' && props.selected !== undefined ? (props.selected ? 'true' : 'false') : undefined))
</script>

<template>
  <span
    class="ui-chip"
    :class="[`ui-chip--${size}`, { 'is-selected': selected, 'is-removable': removable, 'is-static': as === 'span' }]"
  >
    <component
      :is="as"
      class="main"
      :type="as === 'button' ? 'button' : undefined"
      :aria-pressed="pressed"
      @click="as === 'button' ? emit('click', $event) : undefined"
    >
      <slot name="leading">
        <UiIcon v-if="icon" :icon="icon" :size="size === 'md' ? 16 : 14" />
      </slot>
      <span v-if="label || $slots.default" class="text"><slot>{{ label }}</slot></span>
    </component>
    <button
      v-if="removable"
      v-tooltip="removeText"
      type="button"
      class="remove"
      :aria-label="removeText"
      @click="emit('remove', $event)"
    >
      <UiIcon :icon="X" :size="12" />
    </button>
  </span>
</template>

<style scoped>
.ui-chip {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  max-width: 100%;
  height: var(--control-xs);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  vertical-align: middle;
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.ui-chip--md {
  height: var(--control-sm);
}

.main {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  height: 100%;
  padding: 0 10px;
  border: 0;
  border-radius: inherit;
  background: transparent;
  color: inherit;
  font: inherit;
  white-space: nowrap;
}

.ui-chip--md .main {
  padding: 0 var(--space-3);
  gap: var(--space-1-5);
}

.is-removable .main {
  padding-right: var(--space-0-5);
}

button.main {
  cursor: pointer;
}

.ui-chip:not(.is-static):hover {
  background-color: color-mix(in srgb, var(--text-primary) 4%, var(--bg-surface));
}

.text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ui-chip.is-selected {
  border-color: var(--accent);
  background: var(--bg-selected);
  color: var(--accent-text);
}

.ui-chip.is-selected:not(.is-static):hover {
  background: color-mix(in srgb, var(--accent) 8%, var(--bg-selected));
}

.main :deep(.ui-icon) {
  color: var(--text-secondary);
}

.is-selected .main :deep(.ui-icon) {
  color: inherit;
}

/* A 24px target (WCAG 2.5.8) that may overhang the small chip's 1px border; the visible hover
   circle is the 18px ::before. */
.remove {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: var(--control-xs);
  height: var(--control-xs);
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: transparent;
  color: var(--text-tertiary);
  cursor: pointer;
}

.remove::before {
  content: "";
  position: absolute;
  inset: 3px;
  border-radius: inherit;
  transition: background-color var(--dur-1) var(--ease-out);
}

.remove > :deep(.ui-icon) {
  position: relative;
}

.remove:hover {
  color: var(--text-primary);
}

.remove:hover::before {
  background: var(--bg-hover);
}

@media (pointer: coarse) {
  .main::after,
  .remove::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: max(100%, var(--touch-min));
    height: var(--touch-min);
    transform: translate(-50%, -50%);
  }
}
</style>

<script setup lang="ts">
// Key legend (spec 7.10). 'Mod' shows ⌘ on Apple platforms and Ctrl elsewhere; a space means a
// sequence ('G H' is read "G then H"). The caps are hidden from screen readers, which hear the
// spoken form instead. Inside a button, wrap UiKbd in aria-hidden so it doesn't join the name.
import { computed } from 'vue'
import { keySteps, spokenKeys } from './keys'

const props = withDefaults(defineProps<{ keys: string; variant?: 'default' | 'inverse' }>(), { variant: 'default' })

const steps = computed(() => keySteps(props.keys))
const spoken = computed(() => spokenKeys(props.keys))
</script>

<template>
  <span class="ui-kbd" :class="`ui-kbd--${variant}`">
    <kbd v-for="(step, i) in steps" :key="i" class="cap" aria-hidden="true">{{ step.display }}</kbd>
    <span class="sr-only">{{ spoken }}</span>
  </span>
</template>

<style scoped>
.ui-kbd {
  display: inline-flex;
  align-items: center;
  gap: var(--space-0-5);
  vertical-align: middle;
  flex-shrink: 0;
}

.cap {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  height: 18px;
  min-width: 18px;
  padding: 0 var(--space-1);
  border-radius: var(--radius-xs);
  border: 1px solid var(--border-subtle);
  background: var(--bg-subtle);
  color: var(--text-secondary);
  font-family: var(--font-sans);
  font-size: var(--fs-2xs);
  line-height: var(--lh-2xs);
  font-weight: var(--fw-medium);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.ui-kbd--inverse .cap {
  border-color: color-mix(in srgb, var(--text-inverse) 22%, transparent);
  background: color-mix(in srgb, var(--text-inverse) 12%, transparent);
  color: var(--text-inverse);
}
</style>

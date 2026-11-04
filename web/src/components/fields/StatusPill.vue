<script setup lang="ts">
// Status pill (7.11): the status icon then its name on the status's tint (.tint-pill, 7.2), round,
// 12/16 500, at most 160px wide with an ellipsis. `interactive` adds a ChevronDown and a hover
// tint, for a pill that opens the status picker.
import { computed } from 'vue'
import { ChevronDown } from '@lucide/vue'
import type { Status } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import StatusIcon from './StatusIcon.vue'

const props = withDefaults(
  defineProps<{
    status: Status
    size?: 20 | 24 | 28
    interactive?: boolean
    iconOnly?: boolean
  }>(),
  { size: 24, interactive: false, iconOnly: false },
)

const iconSize = computed(() => (props.size === 28 ? 16 : 14))
</script>

<template>
  <span
    class="status-pill tint-pill"
    :class="[`status-pill--${size}`, { 'is-interactive': interactive, 'is-icon-only': iconOnly }]"
    :style="{ '--c': status.color }"
  >
    <StatusIcon :status="status" :size="iconSize" />
    <span v-if="!iconOnly" class="name">{{ status.name }}</span>
    <UiIcon v-if="interactive" class="chevron" :icon="ChevronDown" :size="14" />
  </span>
</template>

<style scoped>
.status-pill {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  max-width: 160px;
  height: 24px;
  padding: 0 var(--space-2) 0 var(--space-1-5);
  border-radius: var(--radius-full);
  color: var(--text-primary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  white-space: nowrap;
  vertical-align: middle;
  transition: background-color var(--dur-1) var(--ease-out);
}

.status-pill--20 {
  height: 20px;
  padding: 0 var(--space-1-5) 0 var(--space-1);
}

.status-pill--28 {
  height: 28px;
  gap: var(--space-1-5);
  padding: 0 10px 0 var(--space-2);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.status-pill.is-icon-only {
  justify-content: center;
  width: 24px;
  padding: 0;
}

.status-pill--20.is-icon-only {
  width: 20px;
}

.status-pill--28.is-icon-only {
  width: 28px;
}

.name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

.chevron {
  flex-shrink: 0;
  margin-right: calc(var(--space-0-5) * -1);
  color: var(--text-secondary);
}

.is-interactive {
  cursor: pointer;
}

.is-interactive:hover {
  background: color-mix(in srgb, var(--c) calc(var(--tint-pill) + 8%), var(--bg-surface));
}
</style>

<script setup lang="ts">
// One palette result (8.11), 40px: the status icon, space tile or action icon, the label with the
// query's match in weight 600, the location in --text-tertiary, then the due date, the key hint or
// the current-theme check. The palette's input keeps focus (aria-activedescendant), so a press on
// the row never takes it.
import { computed } from 'vue'
import { Check } from '@lucide/vue'
import DueDate from '../fields/DueDate.vue'
import SpaceTile from '../fields/SpaceTile.vue'
import StatusIcon from '../fields/StatusIcon.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiKbd from '../ui/UiKbd.vue'
import { highlight } from './paletteMatch'
import type { PaletteOption } from './usePaletteItems'

const props = defineProps<{
  option: PaletteOption
  id: string
  active: boolean
  query: string
}>()

const segments = computed(() =>
  props.option.highlight ? highlight(props.option.label, props.query) : [{ text: props.option.label, match: false }],
)
</script>

<template>
  <div
    :id="id"
    class="palette-option"
    :class="[`kind-${option.kind}`, { 'is-active': active, 'is-done': option.done }]"
    role="option"
    :aria-selected="active ? 'true' : 'false'"
  >
    <span class="lead">
      <StatusIcon v-if="option.status" :status="option.status" :size="16" />
      <SpaceTile v-else-if="option.space" :space="option.space" :size="16" />
      <UiIcon v-else-if="option.icon" :icon="option.icon" :size="16" />
    </span>
    <span class="label">
      <template v-for="(seg, i) in segments" :key="i">
        <span v-if="seg.match" class="hit">{{ seg.text }}</span>
        <template v-else>{{ seg.text }}</template>
      </template>
    </span>
    <span v-if="option.meta" class="meta">{{ option.meta }}</span>
    <span v-if="option.kind === 'retry'" class="retry">Try again</span>
    <span v-if="option.due" class="due"><DueDate :date="option.due" :done="option.done" size="sm" /></span>
    <template v-if="option.current">
      <UiIcon class="check" :icon="Check" :size="16" />
      <span class="sr-only">(current)</span>
    </template>
    <span v-if="option.kbd" class="kbd" aria-hidden="true"><UiKbd :keys="option.kbd" /></span>
  </div>
</template>

<style scoped>
.palette-option {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: 40px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  cursor: pointer;
  user-select: none;
  -webkit-tap-highlight-color: transparent;
}

.palette-option.is-active {
  background: var(--bg-selected);
}

.lead {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 16px;
  color: var(--text-secondary);
}

.label {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.hit {
  font-weight: var(--fw-semibold);
}

.is-done .label {
  color: var(--text-tertiary);
}

.meta {
  flex: 0 1 auto;
  min-width: 0;
  max-width: 40%;
  overflow: hidden;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.due,
.kbd {
  display: inline-flex;
  flex: none;
  align-items: center;
}

.due {
  justify-content: flex-end;
  min-width: 56px;
}

.check {
  flex: none;
  color: var(--accent-text);
}

.kind-create,
.kind-more {
  color: var(--accent-text);
}

.kind-create .lead,
.kind-more .lead {
  color: currentColor;
}

.kind-retry .lead {
  color: var(--danger-text);
}

.retry {
  flex: none;
  color: var(--accent-text);
  font-weight: var(--fw-medium);
}

@media (max-width: 639px) {
  .palette-option {
    min-height: var(--touch-min);
  }

  .kbd {
    display: none;
  }
}
</style>

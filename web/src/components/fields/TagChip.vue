<script setup lang="ts">
// Tag chip (7.11): the tag's tint (.tint-chip, 7.2), round, 18 or 22px tall, at most 140px wide.
// The full name shows in a tooltip when it is cut off. `removable` adds a "Remove <name>" button
// with a 24px target.
import { computed } from 'vue'
import { X } from '@lucide/vue'
import type { Tag } from '../../types'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    tag: Pick<Tag, 'name' | 'color'>
    size?: 'sm' | 'md'
    removable?: boolean
  }>(),
  { size: 'sm', removable: false },
)

const emit = defineEmits<{ remove: [e: MouseEvent] }>()

const tip = computed(() => ({ text: props.tag.name, overflowOnly: true }))
</script>

<template>
  <span class="tag-chip tint-chip" :class="[`tag-chip--${size}`, { 'is-removable': removable }]" :style="{ '--c': tag.color }">
    <span v-tooltip="tip" class="name truncate">{{ tag.name }}</span>
    <button
      v-if="removable"
      v-tooltip="`Remove ${tag.name}`"
      type="button"
      class="remove"
      :aria-label="`Remove ${tag.name}`"
      @click.stop="emit('remove', $event)"
    >
      <UiIcon :icon="X" :size="12" />
    </button>
  </span>
</template>

<style scoped>
.tag-chip {
  position: relative;
  display: inline-flex;
  flex-shrink: 1;
  align-items: center;
  min-width: 0;
  max-width: 140px;
  height: 18px;
  padding: 0 var(--space-1-5);
  border-radius: var(--radius-full);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  white-space: nowrap;
  vertical-align: middle;
}

.tag-chip--md {
  height: 22px;
  padding: 0 var(--space-2);
}

.tag-chip.is-removable {
  padding-right: var(--space-0-5);
}

.name {
  min-width: 0;
}

.remove {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  margin-left: var(--space-0-5);
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: transparent;
  color: inherit;
  cursor: pointer;
  opacity: 0.8;
}

.tag-chip--sm .remove {
  width: 14px;
  height: 14px;
}

/* A 24px target around the small visible circle (WCAG 2.5.8). */
.remove::after {
  content: "";
  position: absolute;
  left: 50%;
  top: 50%;
  width: var(--control-xs);
  height: var(--control-xs);
  transform: translate(-50%, -50%);
}

.remove:hover {
  background: color-mix(in srgb, var(--c) 28%, transparent);
  opacity: 1;
}

@media (pointer: coarse) {
  .remove::after {
    width: var(--touch-min);
    height: var(--touch-min);
  }
}
</style>

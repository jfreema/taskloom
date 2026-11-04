<script setup lang="ts">
// Priority flag (7.2, 7.11). Urgent and High use a filled flag, Normal and Low an outline one, so
// the shape carries meaning as well as the colour (icon colours only; a label beside it stays
// --text-secondary). None renders nothing unless `showEmpty`, which shows a muted outline flag.
import { computed } from 'vue'
import { Flag } from '@lucide/vue'
import { PRIORITY_LABEL, type Priority } from '../../types'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    priority: Priority
    showLabel?: boolean
    size?: 14 | 16
    showEmpty?: boolean
    /** The icon is aria-hidden even without a label (the name is written next to it). */
    decorative?: boolean
  }>(),
  { showLabel: false, size: 16, showEmpty: false, decorative: false },
)

const none = computed(() => props.priority === 'None')
const filled = computed(() => props.priority === 'Urgent' || props.priority === 'High')
/** Without a visible label the icon itself is named: "High priority", "No priority". */
const iconLabel = computed(() => {
  if (props.decorative || (props.showLabel && !none.value)) return undefined
  return none.value ? 'No priority' : `${PRIORITY_LABEL[props.priority]} priority`
})
</script>

<template>
  <span
    v-if="!none || showEmpty"
    class="priority-flag"
    :class="[`priority-flag--${priority.toLowerCase()}`, `priority-flag--${size}`]"
  >
    <UiIcon class="flag" :icon="Flag" :size="size" :filled="filled" :label="iconLabel" />
    <span v-if="showLabel && !none" class="label">{{ PRIORITY_LABEL[priority] }}</span>
  </span>
</template>

<style scoped>
.priority-flag {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-width: 0;
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  white-space: nowrap;
}

.priority-flag--14 {
  gap: var(--space-1);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.flag {
  flex-shrink: 0;
}

.priority-flag--urgent .flag {
  color: var(--priority-urgent);
}

.priority-flag--high .flag {
  color: var(--priority-high);
}

.priority-flag--normal .flag {
  color: var(--priority-normal);
}

.priority-flag--low .flag {
  color: var(--priority-low);
}

.priority-flag--none .flag {
  color: var(--text-tertiary);
}

.label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>

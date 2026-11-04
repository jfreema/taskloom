<script setup lang="ts">
// Overlapping avatars (spec 7.10) followed by a "+N" chip. The stack is one image to screen
// readers, labelled like "Jared Doe, Ana Moreno and 2 others" (lib/format.ts formatNames).
import { computed } from 'vue'
import { formatNames } from '../../lib/format'
import UiAvatar from './UiAvatar.vue'

const props = withDefaults(
  defineProps<{
    users: { id: string; displayName: string }[]
    max?: number
    size?: 16 | 20 | 24 | 28 | 32 | 40
    emptyLabel?: string
  }>(),
  { max: 3, size: 24, emptyLabel: 'Unassigned' },
)

const shown = computed(() => props.users.slice(0, Math.max(1, props.max)))
const rest = computed(() => props.users.length - shown.value.length)
const names = computed(() => props.users.map((u) => u.displayName))
const label = computed(() => (props.users.length ? formatNames(names.value) : props.emptyLabel))
const tip = computed(() => (props.users.length ? names.value.join(', ') : props.emptyLabel))
const chip = computed(() => ({
  height: `${props.size}px`,
  minWidth: `${props.size}px`,
  fontSize: props.size >= 28 ? 'var(--fs-xs)' : 'var(--fs-2xs)',
}))
</script>

<template>
  <span v-tooltip="tip" class="ui-avatar-stack" role="img" :aria-label="label">
    <template v-if="users.length">
      <UiAvatar v-for="u in shown" :key="u.id" class="item" :user="u" :size="size" ring decorative />
      <span v-if="rest > 0" class="more tabular" :style="chip" aria-hidden="true">+{{ rest }}</span>
    </template>
    <UiAvatar v-else :user="null" :size="size" decorative />
  </span>
</template>

<style scoped>
.ui-avatar-stack {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  vertical-align: middle;
}

.item + .item,
.item + .more {
  margin-left: -6px;
}

.more {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0 var(--space-1);
  border-radius: var(--radius-full);
  background: var(--bg-subtle);
  box-shadow: 0 0 0 2px var(--bg-surface), inset 0 0 0 1px var(--border-default);
  color: var(--text-secondary);
  line-height: var(--lh-2xs);
  font-weight: var(--fw-medium);
}
</style>

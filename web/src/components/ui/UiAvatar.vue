<script setup lang="ts">
// Avatar (spec 7.10): initials on a colour derived from the user id (lib/colors.ts avatarColor),
// so renaming never recolours anyone. A null user is a dashed circle labelled "Unassigned".
import { computed } from 'vue'
import { UserRoundPlus } from '@lucide/vue'
import { avatarColor } from '../../lib/colors'
import UiIcon from './UiIcon.vue'

const props = withDefaults(
  defineProps<{
    user: { id: string; displayName: string } | null
    size?: 16 | 20 | 24 | 28 | 32 | 40
    /** A 2px ring in --bg-surface, for stacks. */
    ring?: boolean
    /** A dashed ring, for invited people. */
    pending?: boolean
    /** aria-hidden, no tooltip. */
    decorative?: boolean
  }>(),
  { size: 24, ring: false, pending: false, decorative: false },
)

/** First letters of the first and last words; one letter at 20px and below. */
const letters = computed(() => {
  const words = (props.user?.displayName ?? '').trim().split(/\s+/).filter(Boolean)
  if (!words.length) return '?'
  const first = Array.from(words[0])[0] ?? ''
  const last = words.length > 1 ? (Array.from(words[words.length - 1])[0] ?? '') : ''
  const both = (first + last).toLocaleUpperCase()
  return props.size <= 20 ? (Array.from(both)[0] ?? '') : both
})

const iconSize = computed(() => {
  if (props.size <= 20) return 12
  if (props.size <= 24) return 14
  if (props.size <= 32) return 16
  return 20
})

const box = computed(() => ({
  width: `${props.size}px`,
  height: `${props.size}px`,
  fontSize: `${Math.round(props.size * 0.42 * 2) / 2}px`,
}))
</script>

<template>
  <span
    v-if="user"
    v-tooltip="decorative ? null : user.displayName"
    class="ui-avatar"
    :class="{ 'has-ring': ring, 'is-pending': pending }"
    :style="{ ...box, '--avatar-bg': avatarColor(user.id) }"
    :role="decorative ? undefined : 'img'"
    :aria-label="decorative ? undefined : user.displayName"
    :aria-hidden="decorative ? 'true' : undefined"
  >
    <span class="initials" aria-hidden="true">{{ letters }}</span>
  </span>
  <span
    v-else
    v-tooltip="decorative ? null : 'Unassigned'"
    class="ui-avatar ui-avatar--empty"
    :class="{ 'has-ring': ring }"
    :style="box"
    :role="decorative ? undefined : 'img'"
    :aria-label="decorative ? undefined : 'Unassigned'"
    :aria-hidden="decorative ? 'true' : undefined"
  >
    <UiIcon :icon="UserRoundPlus" :size="iconSize" />
  </span>
</template>

<style scoped>
.ui-avatar {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  border-radius: var(--radius-full);
  background: var(--avatar-bg);
  /* White initials in both themes: --danger-fg is pure white in light and dark (7.2: at least 5.3:1 on every avatar colour). */
  color: var(--danger-fg);
  font-weight: var(--fw-semibold);
  line-height: 1;
  letter-spacing: -0.01em;
  user-select: none;
}

.initials {
  font-size: inherit;
}

.ui-avatar--empty {
  background: transparent;
  border: 1px dashed var(--border-control);
  color: var(--text-tertiary);
}

.has-ring {
  box-shadow: 0 0 0 2px var(--bg-surface);
}

.is-pending {
  outline: 1.5px dashed var(--border-control);
  outline-offset: 1px;
}

.has-ring.is-pending {
  outline-offset: 2px;
}
</style>

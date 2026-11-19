<script setup lang="ts">
// Avatar (spec 7.10): the person's photo (Look 2), else initials on a colour derived from the user id
// (lib/colors.ts avatarColor), so renaming never recolours anyone. A null user is a dashed circle
// labelled "Unassigned". Most callers pass only an id and a name: the photo then comes from the
// signed-in user or the workspace's members. A photo that fails to load falls back to the initials.
import { computed, ref, watch } from 'vue'
import { UserRoundPlus } from '@lucide/vue'
import { avatarColor } from '../../lib/colors'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import UiIcon from './UiIcon.vue'

const props = withDefaults(
  defineProps<{
    /** `photoUrl` wins when given (null: initials); without it the photo is looked up by id. */
    user: { id: string; displayName: string; photoUrl?: string | null } | null
    size?: 16 | 20 | 24 | 28 | 32 | 40 | 64
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

const session = useSession()
const members = useMembers()

const photo = computed<string | null>(() => {
  const u = props.user
  if (!u) return null
  if (u.photoUrl !== undefined) return u.photoUrl
  if (session.user?.id === u.id) return session.user.photoUrl
  return members.get(u.id)?.photoUrl ?? null
})

/** The photo that failed to load, so the initials show instead (until the photo changes). */
const failed = ref<string | null>(null)
watch(photo, () => (failed.value = null))

const iconSize = computed(() => {
  if (props.size <= 20) return 12
  if (props.size <= 24) return 14
  if (props.size <= 32) return 16
  if (props.size <= 40) return 20
  return 24
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
    <img
      v-if="photo && failed !== photo"
      class="photo"
      :src="photo"
      alt=""
      loading="lazy"
      decoding="async"
      draggable="false"
      referrerpolicy="no-referrer"
      @error="failed = photo"
    />
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

/* Over the initials, which show through only while it loads (or if it never does). */
.photo {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  border-radius: inherit;
  object-fit: cover;
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

<script setup lang="ts">
// The account menu at the bottom of the sidebar (3.4): a 44px trigger named "Account: <name>",
// and a UiMenu with a header (name and email), Profile, the Theme group (System, Light, Dark),
// Keyboard shortcuts, Settings and Sign out.
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ChevronDown, CircleUser, Keyboard, LogOut, Monitor, Moon, Settings, Sun } from '@lucide/vue'
import { useTheme } from '../../composables/useTheme'
import { useSession } from '../../stores/session'
import { useUi } from '../../stores/ui'
import type { ThemePreference } from '../../types'
import UiAvatar from '../ui/UiAvatar.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { MenuItem } from '../ui/menu'

const session = useSession()
const ui = useUi()
const router = useRouter()
const theme = useTheme()

const user = computed(() => session.user)
const name = computed(() => user.value?.displayName ?? '')

const THEMES: { value: ThemePreference; label: string; icon: typeof Sun }[] = [
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
]

function items(): MenuItem[] {
  return [
    { id: 'profile', label: 'Profile', icon: CircleUser, onSelect: () => void router.push({ name: 'settings-profile' }) },
    { kind: 'label', id: 'theme', label: 'Theme' },
    ...THEMES.map(
      (t): MenuItem => ({
        kind: 'radio',
        id: `theme-${t.value}`,
        label: t.label,
        icon: t.icon,
        checked: theme.preference.value === t.value,
        onSelect: () => theme.setPreference(t.value),
      }),
    ),
    { kind: 'separator', id: 'sep-theme' },
    { id: 'shortcuts', label: 'Keyboard shortcuts', icon: Keyboard, kbd: '?', onSelect: () => ui.openShortcuts() },
    { id: 'settings', label: 'Settings', icon: Settings, onSelect: () => void router.push({ name: 'settings-appearance' }) },
    { kind: 'separator', id: 'sep-out' },
    { id: 'sign-out', label: 'Sign out', icon: LogOut, onSelect: () => void session.logout() },
  ]
}
</script>

<template>
  <UiMenu v-if="user" :items="items" aria-label="Account" placement="top-start" :min-width="232">
    <template #trigger="{ props }">
      <button
        v-bind="props"
        v-tooltip="{ text: name, overflowOnly: true, placement: 'top' }"
        type="button"
        class="user-trigger focus-inset"
        :aria-label="`Account: ${name}`"
      >
        <UiAvatar :user="user" :size="24" decorative />
        <span class="user-name truncate">{{ name }}</span>
        <UiIcon :icon="ChevronDown" :size="14" class="chevron" />
      </button>
    </template>
    <template #header>
      <div class="who">
        <span class="who-name truncate">{{ name }}</span>
        <span class="who-email truncate">{{ user.email }}</span>
      </div>
    </template>
  </UiMenu>
</template>

<style scoped>
.user-trigger {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-width: 0;
  height: 44px;
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  text-align: left;
  transition: background-color var(--dur-1) var(--ease-out);
}

.user-trigger:hover,
.user-trigger[aria-expanded='true'] {
  background: var(--bg-hover);
}

.user-name {
  flex: 1;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
}

.chevron {
  color: var(--text-tertiary);
}

.who {
  display: flex;
  flex-direction: column;
  min-width: 0;
  max-width: 260px;
}

.who-name {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.who-email {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}
</style>

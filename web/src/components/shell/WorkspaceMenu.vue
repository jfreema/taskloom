<script setup lang="ts">
// The workspace switcher at the top of the sidebar (3.4): a 40px trigger named "Workspace: <name>"
// and a UiMenu with one radio item per workspace, then Workspace settings, Members, Invite people
// (Admin+) and Create workspace. Switching always goes through useWorkspaceSwitch, which commits
// pending deletes, clears the selection and the task store, and goes Home (8.2).
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ChevronDown, Plus, Settings, UserPlus, Users } from '@lucide/vue'
import { useWorkspaceSwitch } from '../../composables/useWorkspaceSwitch'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import UiIcon from '../ui/UiIcon.vue'
import UiMenu from '../ui/UiMenu.vue'
import type { MenuItem } from '../ui/menu'

const workspaces = useWorkspaces()
const ui = useUi()
const router = useRouter()
const switcher = useWorkspaceSwitch()

const current = computed(() => workspaces.current)
const name = computed(() => current.value?.name ?? 'No workspace')
const letter = computed(() => (Array.from(current.value?.name.trim() ?? '')[0] ?? '').toLocaleUpperCase())

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true })

function items(): MenuItem[] {
  const out: MenuItem[] = []
  const all = [...workspaces.list].sort((a, b) => collator.compare(a.name, b.name))
  if (all.length) {
    out.push({ kind: 'label', id: 'workspaces', label: 'Workspaces' })
    for (const w of all) {
      out.push({
        kind: 'radio',
        id: `ws-${w.id}`,
        label: w.name,
        checked: w.id === workspaces.currentId,
        onSelect: () => {
          if (w.id !== workspaces.currentId) void switcher.switchTo(w.id, { navigate: true })
        },
      })
    }
    out.push({ kind: 'separator', id: 'sep-switch' })
  }
  if (current.value) {
    out.push(
      { id: 'settings', label: 'Workspace settings', icon: Settings, onSelect: () => void router.push({ name: 'settings-workspace' }) },
      { id: 'members', label: 'Members', icon: Users, onSelect: () => void router.push({ name: 'settings-members' }) },
    )
    if (workspaces.isAdmin()) out.push({ id: 'invite', label: 'Invite people', icon: UserPlus, onSelect: () => ui.openInvite() })
    out.push({ kind: 'separator', id: 'sep-create' })
  }
  out.push({ id: 'create', label: 'Create workspace', icon: Plus, onSelect: () => ui.openNewWorkspace() })
  return out
}
</script>

<template>
  <UiMenu :items="items" aria-label="Workspaces" :min-width="240">
    <template #trigger="{ props }">
      <button
        v-bind="props"
        v-tooltip="{ text: name, overflowOnly: true, placement: 'bottom' }"
        type="button"
        class="ws-trigger focus-inset"
        :aria-label="`Workspace: ${name}`"
      >
        <span v-if="current" class="ws-tile" aria-hidden="true">{{ letter }}</span>
        <span class="ws-name truncate">{{ name }}</span>
        <UiIcon :icon="ChevronDown" :size="14" class="chevron" />
      </button>
    </template>
  </UiMenu>
</template>

<style scoped>
.ws-trigger {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-width: 0;
  height: var(--control-lg);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  text-align: left;
  transition: background-color var(--dur-1) var(--ease-out);
}

.ws-trigger:hover,
.ws-trigger[aria-expanded='true'] {
  background: var(--bg-hover);
}

.ws-tile {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: var(--radius-sm);
  background: var(--accent);
  color: var(--accent-fg);
  font-size: var(--fs-sm);
  line-height: 1;
  font-weight: var(--fw-semibold);
}

.ws-name {
  flex: 1;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
}

.chevron {
  color: var(--text-tertiary);
}
</style>

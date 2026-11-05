<script setup lang="ts">
// Settings › Members (8.13) for the current workspace.
// - Header: "Copy sign-in link" (everyone but guests; copies the app URL, since Taskloom sends no
//   email) and "Invite people" (Admin+, opens the invite dialog).
// - The table "Members": you first, then by name (MemberRow). Role changes are optimistic (the
//   store toasts and rolls back on failure); Remove asks a confirm first. Guests see no controls.
// - States: skeleton rows while loading (after 200ms), an error with "Try again", and "It's just
//   you" when nobody else is in the workspace.
import { computed, nextTick, ref, watch } from 'vue'
import { Link, UserPlus, Users } from '@lucide/vue'
import MemberRow from '../../components/settings/MemberRow.vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiErrorState from '../../components/ui/UiErrorState.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import { skeletonDelay } from '../../components/shell/skeletonHandover'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { useClipboard } from '../../composables/useClipboard'
import { useConfirm } from '../../composables/useConfirm'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useToast } from '../../composables/useToast'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import { ROLE_LABEL, type AssignableRole, type Member } from '../../types'

const members = useMembers()
const session = useSession()
const ui = useUi()
const workspaces = useWorkspaces()
const toast = useToast()
const confirm = useConfirm()
const clipboard = useClipboard()
const announce = useAnnouncer()

const table = ref<HTMLElement | null>(null)

const canManage = computed(() => workspaces.isAdmin())
const canCopy = computed(() => workspaces.canEdit())
const meId = computed(() => session.user?.id ?? null)

watch(
  () => workspaces.currentId,
  () => void members.ensure(),
  { immediate: true },
)

/** You first, then by name (the store's order). */
const rows = computed<Member[]>(() => {
  const all = members.current
  const me = all.find((m) => m.userId === meId.value)
  return me ? [me, ...all.filter((m) => m !== me)] : all
})

const state = computed<'loading' | 'error' | 'alone' | 'table'>(() => {
  if (!rows.value.length) return members.loadError && !members.loading ? 'error' : 'loading'
  return rows.value.length === 1 ? 'alone' : 'table'
})

const showSkeleton = useDelayedFlag(computed(() => state.value === 'loading'), skeletonDelay())

const description = computed(() => {
  const name = workspaces.current?.name ?? 'this workspace'
  return `Everyone in ${name}. Invited people get access the first time they sign in with that address.`
})

async function copyLink(): Promise<void> {
  const ok = await clipboard.copy(window.location.origin)
  if (ok) toast.success('Link copied')
  else toast.show({ message: "Couldn't copy the link.", tone: 'danger' })
}

function article(role: AssignableRole): string {
  return role === 'Admin' ? 'an' : 'a'
}

function changeRole(m: Member, role: AssignableRole): void {
  void members.updateRole(m.userId, role).catch(() => undefined)
  announce(`${m.displayName} is now ${article(role)} ${ROLE_LABEL[role]}`)
}

/** After a removal: the next row's Remove button, else the previous row's, else the table's heading button. */
function focusNear(index: number): void {
  void nextTick(() => {
    const buttons = Array.from(table.value?.querySelectorAll<HTMLElement>('tbody tr') ?? []).map((tr) =>
      tr.querySelector<HTMLElement>('button[aria-label^="Remove "], button[aria-haspopup="listbox"]'),
    )
    const target = buttons.slice(index).find(Boolean) ?? [...buttons.slice(0, index)].reverse().find(Boolean)
    target?.focus({ preventScroll: true })
  })
}

async function remove(m: Member): Promise<void> {
  const ok = await confirm({
    title: `Remove ${m.displayName}?`,
    message: 'They lose access to this workspace and are unassigned from its tasks.',
    confirmLabel: 'Remove',
    danger: true,
  })
  if (!ok) return
  const index = rows.value.findIndex((x) => x.userId === m.userId)
  void members.remove(m.userId).catch(() => undefined)
  announce(`${m.displayName} removed`)
  focusNear(index)
}
</script>

<template>
  <SettingsPage title="Members" :description="description" :busy="state === 'loading'">
    <template v-if="canCopy || canManage" #actions>
      <UiButton v-if="canCopy" :icon="Link" @click="copyLink">Copy sign-in link</UiButton>
      <UiButton v-if="canManage" variant="primary" :icon="UserPlus" @click="ui.openInvite()">Invite people</UiButton>
    </template>

    <div v-if="state === 'loading'" aria-hidden="true">
      <div v-if="showSkeleton" class="frame">
        <div v-for="i in 3" :key="i" class="skeleton-row">
          <UiSkeleton variant="circle" width="28px" />
          <UiSkeleton width="140px" />
          <UiSkeleton width="180px" />
        </div>
      </div>
    </div>

    <UiErrorState
      v-else-if="state === 'error'"
      title="Couldn't load the members"
      message="Check your connection and try again."
      :retry="() => void members.reload()"
    />

    <div v-else-if="state === 'alone'" class="frame">
      <UiEmptyState :icon="Users" title="It's just you" description="Invite people to work together.">
        <template v-if="canManage" #actions>
          <UiButton variant="primary" :icon="UserPlus" @click="ui.openInvite()">Invite people</UiButton>
        </template>
      </UiEmptyState>
    </div>

    <div v-else ref="table" class="frame">
      <table class="members" role="table" aria-label="Members">
        <thead class="sr-only" role="rowgroup">
          <tr role="row">
            <th role="columnheader" scope="col">Name</th>
            <th role="columnheader" scope="col">Email</th>
            <th role="columnheader" scope="col">Invitation</th>
            <th role="columnheader" scope="col">Role</th>
            <th role="columnheader" scope="col">Actions</th>
          </tr>
        </thead>
        <tbody role="rowgroup">
          <MemberRow
            v-for="m in rows"
            :key="m.userId"
            :member="m"
            :is-me="m.userId === meId"
            :can-manage="canManage"
            :can-copy="canCopy"
            @role="changeRole(m, $event)"
            @remove="remove(m)"
            @copy-link="copyLink"
          />
        </tbody>
      </table>
    </div>
  </SettingsPage>
</template>

<style scoped>
/* MemberRow lays out against this width (two lines per person below 680px). */
.frame {
  container: members / inline-size;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  overflow: hidden;
}

.members {
  width: 100%;
  border-collapse: collapse;
  table-layout: auto;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: 52px;
  padding: 0 var(--space-4);
}

.skeleton-row + .skeleton-row {
  border-top: 1px solid var(--border-subtle);
}

@media (max-width: 639px) {
  .members,
  .members tbody {
    display: block;
  }
}
</style>

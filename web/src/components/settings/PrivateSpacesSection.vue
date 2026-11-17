<script setup lang="ts">
// Settings › General, "Private spaces" (Security 3), for Owner and Admins: every private space of the
// workspace with how many people are in it, including the ones you aren't in, so a space whose
// people have all left can still be reached. "Join" adds you to it; its people then see you listed.
import { ref, watch } from 'vue'
import { Lock } from '@lucide/vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { errorMessage, useToast } from '../../composables/useToast'
import { pluralize } from '../../lib/format'
import { useWorkspaces } from '../../stores/workspaces'
import type { PrivateSpaceSummary, Uuid } from '../../types'
import SpaceTile from '../fields/SpaceTile.vue'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'

const props = defineProps<{ workspaceId: Uuid }>()

const workspaces = useWorkspaces()
const toast = useToast()
const announce = useAnnouncer()

const rows = ref<PrivateSpaceSummary[] | null>(null)
const loadError = ref('')
const joining = ref<Uuid | null>(null)

async function load(): Promise<void> {
  const wsId = props.workspaceId
  loadError.value = ''
  try {
    const result = await workspaces.loadPrivateSpaces(wsId)
    if (wsId === props.workspaceId) rows.value = result
  } catch (err) {
    if (wsId === props.workspaceId) loadError.value = errorMessage(err).message || "Couldn't load the private spaces."
  }
}

watch(
  () => props.workspaceId,
  () => {
    rows.value = null
    void load()
  },
  { immediate: true },
)

async function join(row: PrivateSpaceSummary): Promise<void> {
  if (joining.value) return
  joining.value = row.id
  try {
    await workspaces.joinSpace(row.id)
    row.isMember = true
    row.memberCount++
    announce(`You joined ${row.name}`)
  } catch (err) {
    toast.error(err)
  } finally {
    joining.value = null
  }
}
</script>

<template>
  <section class="block" aria-labelledby="general-private">
    <div class="head">
      <h3 id="general-private" class="block-title">Private spaces</h3>
      <p class="intro">
        Only a private space's people can see it. As an admin you can see every private space's name here and join one, for
        example when everyone in it has left. Its people will see you listed.
      </p>
    </div>
    <p v-if="loadError" class="muted">
      {{ loadError }}
      <UiButton variant="link" size="sm" @click="load">Try again</UiButton>
    </p>
    <p v-else-if="rows === null" class="muted">Loading…</p>
    <p v-else-if="rows.length === 0" class="muted">There are no private spaces in this workspace.</p>
    <ul v-else class="rows">
      <li v-for="row in rows" :key="row.id" class="row">
        <SpaceTile :space="row" :size="20" />
        <span class="name truncate">{{ row.name }}</span>
        <UiIcon :icon="Lock" :size="12" class="lock" />
        <span class="count">{{ pluralize(row.memberCount, 'person', 'people') }}</span>
        <span v-if="row.isMember" class="in">You're in it</span>
        <UiButton
          v-else
          size="sm"
          variant="secondary"
          :loading="joining === row.id"
          :aria-label="`Join ${row.name}`"
          @click="join(row)"
        >
          Join
        </UiButton>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.head {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.block-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.intro,
.muted {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.rows {
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: 48px;
  padding: var(--space-2) var(--space-3);
}

.row + .row {
  border-top: 1px solid var(--border-subtle);
}

.name {
  min-width: 0;
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.lock {
  flex: none;
  color: var(--text-tertiary);
}

.count {
  margin-left: auto;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
  white-space: nowrap;
}

.in {
  min-width: 64px;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
  text-align: right;
  white-space: nowrap;
}
</style>

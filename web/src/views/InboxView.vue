<script setup lang="ts">
// The Inbox (Features 2): what happened to my tasks, mentions of me and my due-date reminders,
// newest first under Today, Yesterday and Earlier. "Unread" (the default) or "All"; "Mark all as
// read"; a row opens its task in the panel, which reads everything about that task. Rows read
// during the visit stay on screen in the Unread view until the page is left or reloaded.
// States: skeleton, error with retry, "You're all caught up" and the first-run explanation.
import { computed, onMounted, watch } from 'vue'
import { CheckCheck, Inbox as InboxIcon, PartyPopper } from '@lucide/vue'
import InboxRow from '../components/inbox/InboxRow.vue'
import { INBOX_GROUP_LABEL, inboxGroup, type InboxGroupKey } from '../components/inbox/inboxText'
import PageHeader from '../components/shell/PageHeader.vue'
import { skeletonDelay } from '../components/shell/skeletonHandover'
import UiButton from '../components/ui/UiButton.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import UiSegmented, { type SegmentedOption } from '../components/ui/UiSegmented.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'
import { useAnnouncer } from '../composables/useAnnouncer'
import { useBreakpoint } from '../composables/useBreakpoint'
import { useDelayedFlag } from '../composables/useDelayedFlag'
import { useTaskPanel } from '../composables/useTaskPanel'
import { useToday } from '../composables/useToday'
import { addDaysIso } from '../lib/dates'
import { pluralize } from '../lib/format'
import { useInbox, type InboxFilter } from '../stores/inbox'
import { useWorkspaces } from '../stores/workspaces'
import type { InboxItem, Uuid } from '../types'

const inbox = useInbox()
const workspaces = useWorkspaces()
const panel = useTaskPanel()
const today = useToday()
const announce = useAnnouncer()
const { isPhone } = useBreakpoint()

const filter = computed<string>({
  get: () => inbox.filter,
  set: (v) => inbox.setFilter(v === 'all' ? 'all' : ('unread' satisfies InboxFilter)),
})

const options = computed<SegmentedOption[]>(() => [
  { value: 'unread', label: 'Unread', count: inbox.unread || undefined },
  { value: 'all', label: 'All' },
])

const pending = computed(() => !inbox.loaded && !inbox.error)
const skeleton = useDelayedFlag(pending, skeletonDelay())

const groups = computed<{ key: InboxGroupKey; items: InboxItem[] }[]>(() => {
  const yesterday = addDaysIso(today.value, -1)
  const out: { key: InboxGroupKey; items: InboxItem[] }[] = []
  for (const n of inbox.items) {
    const key = inboxGroup(n.createdAt, today.value, yesterday)
    const last = out[out.length - 1]
    if (last && last.key === key) last.items.push(n)
    else out.push({ key, items: [n] })
  }
  return out
})

// J and K in the panel step through the tasks in the order shown, each once.
watch(
  () => inbox.items,
  (items) => {
    const seen = new Set<Uuid>()
    const order: Uuid[] = []
    for (const n of items) {
      if (seen.has(n.taskId)) continue
      seen.add(n.taskId)
      order.push(n.taskId)
    }
    panel.setOrder(order)
  },
  { immediate: true },
)

function load(): void {
  void inbox.load()
}

onMounted(load)
// Another workspace (the store was reset by the shell): its own Inbox.
watch(() => workspaces.currentId, (id, before) => {
  if (id && before && id !== before) load()
})

function open(n: InboxItem): void {
  inbox.markTaskRead(n.taskId)
  panel.open(n.taskId, { focus: !(panel.mode.value === 'docked' && panel.isOpen.value) })
}

function toggleRead(n: InboxItem): void {
  void inbox.mark([n.id], !n.readAt)
}

function markAll(): void {
  const count = inbox.unread
  void inbox.markAllRead().then(
    () => announce(`Marked ${pluralize(count, 'notification')} as read`),
    () => undefined,
  )
}
</script>

<template>
  <PageHeader title="Inbox">
    <h1 class="heading">Inbox</h1>
    <template #actions>
      <UiButton variant="ghost" size="sm" :icon="CheckCheck" :disabled="inbox.unread === 0" @click="markAll">Mark all as read</UiButton>
    </template>
  </PageHeader>
  <div class="inbox" :aria-busy="pending || inbox.loading ? 'true' : undefined">
    <div class="bar">
      <UiSegmented v-model="filter" :options="options" aria-label="Show" size="sm" />
      <UiButton v-if="isPhone" variant="ghost" size="sm" :icon="CheckCheck" :disabled="inbox.unread === 0" @click="markAll">Mark all as read</UiButton>
    </div>

    <UiErrorState v-if="inbox.error && !inbox.loaded" class="state" title="Couldn't load your Inbox" :retry="load" />

    <ul v-else-if="!inbox.loaded" class="skeleton" role="list" aria-hidden="true">
      <template v-if="skeleton">
        <li v-for="i in 6" :key="i" class="skeleton-row">
          <UiSkeleton variant="circle" width="28px" height="28px" />
          <span class="skeleton-text">
            <UiSkeleton :width="`${30 + ((i * 17) % 35)}%`" />
            <UiSkeleton :width="`${45 + ((i * 11) % 30)}%`" />
          </span>
        </li>
      </template>
    </ul>

    <template v-else-if="inbox.items.length === 0">
      <UiEmptyState
        v-if="inbox.filter === 'unread'"
        class="state"
        :icon="PartyPopper"
        title="You're all caught up."
        description="Nothing unread. Assignments, mentions, changes to your tasks and due-date reminders show up here."
      >
        <template #actions>
          <UiButton @click="filter = 'all'">Show everything</UiButton>
        </template>
      </UiEmptyState>
      <UiEmptyState
        v-else
        class="state"
        :icon="InboxIcon"
        title="Nothing here yet"
        description="When someone assigns you a task, mentions you with @ or changes a task of yours, it shows up here. So do reminders for tasks due today."
      />
    </template>

    <template v-else>
      <section v-for="g in groups" :key="g.key" class="group" :aria-labelledby="`inbox-${g.key}`">
        <h2 :id="`inbox-${g.key}`" class="group-title">{{ INBOX_GROUP_LABEL[g.key] }}</h2>
        <ul class="rows" role="list">
          <InboxRow
            v-for="n in g.items"
            :key="n.id"
            :item="n"
            :current="n.taskId === panel.openId.value"
            @open="open(n)"
            @toggle-read="toggleRead(n)"
          />
        </ul>
      </section>
      <div v-if="inbox.hasMore" class="more">
        <UiButton variant="ghost" size="sm" :loading="inbox.loadingMore" @click="inbox.loadMore()">Show older</UiButton>
      </div>
    </template>
  </div>
</template>

<style scoped>
.heading {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.inbox {
  width: 100%;
  max-width: calc(880px + 2 * var(--space-6));
  margin: 0 auto;
  padding: var(--space-5) var(--space-6) var(--space-12);
}

.bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
}

.state {
  margin: var(--space-12) auto 0;
}

.group {
  margin-top: var(--space-5);
}

.group-title {
  margin: 0 0 var(--space-1);
  padding: 0 var(--space-2);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--text-tertiary);
}

.rows,
.skeleton {
  margin: 0;
  padding: 0;
  list-style: none;
}

.skeleton {
  margin-top: var(--space-6);
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-2) var(--space-2) calc(var(--space-2) + 8px + var(--space-3));
}

.skeleton-text {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  flex: 1;
  min-width: 0;
}

.more {
  display: flex;
  justify-content: center;
  margin-top: var(--space-4);
}

@container page (max-width: 639px) {
  .inbox {
    padding: var(--space-4) var(--space-3) var(--space-8);
  }
}
</style>

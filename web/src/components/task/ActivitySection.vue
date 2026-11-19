<script setup lang="ts">
// Activity (8.10): "Activity" with the All | Comments filter (kept in taskloom.activityFilter),
// then the entries oldest first, the newest next to the composer. The first entry is synthesized
// ("<creator> created this task"); system entries are 12/16 with the actor's name in 500; comments
// are CommentItems. Runs of small changes by one person fold into one line that opens to the single
// changes (Look 2, activityRuns.ts).
import { computed, ref, useId, watch } from 'vue'
import { ChevronRight } from '@lucide/vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useToday } from '../../composables/useToday'
import { readString, writeString } from '../../lib/storage'
import { useSession } from '../../stores/session'
import type { Activity, TaskDetail, Uuid } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import UiSegmented, { type SegmentedOption } from '../ui/UiSegmented.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import { foldRuns, runSummary, type FeedEntry } from './activityRuns'
import { activitySegments } from './activityText'
import CommentItem from './CommentItem.vue'
import RelativeTime from './RelativeTime.vue'
import { useTaskDetailContext } from './taskDetailContext'
import { mergeComments } from './taskDetailState'
import { useComments } from './useComments'

const FILTER_KEY = 'taskloom.activityFilter'
type Filter = 'all' | 'comments'

const props = defineProps<{ taskId: Uuid; detail: TaskDetail | null }>()

const ctx = useTaskDetailContext()
const session = useSession()
const comments = useComments()
const today = useToday()
const headingId = `${useId()}-activity`

const filter = ref<Filter>(readString(FILTER_KEY) === 'comments' ? 'comments' : 'all')
watch(filter, (f) => writeString(FILTER_KEY, f))
const filterModel = computed<string>({
  get: () => filter.value,
  set: (v) => (filter.value = v === 'comments' ? 'comments' : 'all'),
})
const options: SegmentedOption[] = [
  { value: 'all', label: 'All' },
  { value: 'comments', label: 'Comments' },
]

const merged = computed(() => (props.detail ? mergeComments(props.taskId, props.detail.comments) : []))

const entries = computed<FeedEntry[]>(() => {
  const d = props.detail
  if (!d) return []
  const out: FeedEntry[] = []
  if (filter.value === 'all') {
    for (const a of d.activity) out.push({ kind: 'system', key: a.id, at: a.createdAt, activity: a })
  }
  for (const c of merged.value) out.push({ kind: 'comment', key: c.id, at: c.createdAt, comment: c })
  out.sort((a, b) => Date.parse(a.at) - Date.parse(b.at))
  if (filter.value === 'all') out.unshift({ kind: 'created', key: 'created', at: d.task.createdAt, actor: d.createdBy })
  return filter.value === 'all' ? foldRuns(out) : out
})

/** The runs shown open, by key. A task opened again starts with them folded. */
const openRuns = ref(new Set<string>())
watch(
  () => props.taskId,
  () => (openRuns.value = new Set()),
)

function toggleRun(key: string): void {
  const next = new Set(openRuns.value)
  if (next.has(key)) next.delete(key)
  else next.add(key)
  openRuns.value = next
}

const loading = computed(() => props.detail === null)
const showSkeleton = useDelayedFlag(loading, 200)

function segmentsOf(a: Activity) {
  return activitySegments(a, today.value)
}
</script>

<template>
  <section class="activity" :aria-labelledby="headingId">
    <div class="header">
      <component :is="ctx.headingTag.value" :id="headingId" class="section-title">Activity</component>
      <UiSegmented v-model="filterModel" class="filter" :options="options" aria-label="Activity filter" size="sm" />
    </div>
    <div v-if="loading && showSkeleton" class="skeleton" aria-hidden="true">
      <UiSkeleton width="70%" />
      <UiSkeleton width="55%" />
      <UiSkeleton width="62%" />
    </div>
    <template v-else-if="!loading">
      <p v-if="filter === 'comments' && !entries.length" class="empty">No comments yet. Ask a question or post an update.</p>
      <ol v-else class="entries">
        <template v-for="e in entries" :key="e.key">
          <li v-if="e.kind === 'created'" class="system">
            <span class="actor">{{ e.actor.displayName }}</span>{{ ' created this task ' }}<span class="dot" aria-hidden="true">·</span>{{ ' ' }}<RelativeTime :at="e.at" />
          </li>
          <li v-else-if="e.kind === 'system'" class="system">
            <span class="actor">{{ e.activity.actor.displayName }}</span>
            {{ ' ' }}
            <template v-for="(s, i) in segmentsOf(e.activity)" :key="i">
              <strong v-if="s.strong" class="value">{{ s.text }}</strong>
              <template v-else>{{ s.text }}</template>
            </template>
            {{ ' ' }}<span class="dot" aria-hidden="true">·</span>{{ ' ' }}<RelativeTime :at="e.at" />
          </li>
          <li v-else-if="e.kind === 'run'" class="system run" data-activity-run>
            <span class="actor">{{ e.actor.displayName }}</span>{{ ` ${runSummary(e.items)} ` }}<span class="dot" aria-hidden="true">·</span>{{ ' ' }}<RelativeTime :at="e.at" />
            {{ ' ' }}
            <button
              type="button"
              class="run-toggle"
              :aria-expanded="openRuns.has(e.key) ? 'true' : 'false'"
              :aria-controls="`${headingId}-${e.key}`"
              @click="toggleRun(e.key)"
            >
              <UiIcon class="run-chevron" :icon="ChevronRight" :size="12" />
              {{ openRuns.has(e.key) ? 'Hide' : 'Show' }} {{ e.items.length }} changes
            </button>
            <ol v-if="openRuns.has(e.key)" :id="`${headingId}-${e.key}`" class="run-items">
              <li v-for="a in e.items" :key="a.id">
                <template v-for="(s, i) in segmentsOf(a)" :key="i">
                  <strong v-if="s.strong" class="value">{{ s.text }}</strong>
                  <template v-else>{{ s.text }}</template>
                </template>
                {{ ' ' }}<span class="dot" aria-hidden="true">·</span>{{ ' ' }}<RelativeTime :at="a.createdAt" />
              </li>
            </ol>
          </li>
          <CommentItem
            v-else
            :comment="e.comment"
            :mine="e.comment.author.id === session.user?.id"
            :sending="comments.isSending(e.comment.id)"
          />
        </template>
      </ol>
    </template>
  </section>
</template>

<style scoped>
.activity {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--control-sm);
}

.section-title {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.filter {
  margin-left: auto;
}

.entries {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: 0;
  padding: 0;
  list-style: none;
}

.system {
  padding: var(--space-1) 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
  overflow-wrap: anywhere;
}

.actor {
  font-weight: var(--fw-medium);
  color: var(--text-secondary);
}

.value {
  font-weight: var(--fw-medium);
  color: var(--text-secondary);
}

.dot {
  color: var(--text-tertiary);
}

.run-toggle {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  margin: -2px 0;
  padding: 1px var(--space-1) 1px 2px;
  border: 0;
  border-radius: var(--radius-xs);
  background: transparent;
  color: var(--text-secondary);
  font: inherit;
  font-weight: var(--fw-medium);
  vertical-align: baseline;
  cursor: pointer;
}

.run-toggle:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.run-chevron {
  transition: transform var(--dur-2) var(--ease-out);
}

.run-toggle[aria-expanded="true"] .run-chevron {
  transform: rotate(90deg);
}

.run-items {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin: var(--space-1-5) 0 0 var(--space-1);
  padding: 0 0 0 var(--space-3);
  border-left: 2px solid var(--border-subtle);
  list-style: none;
}

.empty {
  margin: 0;
  padding: var(--space-2) 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-tertiary);
}

.skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-2) 0;
}
</style>

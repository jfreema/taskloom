<script setup lang="ts">
// The Dashboard: a breakdown of the workspace's tasks from GET /api/workspaces/{id}/dashboard. The
// server counts (only tasks the caller can see); this page names the ids from the workspace tree
// and the members, and lays the numbers out as stat tiles, bars and a 14-day trend. Filters in one
// row: Everyone or Mine, and the whole workspace, one space or one list (remembered per person).
// The overdue tasks open in the task panel; list names go to the list.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  CalendarClock,
  CircleAlert,
  CircleCheckBig,
  CircleDashed,
  LayoutDashboard,
  ListTodo,
  Lock,
  RefreshCw,
  UserRoundX,
} from '@lucide/vue'
import { api, query } from '../api'
import DashBars, { type DashBarRow } from '../components/dashboard/DashBars.vue'
import DashCard from '../components/dashboard/DashCard.vue'
import DashStat from '../components/dashboard/DashStat.vue'
import DashTrend from '../components/dashboard/DashTrend.vue'
import ListGlyph from '../components/fields/ListGlyph.vue'
import PriorityFlag from '../components/fields/PriorityFlag.vue'
import SpaceTile from '../components/fields/SpaceTile.vue'
import PageHeader from '../components/shell/PageHeader.vue'
import UiAvatar from '../components/ui/UiAvatar.vue'
import UiAvatarStack from '../components/ui/UiAvatarStack.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiEmptyState from '../components/ui/UiEmptyState.vue'
import UiErrorState from '../components/ui/UiErrorState.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import UiIconButton from '../components/ui/UiIconButton.vue'
import UiSegmented, { type SegmentedOption } from '../components/ui/UiSegmented.vue'
import UiSelect from '../components/ui/UiSelect.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'
import type { ListboxOption } from '../components/ui/listbox'
import { useBreakpoint } from '../composables/useBreakpoint'
import { useTaskPanel } from '../composables/useTaskPanel'
import { useToday } from '../composables/useToday'
import { diffDays, formatDateShort } from '../lib/dates'
import { pluralize } from '../lib/format'
import { readJson, userKey, writeJson } from '../lib/storage'
import { useMembers } from '../stores/members'
import { useSession } from '../stores/session'
import { useWorkspaces } from '../stores/workspaces'
import {
  PRIORITIES,
  PRIORITY_LABEL,
  STATUS_CATEGORY_LABEL,
  STATUS_CATEGORY_ORDER,
  type Dashboard,
  type DashboardScope,
  type ListSummary,
  type Space,
  type Status,
  type StatusCategory,
  type Uuid,
} from '../types'

const workspaces = useWorkspaces()
const members = useMembers()
const session = useSession()
const panel = useTaskPanel()
const today = useToday()
const { isPhone } = useBreakpoint()

// ---- Filters, remembered per person ----

interface Saved {
  scope?: DashboardScope
  where?: string
}

const storageKey = computed(() => (session.user ? userKey('taskloom.dashboard', session.user.id) : null))
const saved = storageKey.value ? readJson<Saved>(storageKey.value, {}) : {}
const scope = ref<DashboardScope>(saved.scope === 'mine' ? 'mine' : 'all')
/** '' (everything), 'space:<id>' or 'list:<id>'. */
const where = ref<string>(typeof saved.where === 'string' ? saved.where : '')

watch([scope, where], () => {
  if (storageKey.value) writeJson(storageKey.value, { scope: scope.value, where: where.value } satisfies Saved)
})

const scopeOptions: SegmentedOption[] = [
  { value: 'all', label: 'Everyone' },
  { value: 'mine', label: 'Mine' },
]
const scopeModel = computed<string>({
  get: () => scope.value,
  set: (v) => (scope.value = v === 'mine' ? 'mine' : 'all'),
})

const spaces = computed<Space[]>(() => workspaces.current?.spaces ?? [])

const whereOptions = computed<ListboxOption[]>(() => {
  const out: ListboxOption[] = [{ value: '', label: 'All spaces' }]
  for (const s of spaces.value) {
    out.push({ value: `space:${s.id}`, label: `All of ${s.name}`, group: s.name })
    for (const l of s.lists) out.push({ value: `list:${l.id}`, label: l.name, group: s.name, keywords: [s.name] })
  }
  return out
})

// A space or list that no longer exists (deleted, or another workspace) falls back to everything.
watch(
  whereOptions,
  (options) => {
    if (workspaces.loaded && !options.some((o) => o.value === where.value)) where.value = ''
  },
  { immediate: true },
)

const whereLabel = computed(() => whereOptions.value.find((o) => o.value === where.value)?.label ?? 'All spaces')

// ---- Loading ----

const data = ref<Dashboard | null>(null)
const error = ref<unknown>(null)
const loading = ref(false)
let request = 0

async function load(): Promise<void> {
  const ws = workspaces.currentId
  if (!ws) return
  const mine = ++request
  loading.value = true
  error.value = null
  const [kind, id] = where.value.split(':')
  try {
    const result = await api.get<Dashboard>(
      `/api/workspaces/${ws}/dashboard${query({
        scope: scope.value,
        spaceId: kind === 'space' ? id : null,
        listId: kind === 'list' ? id : null,
        offset: new Date().getTimezoneOffset(),
      })}`,
    )
    if (mine === request) data.value = result
  } catch (e) {
    if (mine === request) error.value = e
  } finally {
    if (mine === request) loading.value = false
  }
}

watch([() => workspaces.currentId, scope, where, today], () => void load())
onMounted(() => {
  void load()
  void members.ensure()
  window.addEventListener('focus', onFocus)
})
onBeforeUnmount(() => window.removeEventListener('focus', onFocus))

/** Coming back to the tab after a while brings fresh numbers. */
let lastLoad = Date.now()
watch(data, () => (lastLoad = Date.now()))
function onFocus(): void {
  if (Date.now() - lastLoad > 30_000) void load()
}

// ---- Lookups from the workspace tree ----

const listIndex = computed(() => {
  const m = new Map<Uuid, { list: ListSummary; space: Space }>()
  for (const space of spaces.value) for (const list of space.lists) m.set(list.id, { list, space })
  return m
})

const statusIndex = computed(() => {
  const m = new Map<Uuid, Status>()
  for (const space of spaces.value) for (const list of space.lists) for (const s of list.statuses) m.set(s.id, s)
  return m
})

const pctOf = (n: number, of: number) => (of > 0 ? Math.round((n / of) * 100) : 0)

// ---- Tiles ----

const totals = computed(() => data.value?.totals ?? null)
const completedSpark = computed(() => data.value?.trend.map((d) => d.completed) ?? [])
const completionRate = computed(() => (totals.value ? pctOf(totals.value.done, totals.value.total) : 0))

// ---- Status: categories (a tall segmented bar) and statuses grouped by name ----

const CATEGORY_COLOR: Record<StatusCategory, string> = { NotStarted: '#7A868C', Active: '#3D7DF5', Done: '#2E9F5E' }
const CATEGORIES: StatusCategory[] = ['NotStarted', 'Active', 'Done']

const statusGroups = computed(() => {
  const groups = new Map<string, { key: string; name: string; color: string; category: StatusCategory; count: number }>()
  for (const { id, count } of data.value?.byStatus ?? []) {
    const s = statusIndex.value.get(id)
    const name = s?.name ?? 'Other'
    const category = s?.category ?? 'NotStarted'
    const key = `${category}:${name.toLocaleLowerCase()}`
    const g = groups.get(key)
    if (g) g.count += count
    else groups.set(key, { key, name, color: s?.color ?? '#7A868C', category, count })
  }
  return [...groups.values()].sort(
    (a, b) => STATUS_CATEGORY_ORDER[a.category] - STATUS_CATEGORY_ORDER[b.category] || b.count - a.count,
  )
})

const categories = computed(() => {
  const total = statusGroups.value.reduce((n, g) => n + g.count, 0)
  return CATEGORIES.map((c) => {
    const count = statusGroups.value.filter((g) => g.category === c).reduce((n, g) => n + g.count, 0)
    return { category: c, label: STATUS_CATEGORY_LABEL[c], count, pct: pctOf(count, total), color: CATEGORY_COLOR[c] }
  })
})

const statusRows = computed<(DashBarRow & { color: string })[]>(() =>
  statusGroups.value.map((g) => ({
    key: g.key,
    label: g.name,
    color: g.color,
    segments: [{ value: g.count, color: g.color, label: pluralize(g.count, 'task') }],
  })),
)

// ---- Due dates (open tasks): columns ----

const dueColumns = computed(() => {
  const d = data.value?.due
  if (!d) return []
  const cols = [
    { key: 'overdue', label: 'Overdue', value: d.overdue, color: 'var(--danger)' },
    { key: 'today', label: 'Today', value: d.today, color: 'var(--priority-high)' },
    { key: 'tomorrow', label: 'Tomorrow', value: d.tomorrow, color: 'var(--accent)' },
    { key: 'week', label: 'Next 7 days', value: d.nextWeek, color: 'var(--accent)' },
    { key: 'later', label: 'Later', value: d.later, color: 'color-mix(in srgb, var(--accent) 55%, var(--bg-surface))' },
    { key: 'none', label: 'No date', value: d.none, color: 'var(--border-control)' },
  ]
  const max = Math.max(1, ...cols.map((c) => c.value))
  return cols.map((c) => ({ ...c, height: `${(c.value / max) * 100}%` }))
})

// ---- Priority ----

const PRIORITY_COLOR: Record<string, string> = {
  Urgent: 'var(--priority-urgent)',
  High: 'var(--priority-high)',
  Normal: 'var(--priority-normal)',
  Low: 'var(--priority-low)',
  None: 'var(--border-default)',
}

const priorityRows = computed(() =>
  PRIORITIES.map((p) => {
    const c = data.value?.byPriority.find((x) => x.priority === p) ?? { open: 0, done: 0 }
    return {
      key: p,
      label: PRIORITY_LABEL[p],
      priority: p,
      extra: `${c.done} done`,
      segments: [{ value: c.open, color: PRIORITY_COLOR[p], label: 'open' }],
    }
  }),
)

// ---- Workload ----

const WORKLOAD_SHOWN = 8
const showAllPeople = ref(false)

const peopleRows = computed(() =>
  (data.value?.byAssignee ?? []).map((p) => {
    const name = p.userId ? members.nameOf(p.userId) : 'Unassigned'
    return {
      key: p.userId ?? 'none',
      label: name,
      user: p.userId ? { id: p.userId, displayName: name } : null,
      extra: `${p.done} done`,
      segments: [
        { value: p.overdue, color: 'var(--danger)', label: 'overdue' },
        { value: p.dueSoon, color: 'var(--priority-high)', label: 'due this week' },
        { value: p.open - p.overdue - p.dueSoon, color: 'var(--accent)', label: 'later or undated' },
      ],
    }
  }),
)
const peopleShown = computed(() => (showAllPeople.value ? peopleRows.value : peopleRows.value.slice(0, WORKLOAD_SHOWN)))

// ---- Spaces and lists ----

const spaceRows = computed(() => {
  const byList = new Map((data.value?.byList ?? []).map((l) => [l.listId, l]))
  const out: {
    space: Space
    open: number
    done: number
    overdue: number
    lists: { list: ListSummary; open: number; done: number; overdue: number }[]
  }[] = []
  for (const space of spaces.value) {
    const lists = space.lists
      .map((list) => ({ list, ...(byList.get(list.id) ?? { open: 0, done: 0, overdue: 0 }) }))
      .filter((l) => l.open + l.done > 0)
    if (!lists.length) continue
    out.push({
      space,
      lists,
      open: lists.reduce((n, l) => n + l.open, 0),
      done: lists.reduce((n, l) => n + l.done, 0),
      overdue: lists.reduce((n, l) => n + l.overdue, 0),
    })
  }
  return out
})

// ---- Overdue tasks ----

const overdue = computed(() =>
  (data.value?.overdue ?? []).map((t) => {
    const where = listIndex.value.get(t.listId)
    const late = diffDays(today.value, t.dueDate)
    return {
      ...t,
      where: where ? `${where.space.name} › ${where.list.name}` : '',
      late: late === 1 ? '1 day late' : `${late} days late`,
      dueText: formatDateShort(t.dueDate, today.value),
      users: t.assigneeIds.map((id) => ({ id, displayName: members.nameOf(id) })),
    }
  }),
)

watch(overdue, (rows) => panel.setOrder(rows.map((r) => r.id)), { immediate: true })

function openTask(id: Uuid): void {
  panel.open(id, { focus: !(panel.mode.value === 'docked' && panel.isOpen.value) })
}

const empty = computed(() => data.value !== null && data.value.totals.total === 0)
const subtitle = computed(() => {
  const who = scope.value === 'mine' ? 'Your tasks' : 'Everyone’s tasks'
  return `${who} in ${where.value ? whereLabel.value.replace(/^All of /, '') : 'every space you can see'}`
})
</script>

<template>
  <PageHeader title="Dashboard" :loading="loading && !!data">
    <h1 class="heading">Dashboard</h1>
    <template #actions>
      <UiIconButton :icon="RefreshCw" label="Refresh" size="sm" :disabled="loading" @click="load" />
    </template>
  </PageHeader>

  <div class="dashboard" :aria-busy="loading ? 'true' : undefined">
    <div class="filters">
      <p class="subtitle">{{ subtitle }}</p>
      <div class="controls">
        <UiSegmented v-model="scopeModel" :options="scopeOptions" aria-label="Whose tasks" size="sm" />
        <UiSelect v-model="where" :options="whereOptions" aria-label="Where" size="sm" class="where" />
      </div>
    </div>

    <UiErrorState v-if="error && !data" class="state" title="Couldn't load the dashboard" :retry="load" />

    <div v-else-if="!data" class="tiles" aria-hidden="true">
      <div v-for="i in 5" :key="i" class="tile-skeleton"><UiSkeleton width="50%" /><UiSkeleton width="35%" height="28px" /></div>
    </div>

    <UiEmptyState
      v-else-if="empty"
      class="state"
      :icon="LayoutDashboard"
      title="Nothing to count yet"
      :description="
        scope === 'mine'
          ? 'No tasks are assigned to you here. Switch to Everyone, or pick another space.'
          : 'Add some tasks and this page fills in with their status, priority, due dates and who is on them.'
      "
    >
      <template v-if="scope === 'mine' || where" #actions>
        <UiButton @click="(scope = 'all'), (where = '')">Show everything</UiButton>
      </template>
    </UiEmptyState>

    <template v-else-if="totals">
      <div class="tiles">
        <DashStat
          hero
          label="Open tasks"
          :value="totals.open"
          :icon="ListTodo"
          tone="accent"
          :note="`${totals.total} in all · ${completionRate}% done`"
        />
        <DashStat
          label="Overdue"
          :value="totals.overdue"
          :icon="CircleAlert"
          :tone="totals.overdue ? 'danger' : 'neutral'"
          :note="totals.overdue ? `${pctOf(totals.overdue, totals.open)}% of open tasks` : 'Nothing late'"
        />
        <DashStat
          label="Due today"
          :value="totals.dueToday"
          :icon="CalendarClock"
          :note="`${data.due.tomorrow} tomorrow · ${data.due.nextWeek} later this week`"
        />
        <DashStat
          label="Completed, 7 days"
          :value="totals.completedLast7"
          :icon="CircleCheckBig"
          tone="success"
          :note="`${totals.createdLast7} created in the same days`"
          :spark="completedSpark"
        />
        <DashStat
          label="Unassigned"
          :value="totals.unassigned"
          :icon="UserRoundX"
          :note="`${totals.noDueDate} open without a due date`"
        />
      </div>

      <div class="grid">
        <DashCard title="Status" :caption="`${totals.total} tasks, ${totals.subtasks} of them subtasks`">
          <div class="segbar" role="img" :aria-label="categories.map((c) => `${c.label} ${c.count} (${c.pct}%)`).join(', ')">
            <span
              v-for="c in categories.filter((x) => x.count > 0)"
              :key="c.category"
              v-tooltip="`${c.label}: ${c.count} (${c.pct}%)`"
              class="seg"
              :style="{ flexGrow: c.count, background: c.color }"
            >
              <span v-if="c.pct >= 12" class="seg-label">{{ c.pct }}%</span>
            </span>
          </div>
          <ul class="seg-legend" role="list" aria-hidden="true">
            <li v-for="c in categories" :key="c.category">
              <span class="dot" :style="{ background: c.color }" />{{ c.label }}
              <strong class="tabular">{{ c.count }}</strong>
            </li>
          </ul>
          <DashBars :rows="statusRows" class="status-bars">
            <template #lead="{ row }">
              <span class="dot" :style="{ background: row.color }" />
              <span class="truncate">{{ row.label }}</span>
            </template>
          </DashBars>
        </DashCard>

        <DashCard title="Due dates" :caption="`${totals.open} open tasks by when they're due`">
          <div class="due" role="list">
            <div
              v-for="c in dueColumns"
              :key="c.key"
              v-tooltip="`${c.label}: ${pluralize(c.value, 'open task')}`"
              class="due-col"
              role="listitem"
              :aria-label="`${c.label}: ${pluralize(c.value, 'open task')}`"
            >
              <span class="due-num tabular" aria-hidden="true">{{ c.value }}</span>
              <span class="due-track" aria-hidden="true"><span class="due-bar" :style="{ height: c.height, background: c.color }" /></span>
              <span class="due-label" aria-hidden="true">
                <UiIcon v-if="c.key === 'overdue' && c.value > 0" :icon="CircleAlert" :size="14" class="due-alert" />{{ c.label }}
              </span>
            </div>
          </div>
        </DashCard>

        <DashCard title="Priority" caption="Open tasks by priority, with how many are done">
          <DashBars :rows="priorityRows">
            <template #lead="{ row }">
              <PriorityFlag :priority="row.priority" :show-label="row.priority !== 'None'" show-empty decorative :size="14" />
              <span v-if="row.priority === 'None'" class="muted">{{ row.label }}</span>
            </template>
          </DashBars>
        </DashCard>

        <DashCard title="Workload" caption="Open tasks per person. A task with two people counts for both">
          <div class="legend" aria-hidden="true">
            <span><span class="dot" style="background: var(--danger)" />Overdue</span>
            <span><span class="dot" style="background: var(--priority-high)" />Due this week</span>
            <span><span class="dot" style="background: var(--accent)" />Later or no date</span>
          </div>
          <DashBars :rows="peopleShown">
            <template #lead="{ row }">
              <UiAvatar :user="row.user" :size="20" decorative />
              <span class="truncate">{{ row.label }}</span>
            </template>
          </DashBars>
          <UiButton
            v-if="peopleRows.length > WORKLOAD_SHOWN"
            class="more"
            variant="ghost"
            size="sm"
            @click="showAllPeople = !showAllPeople"
          >
            {{ showAllPeople ? 'Show fewer' : `Show all ${peopleRows.length}` }}
          </UiButton>
        </DashCard>

        <DashCard title="Created and completed" caption="The last 14 days" :span="2">
          <DashTrend :trend="data.trend" :today="data.today" />
        </DashCard>

        <DashCard title="Spaces and lists" caption="Open, overdue and done per list">
          <div class="table-wrap">
            <table class="lists">
              <thead>
                <tr>
                  <th scope="col">List</th>
                  <th scope="col" class="n">Open</th>
                  <th scope="col" class="n">Overdue</th>
                  <th scope="col" class="n">Done</th>
                  <th scope="col" class="progress-col">Progress</th>
                </tr>
              </thead>
              <tbody v-for="s in spaceRows" :key="s.space.id">
                <tr class="space-row">
                  <th scope="rowgroup" class="space-name">
                    <SpaceTile :space="s.space" :size="18" />
                    <span class="truncate">{{ s.space.name }}</span>
                    <UiIcon v-if="s.space.isPrivate" :icon="Lock" :size="12" class="lock" aria-label="Private" />
                  </th>
                  <td class="n tabular">{{ s.open }}</td>
                  <td class="n tabular" :class="{ late: s.overdue > 0 }">{{ s.overdue }}</td>
                  <td class="n tabular">{{ s.done }}</td>
                  <td class="progress-col">
                    <span class="progress" :aria-label="`${pctOf(s.done, s.open + s.done)}% done`">
                      <span class="progress-fill" :style="{ width: `${pctOf(s.done, s.open + s.done)}%` }" />
                    </span>
                  </td>
                </tr>
                <tr v-for="l in s.lists" :key="l.list.id">
                  <th scope="row" class="list-name">
                    <RouterLink :to="`/l/${l.list.id}`" class="list-link focus-ring">
                      <ListGlyph :list="l.list" :size="14" />
                      <span class="truncate">{{ l.list.name }}</span>
                    </RouterLink>
                  </th>
                  <td class="n tabular">{{ l.open }}</td>
                  <td class="n tabular" :class="{ late: l.overdue > 0 }">{{ l.overdue }}</td>
                  <td class="n tabular">{{ l.done }}</td>
                  <td class="progress-col">
                    <span class="progress small" :aria-label="`${pctOf(l.done, l.open + l.done)}% done`">
                      <span class="progress-fill" :style="{ width: `${pctOf(l.done, l.open + l.done)}%` }" />
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </DashCard>

        <DashCard title="Most overdue" :caption="totals.overdue ? `${totals.overdue} open tasks are past their due date` : undefined">
          <ul v-if="overdue.length" class="overdue" role="list">
            <li v-for="t in overdue" :key="t.id">
              <button type="button" class="overdue-row focus-inset" :class="{ current: panel.openId.value === t.id }" @click="openTask(t.id)">
                <PriorityFlag :priority="t.priority" :size="14" />
                <span class="overdue-text">
                  <span class="overdue-title truncate">{{ t.title }}</span>
                  <span class="overdue-where truncate">{{ t.where }}</span>
                </span>
                <span class="overdue-late">
                  <span class="late-text">{{ t.late }}</span>
                  <span class="due-date">{{ t.dueText }}</span>
                </span>
                <UiAvatarStack v-if="!isPhone" :users="t.users" :size="20" :max="2" />
              </button>
            </li>
          </ul>
          <div v-else class="none-late">
            <UiIcon :icon="CircleDashed" :size="16" />
            Nothing is overdue. Nice.
          </div>
        </DashCard>
      </div>
    </template>
  </div>
</template>

<style scoped>
.heading {
  font-size: var(--fs-page-title);
  line-height: var(--lh-page-title);
  font-weight: var(--fw-heading);
  color: var(--heading-color);
}

.dashboard {
  width: 100%;
  max-width: calc(1240px + 2 * var(--space-6));
  margin: 0 auto;
  padding: var(--space-5) var(--space-6) var(--space-12);
}

.filters {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--space-3);
  margin-bottom: var(--space-4);
}

.subtitle {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.controls {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.where {
  min-width: 200px;
}

.state {
  margin: var(--space-12) auto 0;
}

.tiles {
  display: grid;
  grid-template-columns: 1.3fr repeat(4, 1fr);
  gap: var(--space-3);
  margin-bottom: var(--space-3);
}

.tile-skeleton {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  height: 112px;
  padding: var(--space-4);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-xl);
  background: var(--bg-surface);
}

.grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-3);
}

.muted {
  color: var(--text-secondary);
}

.dot {
  display: inline-block;
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: var(--radius-full);
}

/* Status */
.segbar {
  display: flex;
  gap: 2px;
  height: 28px;
}

.segbar .seg {
  display: flex;
  align-items: center;
  min-width: 4px;
  padding: 0 var(--space-2);
  border-radius: var(--radius-xs);
}

.seg-label {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  color: #FFFFFF;
}

.seg-legend,
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1) var(--space-4);
  margin: var(--space-2) 0 0;
  padding: 0;
  list-style: none;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-secondary);
}

.legend {
  margin: 0 0 var(--space-3);
}

.seg-legend li,
.legend span:has(> .dot) {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
}

.seg-legend strong {
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.status-bars {
  margin-top: var(--space-4);
  padding-top: var(--space-3);
  border-top: 1px solid var(--border-subtle);
}

/* Due dates */
.due {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: var(--space-2);
  height: 220px;
}

.due-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  padding: var(--space-1) 0;
  border-radius: var(--radius-md);
}

.due-col:hover {
  background: var(--bg-hover);
}

.due-num {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.due-track {
  flex: 1;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  width: 100%;
  border-bottom: 1px solid var(--border-default);
}

.due-bar {
  width: min(24px, 60%);
  min-height: 2px;
  border-radius: var(--radius-xs) var(--radius-xs) 0 0;
  transition: height var(--dur-4) var(--ease-out);
}

.due-label {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  max-width: 100%;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-secondary);
  text-align: center;
}

.due-alert {
  flex: none;
  color: var(--danger-text);
}

.more {
  margin-top: var(--space-2);
}

/* Spaces and lists */
.table-wrap {
  overflow-x: auto;
}

.lists {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.lists th,
.lists td {
  padding: var(--space-1-5) var(--space-2);
  text-align: left;
  font-weight: var(--fw-regular);
  color: var(--text-primary);
}

.lists thead th {
  padding-top: 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  color: var(--text-tertiary);
  border-bottom: 1px solid var(--border-subtle);
}

.lists .n {
  width: 1%;
  text-align: right;
  white-space: nowrap;
}

.lists td.late {
  color: var(--danger-text);
  font-weight: var(--fw-semibold);
}

.space-row th,
.space-row td {
  padding-top: var(--space-3);
  font-weight: var(--fw-semibold);
}

.space-name,
.list-link {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  max-width: 260px;
}

.lock {
  flex: none;
  color: var(--text-tertiary);
}

.list-name {
  padding-left: var(--space-6) !important;
}

.list-link {
  color: var(--text-primary);
  text-decoration: none;
  border-radius: var(--radius-xs);
}

.list-link:hover .truncate {
  text-decoration: underline;
  text-underline-offset: 2px;
}

.progress-col {
  width: 22%;
  min-width: 72px;
}

.progress {
  display: block;
  height: 8px;
  border-radius: var(--radius-full);
  background: color-mix(in srgb, var(--success-text) 16%, var(--bg-surface));
  overflow: hidden;
}

.progress.small {
  height: 6px;
}

.progress-fill {
  display: block;
  height: 100%;
  border-radius: var(--radius-full);
  background: var(--success-text);
}

/* Overdue */
.overdue {
  margin: 0 calc(-1 * var(--space-2));
  padding: 0;
  list-style: none;
}

.overdue-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
  padding: var(--space-2);
  border: 0;
  border-radius: var(--radius-md);
  background: none;
  font: inherit;
  text-align: left;
  color: inherit;
  cursor: pointer;
}

.overdue-row:hover {
  background: var(--bg-hover);
}

.overdue-row.current {
  background: var(--bg-selected);
}

.overdue-text {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.overdue-title {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.overdue-where,
.due-date {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.overdue-late {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  flex: none;
}

.late-text {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  color: var(--danger-text);
}

.none-late {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-6) 0;
  justify-content: center;
  font-size: var(--fs-sm);
  color: var(--text-tertiary);
}

@container page (max-width: 1100px) {
  .tiles {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@container page (max-width: 820px) {
  .grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@container page (max-width: 639px) {
  .dashboard {
    padding: var(--space-4) var(--space-3) var(--space-8);
  }

  .tiles {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .tiles > :first-child {
    grid-column: 1 / -1;
  }

  .controls,
  .where {
    width: 100%;
  }

  .due {
    height: 180px;
  }

  .progress-col {
    display: none;
  }
}
</style>

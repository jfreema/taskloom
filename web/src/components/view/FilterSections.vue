<script setup lang="ts">
// The checkbox lists of the filter popovers (7.12): Status, Assignee (Me, Unassigned, then the
// other members), Priority, Tags and Due. FilterButton shows every facet; a facet chip shows its
// own. Up and Down (and Home, End) move between the checkboxes; Space toggles. Each list is a
// group named by its heading.
import { computed, ref, useId } from 'vue'
import { UserRoundPlus } from '@lucide/vue'
import { DUE_FILTERS, type TaskFilters } from '../../lib/taskQuery'
import { useMembers } from '../../stores/members'
import { useSession } from '../../stores/session'
import { useTags } from '../../stores/tags'
import { useWorkspaces } from '../../stores/workspaces'
import { PRIORITIES, type Priority, type Uuid } from '../../types'
import PriorityFlag from '../fields/PriorityFlag.vue'
import StatusIcon from '../fields/StatusIcon.vue'
import UiAvatar from '../ui/UiAvatar.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiIcon from '../ui/UiIcon.vue'
import { FACETS, FACET_LABEL, hasValue, toggleValue, valueLabel, type FacetKey } from './filterFacets'

const props = withDefaults(
  defineProps<{
    modelValue: TaskFilters
    listId: Uuid
    facets?: readonly FacetKey[]
  }>(),
  { facets: () => FACETS },
)

const emit = defineEmits<{ 'update:modelValue': [filters: TaskFilters] }>()

const uid = useId()
const root = ref<HTMLElement | null>(null)
const workspaces = useWorkspaces()
const members = useMembers()
const tags = useTags()
const session = useSession()

const meId = computed(() => session.user?.id ?? null)

const lookups = {
  statusName: (id: Uuid) => workspaces.statusById(id)?.name,
  memberName: (id: Uuid) => members.nameOf(id),
  tagName: (id: Uuid) => tags.get(id)?.name,
}

interface Row {
  value: string
  label: string
}

const statusRows = computed<Row[]>(() => workspaces.statusesFor(props.listId).map((s) => ({ value: s.id, label: s.name })))

const assigneeRows = computed<Row[]>(() => {
  const me = meId.value
  const rows: Row[] = [
    { value: 'me', label: 'Me' },
    { value: 'unassigned', label: 'Unassigned' },
  ]
  for (const m of members.current) if (m.userId !== me) rows.push({ value: m.userId, label: m.displayName })
  // People who left the workspace but are still in the saved filter can be unchecked.
  for (const v of props.modelValue.assignees) {
    if (v !== 'me' && v !== 'unassigned' && v !== me && !members.get(v)) rows.push({ value: v, label: 'Former member' })
  }
  return rows
})

const priorityRows = computed<Row[]>(() => PRIORITIES.map((p) => ({ value: p, label: valueLabel('priorities', p, lookups) })))
const tagRows = computed<Row[]>(() => tags.current.map((t) => ({ value: t.id, label: t.name })))
const dueRows = computed<Row[]>(() => DUE_FILTERS.map((d) => ({ value: d, label: valueLabel('due', d, lookups) })))

function rowsFor(key: FacetKey): Row[] {
  switch (key) {
    case 'statusIds':
      return statusRows.value
    case 'assignees':
      return assigneeRows.value
    case 'priorities':
      return priorityRows.value
    case 'tagIds':
      return tagRows.value
    case 'due':
      return dueRows.value
  }
}

function toggle(key: FacetKey, value: string): void {
  emit('update:modelValue', toggleValue(props.modelValue, key, value))
}

function onKeydown(e: KeyboardEvent): void {
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(e.key) || e.altKey || e.ctrlKey || e.metaKey) return
  const boxes = [...(root.value?.querySelectorAll<HTMLInputElement>('input[type="checkbox"]') ?? [])]
  if (!boxes.length) return
  const i = boxes.indexOf(document.activeElement as HTMLInputElement)
  let next: number
  if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = boxes.length - 1
  else if (i === -1) next = 0
  else next = Math.min(boxes.length - 1, Math.max(0, i + (e.key === 'ArrowDown' ? 1 : -1)))
  e.preventDefault()
  boxes[next]?.focus()
}

const statusOf = (id: string) => workspaces.statusById(id)
const userOf = (id: string) => ({ id: id === 'me' ? (meId.value ?? 'me') : id, displayName: id === 'me' ? (session.user?.displayName ?? 'Me') : members.nameOf(id) })
const colorOf = (id: string) => tags.get(id)?.color ?? 'var(--text-tertiary)'
</script>

<template>
  <div ref="root" class="filter-sections" @keydown="onKeydown">
    <div v-for="key in facets" :key="key" class="section">
      <div :id="`${uid}-${key}`" class="heading">{{ FACET_LABEL[key] }}</div>
      <div class="rows" role="group" :aria-labelledby="`${uid}-${key}`">
        <label v-for="row in rowsFor(key)" :key="row.value" class="row">
          <UiCheckbox :model-value="hasValue(modelValue, key, row.value)" @update:model-value="toggle(key, row.value)" />
          <template v-if="key === 'statusIds' && statusOf(row.value)">
            <StatusIcon :status="statusOf(row.value)!" :size="16" />
            <span class="text">{{ row.label }}</span>
          </template>
          <template v-else-if="key === 'assignees'">
            <span v-if="row.value === 'unassigned'" class="glyph" aria-hidden="true">
              <UiIcon :icon="UserRoundPlus" :size="16" />
            </span>
            <UiAvatar v-else :user="userOf(row.value)" :size="20" :pending="members.get(row.value)?.pending ?? false" decorative />
            <span class="text">{{ row.label }}</span>
          </template>
          <template v-else-if="key === 'priorities'">
            <PriorityFlag :priority="row.value as Priority" show-empty decorative />
            <span class="text">{{ row.label }}</span>
          </template>
          <template v-else-if="key === 'tagIds'">
            <span class="dot" :style="{ '--c': colorOf(row.value) }" aria-hidden="true" />
            <span class="text">{{ row.label }}</span>
          </template>
          <span v-else class="text">{{ row.label }}</span>
        </label>
        <p v-if="key === 'tagIds' && !rowsFor(key).length" class="none">No tags yet.</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.filter-sections {
  display: flex;
  flex-direction: column;
  padding: var(--space-1);
}

.section + .section {
  margin-top: var(--space-1);
  padding-top: var(--space-1);
  border-top: 1px solid var(--border-subtle);
}

.heading {
  padding: var(--space-1-5) var(--space-2) var(--space-1);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
}

.rows {
  display: flex;
  flex-direction: column;
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-menu);
  padding: var(--space-1) var(--space-2);
  border-radius: var(--radius-sm);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  cursor: pointer;
}

.row:hover {
  background: var(--bg-hover);
}

.row:focus-within {
  background: var(--bg-hover);
}

.text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.glyph {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: var(--radius-full);
  background: var(--bg-subtle);
  color: var(--text-tertiary);
}

.dot {
  flex-shrink: 0;
  width: 10px;
  height: 10px;
  margin: 0 var(--space-1);
  border-radius: var(--radius-full);
  background: var(--c);
}

.none {
  margin: 0;
  padding: var(--space-1) var(--space-2) var(--space-2);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

@media (pointer: coarse) {
  .row {
    min-height: var(--touch-min);
  }
}
</style>

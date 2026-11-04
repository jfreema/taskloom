<script setup lang="ts">
// /_ui state table of the 7.11 fields, on the signed-in user's workspace (FieldGallery loads it;
// seed it for the full picture): the status, priority, assignee, date, tag and list pickers and the
// complete toggle, each in every state it has. A field's Disabled is its read-only form, the one
// guests see (9.4); its Empty is "none, add".
import { computed } from 'vue'
import { addDaysIso, todayIso } from '../../../lib/dates'
import { useMembers } from '../../../stores/members'
import { useTags } from '../../../stores/tags'
import { useWorkspaces } from '../../../stores/workspaces'
import AssigneePicker from '../../fields/AssigneePicker.vue'
import CompleteToggle from '../../fields/CompleteToggle.vue'
import DateField from '../../fields/DateField.vue'
import ListPicker from '../../fields/ListPicker.vue'
import PriorityPicker from '../../fields/PriorityPicker.vue'
import StatusPicker from '../../fields/StatusPicker.vue'
import TagPicker from '../../fields/TagPicker.vue'
import StateRow from './StateRow.vue'
import StateTable from './StateTable.vue'
import type { SampleState } from './states'

const workspaces = useWorkspaces()
const members = useMembers()
const tags = useTags()

/** The seed's "Website refresh" when there is one, else the first list. */
const sample = computed(() => workspaces.allLists.find((x) => x.list.name === 'Website refresh') ?? workspaces.allLists[0] ?? null)
const statusId = computed(() => {
  const statuses = sample.value ? workspaces.statusesFor(sample.value.list.id) : []
  return (statuses[1] ?? statuses[0])?.id ?? null
})
const people = computed(() => members.current.slice(0, 2).map((m) => m.userId))
const tagIds = computed(() => tags.current.slice(0, 2).map((t) => t.id))
const due = addDaysIso(todayIso(), 2)

const PICKER: SampleState[] = ['default', 'hover', 'focus', 'disabled', 'empty']
const STATIC: SampleState[] = ['default', 'hover', 'focus', 'disabled']

const off = (s: SampleState) => s === 'disabled'
const blank = (s: SampleState) => s === 'empty'
const ignore = () => undefined
</script>

<template>
  <StateTable
    title="States · fields"
    note="Disabled is the read-only form that guests see; Empty is the trigger with no value (“none, add”)."
  >
    <template v-if="sample && statusId">
      <StateRow label="Status" note="Pill" :states="STATIC">
        <template #default="{ state }">
          <StatusPicker :model-value="statusId" :list-id="sample.list.id" variant="pill" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="Priority" note="Label" :states="PICKER">
        <template #default="{ state }">
          <PriorityPicker :model-value="blank(state) ? 'None' : 'High'" variant="label" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="Assignees" note="Field" :states="PICKER">
        <template #default="{ state }">
          <AssigneePicker :model-value="blank(state) ? [] : people" variant="field" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="Due date" note="Field" :states="PICKER">
        <template #default="{ state }">
          <DateField :model-value="blank(state) ? null : due" kind="due" variant="field" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="Tags" note="Field" :states="PICKER">
        <template #default="{ state }">
          <TagPicker :model-value="blank(state) ? [] : tagIds" variant="field" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="List" note="Field" :states="PICKER">
        <template #default="{ state }">
          <ListPicker :model-value="blank(state) ? null : sample.list.id" variant="field" :readonly="off(state)" @update:model-value="ignore" />
        </template>
      </StateRow>

      <StateRow label="Complete toggle" note="Empty is open" :states="PICKER">
        <template #default="{ state }">
          <CompleteToggle :done="!blank(state)" :label="`Complete Plan Q1 roadmap (${state})`" :disabled="off(state)" />
        </template>
      </StateRow>
    </template>
    <tr v-else>
      <td colspan="8" class="none">This workspace has no list yet. Seed it (POST /api/dev/seed) to see the fields.</td>
    </tr>
  </StateTable>
</template>

<style scoped>
.none {
  padding: var(--space-4) 0;
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}
</style>

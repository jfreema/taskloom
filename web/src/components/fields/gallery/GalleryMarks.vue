<script setup lang="ts">
// /_ui: CompleteToggle (open with the status colour, done, the sizes, disabled, read-only) and
// PriorityFlag and PriorityPicker (icon, label, chip, read-only; 1–4 and 0 pick while open).
import { computed, ref } from 'vue'
import { useWorkspaces } from '../../../stores/workspaces'
import { PRIORITIES, PRIORITY_LABEL, type Priority, type Uuid } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import CompleteToggle from '../CompleteToggle.vue'
import PriorityFlag from '../PriorityFlag.vue'
import PriorityPicker from '../PriorityPicker.vue'

const props = defineProps<{ listId: Uuid }>()

const workspaces = useWorkspaces()
const color = computed(() => workspaces.statusesFor(props.listId)[1]?.color)

const doneA = ref(false)
const doneB = ref(true)
const sizes = ref<Record<16 | 18 | 20, boolean>>({ 16: false, 18: true, 20: false })

const iconPriority = ref<Priority>('High')
const labelPriority = ref<Priority>('Normal')
const chipPriority = ref<Priority>('None')
</script>

<template>
  <GalleryCard title="Complete and priority">
    <div class="row">
      <CompleteToggle :done="doneA" label="Draft outline" :color="color" @toggle="doneA = !doneA" />
      <CompleteToggle :done="doneB" label="Collect screenshots" @toggle="doneB = !doneB" />
      <CompleteToggle
        v-for="s in [16, 18, 20] as const"
        :key="s"
        :done="sizes[s]"
        :label="`Size ${s}`"
        :size="s"
        @toggle="sizes[s] = !sizes[s]"
      />
      <CompleteToggle :done="false" label="Disabled task" disabled />
      <CompleteToggle :done="true" label="Read-only task" readonly />
    </div>
    <div class="row">
      <PriorityFlag v-for="p in PRIORITIES" :key="p" :priority="p" show-label show-empty />
    </div>
    <div class="row">
      <PriorityFlag v-for="p in PRIORITIES" :key="p" :priority="p" :size="14" />
    </div>
    <div class="row">
      <PriorityPicker v-model="iconPriority" variant="icon" />
      <PriorityPicker v-model="labelPriority" variant="label" />
      <PriorityPicker v-model="chipPriority" variant="chip" />
      <PriorityPicker :model-value="labelPriority" readonly />
    </div>
    <p class="small muted">
      Icon: {{ PRIORITY_LABEL[iconPriority] }} · Label: {{ PRIORITY_LABEL[labelPriority] }} · Chip: {{ PRIORITY_LABEL[chipPriority] }}
    </p>
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

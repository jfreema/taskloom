<script setup lang="ts">
// /_ui: DueDate (overdue, today, tomorrow, later, done; sm and md) and DateField: a start and due
// pair that keeps start ≤ due (a due date before the start moves the start, and the other way
// round), the cell, chip and icon triggers, and read-only.
import { ref } from 'vue'
import { useToday } from '../../../composables/useToday'
import { addDaysIso, formatDateLong } from '../../../lib/dates'
import type { IsoDate } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import DateField from '../DateField.vue'
import DueDate from '../DueDate.vue'

const today = useToday()
const t = (n: number) => addDaysIso(today.value, n)

const start = ref<IsoDate | null>(t(2))
const due = ref<IsoDate | null>(t(5))
const cellDue = ref<IsoDate | null>(t(-2))
const chipDue = ref<IsoDate | null>(null)
const iconDue = ref<IsoDate | null>(t(1))

const long = (d: IsoDate | null) => (d ? formatDateLong(d) : 'none')
</script>

<template>
  <GalleryCard title="Dates">
    <div class="row">
      <DueDate :date="t(-2)" />
      <DueDate :date="t(0)" />
      <DueDate :date="t(1)" />
      <DueDate :date="t(12)" />
      <DueDate :date="t(-2)" done />
      <DueDate :date="t(-1)" size="sm" show-icon />
      <DueDate :date="t(3)" size="sm" show-icon />
    </div>
    <div class="row">
      <DateField v-model="start" v-model:other-date="due" kind="start" variant="field" />
      <DateField v-model="due" v-model:other-date="start" kind="due" variant="field" />
    </div>
    <p class="small muted">Start: {{ long(start) }} · Due: {{ long(due) }}</p>
    <div class="row">
      <DateField v-model="cellDue" kind="due" variant="cell" />
      <DateField v-model="chipDue" kind="due" variant="chip" />
      <DateField v-model="iconDue" kind="due" variant="icon" />
      <DateField :model-value="due" kind="due" variant="field" readonly />
    </div>
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

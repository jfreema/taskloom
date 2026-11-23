<script setup lang="ts">
// Created and completed tasks per day for the last 14 days: a pair of columns per day on one axis
// (a hairline grid with three round ticks), today's label in the primary ink. Each day has a tooltip
// with both numbers, and a visually hidden table holds the same data for screen readers.
import { computed } from 'vue'
import { fromIso } from '../../lib/dates'
import type { Dashboard, IsoDate } from '../../types'

const props = defineProps<{ trend: Dashboard['trend']; today: IsoDate }>()

/** A round top for the axis (at least 4), with a tick at its half: 4, 6, 8, 10, 20, 30, 40, 60, … */
const top = computed(() => {
  const max = Math.max(4, ...props.trend.map((d) => Math.max(d.created, d.completed)))
  const p = 10 ** Math.floor(Math.log10(max))
  return [1, 2, 3, 4, 6, 8, 10].map((m) => m * p).find((s) => s >= max && s % 2 === 0) ?? 10 * p
})
const ticks = computed(() => [top.value, top.value / 2, 0])

const fmtDay = new Intl.DateTimeFormat(undefined, { day: 'numeric' })
const fmtWeekday = new Intl.DateTimeFormat(undefined, { weekday: 'narrow' })
const fmtLong = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' })

const days = computed(() =>
  props.trend.map((d) => {
    const date = fromIso(d.date)
    return {
      ...d,
      day: fmtDay.format(date),
      weekday: fmtWeekday.format(date),
      long: fmtLong.format(date),
      isToday: d.date === props.today,
    }
  }),
)

const totals = computed(() => ({
  created: props.trend.reduce((n, d) => n + d.created, 0),
  completed: props.trend.reduce((n, d) => n + d.completed, 0),
}))

const pct = (v: number) => `${(v / top.value) * 100}%`
</script>

<template>
  <div class="dash-trend">
    <div class="legend" aria-hidden="true">
      <span class="key"><span class="swatch created" />Created <strong class="tabular">{{ totals.created }}</strong></span>
      <span class="key"><span class="swatch completed" />Completed <strong class="tabular">{{ totals.completed }}</strong></span>
    </div>
    <div class="plot" aria-hidden="true">
      <div class="grid">
        <div v-for="t in ticks" :key="t" class="tick"><span class="tick-label tabular">{{ t }}</span></div>
      </div>
      <div class="cols">
        <div
          v-for="d in days"
          :key="d.date"
          v-tooltip="{ text: `${d.long}: ${d.created} created, ${d.completed} completed`, placement: 'top' }"
          class="day"
          :class="{ today: d.isToday }"
        >
          <div class="bars">
            <span class="bar created" :style="{ height: pct(d.created) }" />
            <span class="bar completed" :style="{ height: pct(d.completed) }" />
          </div>
          <span class="x"><span class="wd">{{ d.weekday }}</span>{{ d.day }}</span>
        </div>
      </div>
    </div>
    <table class="sr-only">
      <caption>Tasks created and completed per day</caption>
      <thead>
        <tr><th scope="col">Day</th><th scope="col">Created</th><th scope="col">Completed</th></tr>
      </thead>
      <tbody>
        <tr v-for="d in days" :key="d.date"><th scope="row">{{ d.long }}</th><td>{{ d.created }}</td><td>{{ d.completed }}</td></tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.dash-trend {
  --created: var(--border-control);
  --completed: var(--accent);
}

.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  margin-bottom: var(--space-3);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-secondary);
}

.key {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
}

.key strong {
  color: var(--text-primary);
  font-weight: var(--fw-semibold);
}

.swatch {
  width: 10px;
  height: 10px;
  border-radius: 3px;
}

.swatch.created,
.bar.created {
  background: var(--created);
}

.swatch.completed,
.bar.completed {
  background: var(--completed);
}

.plot {
  position: relative;
  height: 180px;
  padding-left: 28px;
}

.grid {
  position: absolute;
  inset: 0 0 22px 28px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
}

.tick {
  position: relative;
  border-top: 1px solid var(--border-subtle);
}

.tick-label {
  position: absolute;
  right: calc(100% + 6px);
  top: -8px;
  font-size: var(--fs-2xs);
  line-height: var(--lh-2xs);
  color: var(--text-tertiary);
}

.cols {
  position: relative;
  display: flex;
  height: 100%;
}

.day {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 0;
  border-radius: var(--radius-sm);
}

.day:hover {
  background: var(--bg-hover);
}

.bars {
  flex: 1;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  gap: 2px;
  width: 100%;
}

.bar {
  width: min(10px, 32%);
  min-height: 0;
  border-radius: 3px 3px 0 0;
  transition: height var(--dur-4) var(--ease-out);
}

.x {
  display: flex;
  flex-direction: column;
  align-items: center;
  height: 22px;
  padding-top: 3px;
  font-size: var(--fs-2xs);
  line-height: 10px;
  color: var(--text-tertiary);
}

.wd {
  font-size: 9px;
  text-transform: uppercase;
}

.today .x {
  color: var(--text-primary);
  font-weight: var(--fw-semibold);
}
</style>

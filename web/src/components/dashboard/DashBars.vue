<script setup lang="ts" generic="Row extends DashBarRow">
// Horizontal bars, one row per item: a lead slot (a swatch, avatar or flag with the name), a track
// with the row's segments laid end to end on a scale shared by every row, and the row's total.
// Segments have a 2px gap between them and a rounded end; a tooltip and the row's accessible text
// spell out every part, so the colours are never the only way to read it.
import { computed } from 'vue'

export interface DashBarSegment {
  value: number
  color: string
  label: string
}

export interface DashBarRow {
  key: string
  label: string
  segments: DashBarSegment[]
  /** Shown after the total, in the secondary ink ("12 done"). */
  extra?: string
}

const props = defineProps<{ rows: Row[]; max?: number }>()

defineSlots<{ lead(p: { row: Row }): unknown }>()

const total = (r: DashBarRow) => r.segments.reduce((n, s) => n + s.value, 0)
const scale = computed(() => Math.max(props.max ?? 0, ...props.rows.map(total), 1))

function describe(r: DashBarRow): string {
  const parts = r.segments.filter((s) => s.value > 0).map((s) => `${s.value} ${s.label}`)
  return `${r.label}: ${total(r)}${parts.length > 1 ? ` (${parts.join(', ')})` : ''}${r.extra ? `, ${r.extra}` : ''}`
}
</script>

<template>
  <ul class="dash-bars" role="list">
    <li v-for="r in rows" :key="r.key" class="row" :aria-label="describe(r)">
      <div class="lead" aria-hidden="true"><slot name="lead" :row="r" /></div>
      <div v-tooltip="{ text: describe(r), placement: 'top' }" class="track" aria-hidden="true">
        <span
          v-for="(s, i) in r.segments.filter((x) => x.value > 0)"
          :key="i"
          class="seg"
          :style="{ width: `${(s.value / scale) * 100}%`, background: s.color }"
        />
      </div>
      <span class="num tabular" aria-hidden="true">
        {{ total(r) }}<span v-if="r.extra" class="extra">{{ r.extra }}</span>
      </span>
    </li>
  </ul>
</template>

<style scoped>
.dash-bars {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.row {
  display: grid;
  grid-template-columns: minmax(96px, 34%) 1fr auto;
  align-items: center;
  gap: var(--space-3);
  min-height: 28px;
}

.lead {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-primary);
}

.track {
  display: flex;
  gap: 2px;
  height: 12px;
  border-radius: var(--radius-xs);
  background: var(--bg-subtle);
  box-shadow: inset 0 0 0 1px var(--border-subtle);
  overflow: hidden;
}

.seg {
  height: 100%;
  min-width: 3px;
  transition: width var(--dur-4) var(--ease-out);
}

.seg:last-child {
  border-radius: 0 var(--radius-xs) var(--radius-xs) 0;
}

.num {
  min-width: 3ch;
  text-align: right;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
  white-space: nowrap;
}

.extra {
  margin-left: var(--space-1-5);
  font-weight: var(--fw-regular);
  color: var(--text-tertiary);
}
</style>

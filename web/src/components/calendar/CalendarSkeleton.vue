<script setup lang="ts">
// The Calendar's first-load skeleton (spec 8.9 "Loading: a grid skeleton"): the header, the weekday
// row and five weeks of days with a few chip bars, or the agenda's rows on narrow pages.
import UiSkeleton from '../ui/UiSkeleton.vue'

defineProps<{ agenda: boolean }>()

const CHIPS: Record<number, string[]> = { 2: ['70%'], 8: ['85%', '55%'], 10: ['60%'], 16: ['75%'], 19: ['50%', '80%'], 24: ['65%'], 29: ['70%'] }
</script>

<template>
  <div class="calendar-skeleton" aria-hidden="true">
    <div class="head">
      <UiSkeleton variant="rect" width="28px" height="28px" />
      <UiSkeleton variant="rect" width="28px" height="28px" />
      <UiSkeleton variant="rect" width="56px" height="28px" />
      <UiSkeleton variant="text" width="140px" height="18px" />
    </div>
    <div v-if="agenda" class="rows">
      <div v-for="i in 8" :key="i" class="row">
        <UiSkeleton variant="circle" width="18px" height="18px" />
        <UiSkeleton variant="text" :width="`${40 + ((i * 17) % 40)}%`" />
      </div>
    </div>
    <div v-else class="grid">
      <div v-for="i in 7" :key="`w${i}`" class="weekday"><UiSkeleton variant="text" width="28px" /></div>
      <div v-for="i in 35" :key="i" class="cell">
        <UiSkeleton variant="text" width="14px" />
        <UiSkeleton v-for="(w, j) in CHIPS[i] ?? []" :key="j" variant="rect" :width="w" height="18px" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.calendar-skeleton {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}

.head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: 44px;
  flex: none;
}

.grid {
  display: grid;
  flex: 1 1 auto;
  grid-template-columns: repeat(7, minmax(0, 1fr));
  grid-template-rows: 28px repeat(5, minmax(112px, 1fr));
  gap: 1px;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--border-subtle);
  overflow: hidden;
}

.weekday,
.cell {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  padding: var(--space-1-5) var(--space-2);
  background: var(--bg-surface);
}

.weekday {
  justify-content: center;
}

.rows {
  display: flex;
  flex-direction: column;
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: 48px;
  border-bottom: 1px solid var(--border-subtle);
}
</style>

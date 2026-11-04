<script setup lang="ts">
// Home's loading state (8.5): the quick add bar, a section heading and 6 rows, shown only once the
// first load has run past 200ms (the parent passes `show`); until then the space stays empty, so a
// fast load never flashes. Skeletons are aria-hidden; the page region carries aria-busy.
import UiSkeleton from '../ui/UiSkeleton.vue'

defineProps<{ show: boolean }>()

const WIDTHS = ['52%', '38%', '61%', '45%', '57%', '33%']
</script>

<template>
  <div class="home-skeleton" aria-hidden="true">
    <template v-if="show">
      <UiSkeleton variant="rect" width="100%" height="40px" />
      <div class="head"><UiSkeleton width="96px" /></div>
      <div v-for="(w, i) in WIDTHS" :key="i" class="row">
        <UiSkeleton variant="circle" width="16px" height="16px" />
        <UiSkeleton :width="w" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.home-skeleton {
  display: flex;
  flex-direction: column;
  margin-top: var(--space-5);
}

.head {
  display: flex;
  align-items: center;
  height: var(--control-md);
  margin-top: var(--space-6);
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: 40px;
  padding: 0 var(--space-2);
}
</style>

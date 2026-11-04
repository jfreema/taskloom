<script setup lang="ts">
// A neutral page skeleton for the list page while its view's code loads or the workspace switches
// (8.1: shown only after 200ms, never the word "Loading…"). Each view draws its own first-load
// skeleton that matches its layout.
import UiSkeleton from '../ui/UiSkeleton.vue'

const ROWS = ['46%', '62%', '38%', '55%', '70%', '42%', '58%', '34%']
</script>

<template>
  <div class="view-skeleton" aria-hidden="true">
    <div v-for="(w, i) in ROWS" :key="i" class="row">
      <UiSkeleton variant="circle" width="16px" height="16px" />
      <UiSkeleton :width="w" />
    </div>
  </div>
</template>

<style scoped>
.view-skeleton {
  display: flex;
  flex-direction: column;
  padding: var(--space-2) var(--space-4);
}

.row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: var(--row-list);
  border-bottom: 1px solid var(--border-subtle);
}
</style>

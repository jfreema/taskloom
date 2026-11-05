<script setup lang="ts">
// The page skeleton (8.2): a header bar and eight rows. AppShell shows it while the workspace tree
// loads, and App.vue's boot frame while the session loads. Leaving, it hands over to the page's
// own skeleton (skeletonHandover).
import { onBeforeUnmount } from 'vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import { skeletonHandover } from './skeletonHandover'

const ROWS = ['46%', '62%', '38%', '55%', '70%', '42%', '58%', '34%']

onBeforeUnmount(skeletonHandover)
</script>

<template>
  <div class="page-skeleton" aria-hidden="true">
    <div class="skeleton-header">
      <UiSkeleton variant="rect" width="18px" height="18px" />
      <UiSkeleton width="180px" />
    </div>
    <div class="skeleton-rows">
      <div v-for="(w, i) in ROWS" :key="i" class="skeleton-row">
        <UiSkeleton variant="circle" width="16px" height="16px" />
        <UiSkeleton :width="w" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.page-skeleton {
  display: flex;
  flex-direction: column;
}

.skeleton-header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: var(--topbar-h);
  padding: 0 var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
}

.skeleton-rows {
  display: flex;
  flex-direction: column;
  padding: var(--space-3) var(--space-4);
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  height: var(--row-list);
  padding: 0 var(--space-2);
  border-bottom: 1px solid var(--border-subtle);
}
</style>

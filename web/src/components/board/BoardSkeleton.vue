<script setup lang="ts">
// The Board's first-load skeleton (8.8): 4 columns of 3 cards, shaped like the real ones. Shown
// only after 200ms (8.1).
import UiSkeleton from '../ui/UiSkeleton.vue'

const COLUMNS = [
  { name: '38%', cards: ['82%', '64%', '74%'] },
  { name: '52%', cards: ['70%', '88%', '58%'] },
  { name: '30%', cards: ['76%', '60%', '84%'] },
  { name: '44%', cards: ['66%', '80%', '54%'] },
]
</script>

<template>
  <div class="board-skeleton" aria-hidden="true">
    <div v-for="(c, i) in COLUMNS" :key="i" class="column">
      <div class="head">
        <UiSkeleton variant="circle" width="16px" height="16px" />
        <UiSkeleton :width="c.name" />
      </div>
      <div v-for="(w, j) in c.cards" :key="j" class="card">
        <UiSkeleton :width="w" />
        <div class="meta">
          <UiSkeleton width="56px" />
          <UiSkeleton variant="circle" width="20px" height="20px" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.board-skeleton {
  display: flex;
  gap: var(--space-3);
  padding: var(--space-4);
  overflow: hidden;
}

.column {
  display: flex;
  flex: none;
  flex-direction: column;
  gap: var(--space-2);
  width: 280px;
  padding: 0 var(--space-2) var(--space-2);
  border-radius: var(--radius-lg);
  background: var(--bg-column);
}

.head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: 40px;
  padding: 0 var(--space-1);
}

.head > :last-child {
  flex: none;
}

.card {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: 10px var(--space-3) 12px;
  border: 1px solid var(--border-default);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  box-shadow: var(--shadow-1);
}

.meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

@media (max-width: 639px) {
  .column {
    width: 85vw;
  }
}
</style>

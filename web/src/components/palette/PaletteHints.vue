<script setup lang="ts">
// The palette's key hints (8.11), stuck to the bottom of its scroll area. Phones don't show them.
import UiKbd from '../ui/UiKbd.vue'
</script>

<template>
  <footer class="palette-hints" aria-hidden="true">
    <span class="hint"><UiKbd keys="ArrowUp" /><UiKbd keys="ArrowDown" /> to move</span>
    <span class="hint"><UiKbd keys="Enter" /> to open</span>
    <span class="hint"><UiKbd keys="Escape" /> to close</span>
  </footer>
</template>

<style scoped>
.palette-hints {
  position: sticky;
  bottom: 0;
  z-index: 1;
  display: flex;
  flex: none;
  align-items: center;
  gap: var(--space-4);
  height: 36px;
  padding: 0 var(--space-4);
  border-top: 1px solid var(--border-subtle);
  background: var(--bg-raised);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.hint {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
}

@media (max-width: 639px) {
  .palette-hints {
    display: none;
  }
}
</style>

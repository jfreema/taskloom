<script setup lang="ts">
// One demo card in the /_ui gallery: a heading and the demo.
withDefaults(defineProps<{ title: string; wide?: boolean }>(), { wide: false })
</script>

<template>
  <section class="card" :class="{ wide }">
    <h2 class="card-title">{{ title }}</h2>
    <slot />
  </section>
</template>

<style scoped>
.card {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
  padding: var(--space-4) var(--space-5) var(--space-5);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  background: var(--bg-surface);
  box-shadow: var(--shadow-1);
}

.card.wide {
  grid-column: span 2;
}

.card-title {
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
}

@media (max-width: 800px) {
  .card.wide {
    grid-column: auto;
  }
}
</style>

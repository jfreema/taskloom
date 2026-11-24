<script setup lang="ts">
// A dashboard card: a titled surface with an optional caption and a slot for actions on the right.
// The title is an h2, so the page reads as a list of sections.
import { useId } from 'vue'

withDefaults(defineProps<{ title: string; caption?: string; span?: 1 | 2 }>(), { caption: undefined, span: 1 })

defineSlots<{ default(): unknown; actions?(): unknown }>()

const headingId = useId()
</script>

<template>
  <section class="dash-card" :class="{ wide: span === 2 }" :aria-labelledby="headingId">
    <header class="head">
      <div class="titles">
        <h2 :id="headingId" class="title">{{ title }}</h2>
        <p v-if="caption" class="caption">{{ caption }}</p>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <div class="body"><slot /></div>
  </section>
</template>

<style scoped>
.dash-card {
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding: var(--space-4) var(--space-5) var(--space-5);
  background: var(--bg-surface);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-1);
}

.wide {
  grid-column: 1 / -1;
}

.head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
  /* Pelican (Look 3) rules the title off from the body, edge to edge across the card. */
  margin: 0 calc(var(--space-5) * -1) var(--space-4);
  padding: 0 var(--space-5) var(--card-head-pad);
  border-bottom: var(--card-head-rule-w) solid var(--border-subtle);
}

.title {
  margin: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-heading);
  color: var(--heading-color);
}

.caption {
  margin: var(--space-0-5) 0 0;
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
}

.body {
  flex: 1;
  min-width: 0;
}
</style>

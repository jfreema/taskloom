<script setup lang="ts">
// The frame of the workspace settings pages (8.13: General, Members, Tags): an h2 title (18/26 600)
// with its actions on the right, a one-line description, then the page (max 720px). Below 640px
// the actions wrap under the title. The page is a region named by its title.
import { useId } from 'vue'

defineProps<{
  title: string
  description?: string
  /** aria-busy while the page's data loads. */
  busy?: boolean
}>()

const titleId = `${useId()}-title`
</script>

<template>
  <section class="settings-page" :aria-labelledby="titleId" :aria-busy="busy ? 'true' : undefined">
    <header class="page-header">
      <div class="heading">
        <h2 :id="titleId" class="page-title">{{ title }}</h2>
        <p v-if="description" class="page-description">{{ description }}</p>
      </div>
      <div v-if="$slots.actions" class="page-actions"><slot name="actions" /></div>
    </header>
    <slot />
  </section>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  width: 100%;
  max-width: 720px;
  min-width: 0;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--space-3) var(--space-4);
}

.heading {
  flex: 1 1 280px;
  min-width: 0;
}

.settings-page .page-title {
  font-size: var(--fs-xl);
  line-height: var(--lh-xl);
  font-weight: var(--fw-semibold);
  letter-spacing: -0.01em;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.page-description {
  margin-top: var(--space-1);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-secondary);
  overflow-wrap: anywhere;
}

.page-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
}
</style>

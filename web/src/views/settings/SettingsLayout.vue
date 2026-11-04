<script setup lang="ts">
// Settings frame (spec 8.13). Placeholder until B3 builds it: the "Settings" h1, the inner
// navigation and the child page.
import PageHeader from '../../components/shell/PageHeader.vue'

const links = [
  { name: 'settings-profile', label: 'Profile' },
  { name: 'settings-appearance', label: 'Appearance' },
  { name: 'settings-workspace', label: 'General' },
  { name: 'settings-members', label: 'Members' },
  { name: 'settings-tags', label: 'Tags' },
] as const
</script>

<template>
  <PageHeader title="Settings">
    <h1 class="heading">Settings</h1>
  </PageHeader>
  <div class="settings">
    <nav class="nav" aria-label="Settings">
      <RouterLink v-for="l in links" :key="l.name" class="nav-link focus-inset" :to="{ name: l.name }">
        {{ l.label }}
      </RouterLink>
    </nav>
    <section class="content">
      <RouterView />
    </section>
  </div>
</template>

<style scoped>
.heading {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.settings {
  display: grid;
  grid-template-columns: 200px minmax(0, 1fr);
  gap: var(--space-8);
  width: 100%;
  max-width: 960px;
  padding: var(--space-6);
}

.nav {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
}

.nav-link {
  display: flex;
  align-items: center;
  height: var(--row-sidebar);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  text-decoration: none;
}

.nav-link:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.nav-link.router-link-active {
  background: var(--bg-selected);
  color: var(--accent-text);
  font-weight: var(--fw-medium);
}

.content :deep(h2) {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

@media (max-width: 639px) {
  .settings {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--space-4);
    padding: var(--space-4);
  }

  .nav {
    flex-direction: row;
    overflow-x: auto;
  }

  .nav-link {
    flex-shrink: 0;
  }
}
</style>

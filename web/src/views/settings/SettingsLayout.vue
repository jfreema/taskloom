<script setup lang="ts">
// Settings frame (spec 8.13): the "Settings" h1 in the page header, an inner
// <nav aria-label="Settings"> (220px) with the groups Account (Profile, Appearance) and Workspace
// (General, Members, Tags), and the child page (max 720px). It stays beside the page while it
// scrolls. Below 640px of page width the nav becomes one row of links that scrolls sideways, and
// the active link is scrolled into view.
import { nextTick, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import PageHeader from '../../components/shell/PageHeader.vue'

const groups = [
  {
    id: 'account',
    label: 'Account',
    links: [
      { name: 'settings-profile', label: 'Profile' },
      { name: 'settings-appearance', label: 'Appearance' },
    ],
  },
  {
    id: 'workspace',
    label: 'Workspace',
    links: [
      { name: 'settings-workspace', label: 'General' },
      { name: 'settings-members', label: 'Members' },
      { name: 'settings-tags', label: 'Tags' },
    ],
  },
] as const

const route = useRoute()
const nav = ref<HTMLElement | null>(null)

// In the sideways row, keep the current page's link in view.
watch(
  () => route.name,
  () => {
    void nextTick(() => {
      const el = nav.value
      if (!el || el.scrollWidth <= el.clientWidth) return
      el.querySelector<HTMLElement>('[aria-current="page"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
    })
  },
  { immediate: true, flush: 'post' },
)
</script>

<template>
  <PageHeader title="Settings">
    <h1 class="heading">Settings</h1>
  </PageHeader>
  <div class="settings">
    <nav ref="nav" class="nav" aria-label="Settings">
      <div v-for="g in groups" :key="g.id" class="group">
        <p :id="`settings-nav-${g.id}`" class="group-label">{{ g.label }}</p>
        <ul class="links" role="list" :aria-labelledby="`settings-nav-${g.id}`">
          <li v-for="l in g.links" :key="l.name">
            <RouterLink class="nav-link" :to="{ name: l.name }">{{ l.label }}</RouterLink>
          </li>
        </ul>
      </div>
    </nav>
    <div class="content">
      <RouterView />
    </div>
  </div>
</template>

<style scoped>
.heading {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.settings {
  display: grid;
  grid-template-columns: 220px minmax(0, 1fr);
  align-items: start;
  gap: var(--space-10);
  width: 100%;
  max-width: calc(220px + var(--space-10) + 720px + 2 * var(--space-8));
  padding: var(--space-8);
}

.nav {
  position: sticky;
  top: calc(var(--topbar-h) + var(--space-8));
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.group-label {
  padding: 0 var(--space-2);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
  color: var(--text-tertiary);
}

.links {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  margin: 0;
  padding: 0;
  list-style: none;
}

.nav-link {
  display: flex;
  align-items: center;
  height: var(--row-sidebar);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-decoration: none;
  white-space: nowrap;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.nav-link:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.nav-link:focus-visible {
  outline: 2px solid var(--focus-ring);
  outline-offset: -2px;
}

.nav-link.router-link-active {
  background: var(--bg-selected);
  color: var(--accent-text);
  font-weight: var(--fw-medium);
}

.content {
  min-width: 0;
}

@container page (width < 900px) {
  .settings {
    gap: var(--space-8);
    padding: var(--space-6);
    grid-template-columns: 180px minmax(0, 1fr);
  }
}

/* Phones and narrow pages: one row of links that scrolls sideways, above the page. */
@container page (width < 640px) {
  .settings {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--space-5);
    padding: var(--space-3) 0 var(--space-6);
  }

  .nav {
    position: static;
    flex-direction: row;
    align-items: center;
    gap: var(--space-1);
    padding: var(--space-1) var(--space-4);
    overflow-x: auto;
    scrollbar-width: none;
    overscroll-behavior-x: contain;
  }

  .nav::-webkit-scrollbar {
    display: none;
  }

  .group {
    flex-direction: row;
    align-items: center;
    flex-shrink: 0;
  }

  /* A thin rule between the two groups stands in for their labels. */
  .group + .group::before {
    content: "";
    flex-shrink: 0;
    width: 1px;
    height: var(--space-5);
    margin: 0 var(--space-1);
    background: var(--border-default);
  }

  .group-label {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }

  .links {
    flex-direction: row;
    gap: var(--space-1);
  }

  .nav-link {
    height: var(--control-md);
    padding: 0 var(--space-3);
    border-radius: var(--radius-full);
  }

  .content {
    padding: 0 var(--space-4);
  }
}
</style>

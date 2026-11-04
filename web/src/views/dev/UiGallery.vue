<script setup lang="ts">
// The primitive gallery at /_ui (spec 3.2, development only): one demo per 7.10 primitive, with a
// theme switch so each can be checked in both themes. The full state matrix is B6's (10.4). The
// demos live in components/ui/gallery, one file per group.
import { Monitor, Moon, Sun } from '@lucide/vue'
import { useTheme } from '../../composables/useTheme'
import { useBreakpoint } from '../../composables/useBreakpoint'
import type { ThemePreference } from '../../types'
import AppLogo from '../../components/shell/AppLogo.vue'
import UiSegmented from '../../components/ui/UiSegmented.vue'
import GalleryBasics from '../../components/ui/gallery/GalleryBasics.vue'
import GalleryEdges from '../../components/ui/gallery/GalleryEdges.vue'
import GalleryFeedback from '../../components/ui/gallery/GalleryFeedback.vue'
import GalleryForms from '../../components/ui/gallery/GalleryForms.vue'
import GalleryMenus from '../../components/ui/gallery/GalleryMenus.vue'
import GalleryOverlays from '../../components/ui/gallery/GalleryOverlays.vue'
import GalleryPickers from '../../components/ui/gallery/GalleryPickers.vue'
import GalleryStatus from '../../components/ui/gallery/GalleryStatus.vue'
import FieldGallery from '../../components/fields/FieldGallery.vue'

const { preference, setPreference } = useTheme()
const { isPhone } = useBreakpoint()

const themeOptions = [
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
]

function setTheme(v: string): void {
  setPreference(v as ThemePreference)
}
</script>

<template>
  <div class="gallery">
    <header class="top">
      <div class="brand">
        <AppLogo :size="24" />
        <h1 class="title">UI gallery</h1>
      </div>
      <UiSegmented
        :model-value="preference"
        :options="themeOptions"
        aria-label="Theme"
        size="sm"
        :icon-only="isPhone"
        @update:model-value="setTheme"
      />
    </header>

    <main class="grid">
      <GalleryBasics />
      <GalleryForms />
      <GalleryMenus />
      <GalleryPickers />
      <GalleryOverlays />
      <GalleryFeedback />
      <GalleryStatus />
      <GalleryEdges />
      <FieldGallery />
    </main>
  </div>
</template>

<style scoped>
.gallery {
  min-height: 100dvh;
  background: var(--bg-canvas);
}

.top {
  position: sticky;
  top: 0;
  z-index: var(--z-sticky);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  height: var(--topbar-h);
  padding: 0 var(--space-6);
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-surface);
}

.brand {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
}

.title {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.grid {
  display: grid;
  grid-auto-flow: dense;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 340px), 1fr));
  gap: var(--space-4);
  max-width: 1280px;
  margin: 0 auto;
  padding: var(--space-6);
}

@media (max-width: 639px) {
  .top {
    padding: 0 var(--space-4);
  }

  .grid {
    padding: var(--space-4);
  }
}
</style>

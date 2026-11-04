<script setup lang="ts">
// The two visually hidden live regions behind useAnnouncer() (spec 7.10), mounted once in App.vue.
// They live in #overlay-root, so they keep announcing while a modal makes #app inert.
import { useAnnouncerRegions } from '../../composables/useAnnouncer'

const { polite, assertive } = useAnnouncerRegions()
</script>

<template>
  <Teleport to="#overlay-root">
    <div class="sr-only" aria-live="polite" aria-atomic="true">{{ polite }}</div>
    <div class="sr-only" aria-live="assertive" aria-atomic="true">{{ assertive }}</div>
  </Teleport>
</template>

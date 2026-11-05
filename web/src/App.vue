<script setup lang="ts">
// The root: the routed page plus the hosts every page needs, the sign-in page included (spec 3.6):
// toasts, confirm dialogs, the shared tooltip and the screen-reader announcer.
// The first navigation waits for the session (router.ts); for a page that needs sign-in, the
// shell's frame (BootFrame) shows meanwhile, after 200ms, so a slow start is never a blank canvas.
import { computed, watchEffect } from 'vue'
import { RouterView, START_LOCATION, useRoute, useRouter } from 'vue-router'
import { pageTitle } from './router'
import BootFrame from './components/shell/BootFrame.vue'
import { useDelayedFlag } from './composables/useDelayedFlag'
import UiAnnouncer from './components/ui/UiAnnouncer.vue'
import UiConfirmHost from './components/ui/UiConfirmHost.vue'
import UiToastHost from './components/ui/UiToastHost.vue'
import UiTooltipHost from './components/ui/UiTooltipHost.vue'

const route = useRoute()
const router = useRouter()

const publicTarget = !!router.resolve(window.location.pathname + window.location.search + window.location.hash).meta.public
const booting = useDelayedFlag(computed(() => !publicTarget && router.currentRoute.value === START_LOCATION))

// afterEach sets the title on navigation; this keeps it current when the page's data arrives or
// changes (a list loaded after the route, a rename).
watchEffect(() => {
  document.title = pageTitle(route)
})
</script>

<template>
  <BootFrame v-if="booting" />
  <RouterView />
  <UiToastHost />
  <UiConfirmHost />
  <UiTooltipHost />
  <UiAnnouncer />
</template>

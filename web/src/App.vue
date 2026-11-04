<script setup lang="ts">
// The root: the routed page plus the hosts every page needs, the sign-in page included (spec 3.6):
// toasts, confirm dialogs, the shared tooltip and the screen-reader announcer.
import { watchEffect } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import { pageTitle } from './router'
import UiAnnouncer from './components/ui/UiAnnouncer.vue'
import UiConfirmHost from './components/ui/UiConfirmHost.vue'
import UiToastHost from './components/ui/UiToastHost.vue'
import UiTooltipHost from './components/ui/UiTooltipHost.vue'

const route = useRoute()

// afterEach sets the title on navigation; this keeps it current when the page's data arrives or
// changes (a list loaded after the route, a rename).
watchEffect(() => {
  document.title = pageTitle(route)
})
</script>

<template>
  <RouterView />
  <UiToastHost />
  <UiConfirmHost />
  <UiTooltipHost />
  <UiAnnouncer />
</template>

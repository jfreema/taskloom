<script setup lang="ts">
// An error boundary around the list page's view: when the view fails to set up or render, the
// content area shows "Couldn't show this view" with "Try again" (which mounts it afresh) while the
// header and toolbar stay usable (8.1). The error is still logged. Errors from event handlers and
// watchers pass through untouched: a failed click must not take the whole view down.
import { onErrorCaptured, ref, watch } from 'vue'
import UiErrorState from '../ui/UiErrorState.vue'

const props = defineProps<{ resetKey: string }>()

const failed = ref(false)
const attempt = ref(0)

// Vue names the phase in development and gives its code in production (0 setup, 1 render).
function isRenderError(info: string): boolean {
  return info === 'setup function' || info === 'render function' || /#runtime-[01]$/.test(info)
}

onErrorCaptured((err, _instance, info) => {
  if (!isRenderError(info)) return
  console.error(err)
  failed.value = true
  return false
})

watch(
  () => props.resetKey,
  () => {
    failed.value = false
  },
)

function retry(): void {
  failed.value = false
  attempt.value++
}
</script>

<template>
  <UiErrorState
    v-if="failed"
    class="view-error"
    title="Couldn't show this view"
    message="Something went wrong. Try again, or reload the page."
    :retry="retry"
  />
  <div v-else :key="attempt" class="view-boundary"><slot /></div>
</template>

<style scoped>
.view-boundary {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.view-error {
  margin: auto;
}
</style>

<script setup lang="ts">
// The loading shuttle (spec 7.10): a 2px track along the bottom edge of its positioned parent
// (PageHeader). A teal shuttle, 30% wide, crosses every 1.2s once `active` has been true for 150ms.
// Under reduced motion it is a static bar at 40% opacity.
import { onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{ active: boolean }>()

const shown = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

watch(
  () => props.active,
  (on) => {
    clearTimeout(timer)
    if (on) timer = setTimeout(() => (shown.value = true), 150)
    else shown.value = false
  },
  { immediate: true },
)

onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <span class="ui-loading-bar" :class="{ 'is-shown': shown }" aria-hidden="true">
    <span class="shuttle" />
  </span>
</template>

<style scoped>
.ui-loading-bar {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 2px;
  overflow: hidden;
  pointer-events: none;
  opacity: 0;
  transition: opacity var(--dur-2) var(--ease-out);
}

.ui-loading-bar.is-shown {
  opacity: 1;
}

.shuttle {
  position: absolute;
  inset: 0;
  background: var(--accent);
  opacity: 0.4;
}

@media (prefers-reduced-motion: no-preference) {
  .shuttle {
    right: auto;
    width: 30%;
    opacity: 1;
    border-radius: var(--radius-full);
    transform: translateX(-100%);
  }

  .is-shown .shuttle {
    animation: loom-shuttle 1.2s var(--ease-in-out) infinite;
  }
}

@keyframes loom-shuttle {
  from { transform: translateX(-100%); }
  to { transform: translateX(334%); }
}
</style>

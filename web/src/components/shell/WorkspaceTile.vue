<script setup lang="ts">
// A workspace's tile (3.4): its initial on --accent. 24px in the sidebar's workspace trigger, 16px
// as the leading icon of the switcher's items. Decorative: the name is always next to it.
import { computed } from 'vue'

const props = withDefaults(defineProps<{ name: string; size?: 16 | 24 }>(), { size: 24 })

const letter = computed(() => (Array.from(props.name.trim())[0] ?? '').toLocaleUpperCase())
</script>

<template>
  <span class="ws-tile" :class="`ws-tile--${size}`" aria-hidden="true">{{ letter }}</span>
</template>

<style scoped>
.ws-tile {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  background: var(--accent);
  color: var(--accent-fg);
  font-weight: var(--fw-semibold);
  line-height: 1;
}

.ws-tile--24 {
  width: 24px;
  height: 24px;
  border-radius: var(--radius-sm);
  font-size: var(--fs-sm);
}

.ws-tile--16 {
  width: 16px;
  height: 16px;
  border-radius: var(--radius-xs);
  font-size: var(--fs-2xs);
}
</style>

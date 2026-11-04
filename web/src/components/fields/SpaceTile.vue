<script setup lang="ts">
// Space tile (7.11): the space's first letter, upper-cased, on its tint (.tint-tile, 7.2).
// Decorative: the space name is always written next to it.
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    space: { name: string; color: string }
    size?: 16 | 18 | 20 | 24 | 32
  }>(),
  { size: 18 },
)

const letter = computed(() => (Array.from(props.space.name.trim())[0] ?? '').toLocaleUpperCase())

const style = computed(() => ({
  '--c': props.space.color,
  '--tile-size': `${props.size}px`,
  '--tile-font': `${Math.round(props.size * 0.55)}px`,
}))
</script>

<template>
  <span class="space-tile tint-tile" :class="{ large: size === 32 }" :style="style" aria-hidden="true">{{ letter }}</span>
</template>

<style scoped>
.space-tile {
  display: inline-flex;
  flex: none;
  align-items: center;
  justify-content: center;
  width: var(--tile-size);
  height: var(--tile-size);
  border-radius: var(--radius-xs);
  font-size: var(--tile-font);
  font-weight: var(--fw-semibold);
  line-height: 1;
  user-select: none;
}

.space-tile.large {
  border-radius: var(--radius-md);
}
</style>

<script setup lang="ts">
// A tag's colour as a menu item icon in the bulk bar's Tags menu: a 10px dot in a 16px box, as in
// the tag picker.
defineProps<{ color: string }>()
</script>

<template>
  <span class="tag-dot-box" aria-hidden="true">
    <span class="tag-dot" :style="{ '--c': color }" />
  </span>
</template>

<style scoped>
.tag-dot-box {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
}

.tag-dot {
  width: 10px;
  height: 10px;
  border-radius: var(--radius-full);
  background: var(--c);
}
</style>

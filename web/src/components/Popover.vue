<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

// Click the trigger to open; click anywhere else (or press Escape) to close.
const open = ref(false)
const root = ref<HTMLElement>()

function onDoc(e: MouseEvent) {
  if (open.value && root.value && !root.value.contains(e.target as Node)) open.value = false
}
function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') open.value = false
}
onMounted(() => {
  document.addEventListener('mousedown', onDoc)
  document.addEventListener('keydown', onKey)
})
onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDoc)
  document.removeEventListener('keydown', onKey)
})
const close = () => (open.value = false)
</script>

<template>
  <span ref="root" class="popover" @click.stop>
    <span @click="open = !open"><slot name="trigger" /></span>
    <div v-if="open" class="menu"><slot :close="close" /></div>
  </span>
</template>

<style scoped>
.popover { position: relative; display: inline-flex; }
.menu { top: calc(100% + 4px); left: 0; }
</style>

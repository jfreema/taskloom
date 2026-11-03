<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ name?: string | null; size?: number }>()
const palette = ['#7b68ee', '#e5484d', '#f5a623', '#3db88b', '#4194f6', '#d6409f', '#0c9ca6', '#8f6b43']
const initials = computed(() =>
  (props.name ?? '?').split(/[\s@.]+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join(''),
)
const color = computed(() => {
  let h = 0
  for (const c of props.name ?? '') h = (h * 31 + c.charCodeAt(0)) >>> 0
  return palette[h % palette.length]
})
</script>

<template>
  <span
    class="avatar"
    :title="name ?? ''"
    :style="{ background: color, width: `${size ?? 22}px`, height: `${size ?? 22}px`, fontSize: `${(size ?? 22) * 0.45}px` }"
  >{{ initials }}</span>
</template>

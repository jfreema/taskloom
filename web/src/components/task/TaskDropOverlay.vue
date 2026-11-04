<script setup lang="ts">
// "Drop files to upload" over the task detail while files are dragged over it (8.10). In the panel
// it covers the panel. On the page (`fixed`) the detail can be taller than the window, so it covers
// the visible part of the area that scrolls it (the main area), measured when the drag comes in.
import { onMounted, ref } from 'vue'
import { Upload } from '@lucide/vue'
import UiIcon from '../ui/UiIcon.vue'

const props = defineProps<{ fixed: boolean }>()

const el = ref<HTMLElement | null>(null)
const frame = ref<Record<string, string> | undefined>(undefined)

function scrollParent(node: HTMLElement): HTMLElement {
  for (let p = node.parentElement; p; p = p.parentElement) {
    if (/(auto|scroll|overlay)/.test(getComputedStyle(p).overflowY)) return p
  }
  return document.documentElement
}

onMounted(() => {
  if (!props.fixed || !el.value) return
  const r = scrollParent(el.value).getBoundingClientRect()
  const top = Math.max(r.top, 0)
  const left = Math.max(r.left, 0)
  const width = Math.min(r.right, window.innerWidth) - left
  const height = Math.min(r.bottom, window.innerHeight) - top
  frame.value = { top: `${top}px`, left: `${left}px`, width: `${width}px`, height: `${height}px`, right: 'auto', bottom: 'auto' }
})
</script>

<template>
  <div ref="el" class="drop-overlay" :class="{ fixed }" :style="frame" aria-hidden="true">
    <div class="drop-box">
      <UiIcon :icon="Upload" :size="20" />
      <span>Drop files to upload</span>
    </div>
  </div>
</template>

<style scoped>
.drop-overlay {
  position: absolute;
  inset: 0;
  z-index: var(--z-sticky);
  display: flex;
  padding: var(--space-2);
}

.drop-overlay.fixed {
  position: fixed;
}

.drop-box {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  border: 2px dashed var(--accent);
  border-radius: var(--radius-lg);
  background: color-mix(in srgb, var(--accent-subtle) 85%, transparent);
  color: var(--accent-text);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}

.drop-box > * {
  pointer-events: none;
}
</style>

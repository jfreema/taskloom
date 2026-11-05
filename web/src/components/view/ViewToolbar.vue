<script setup lang="ts">
// View toolbar (7.12, 8.4): the 44px row under the page header. `start` holds the search, the
// filter button and the filter chips (ListPage); `end` holds the view's own controls, which each
// view teleports in. It is one row that never wraps: when it is too narrow (phones, a docked
// panel) it scrolls sideways, with faded edges where more is hidden.
import { onBeforeUnmount, onMounted, ref } from 'vue'

const scroller = ref<HTMLElement | null>(null)
const fadeStart = ref(false)
const fadeEnd = ref(false)

function update(): void {
  const el = scroller.value
  if (!el) return
  const max = el.scrollWidth - el.clientWidth
  fadeStart.value = el.scrollLeft > 1
  fadeEnd.value = max - el.scrollLeft > 1
}

let resize: ResizeObserver | null = null
let mutations: MutationObserver | null = null
onMounted(() => {
  const el = scroller.value
  if (!el) return
  // The observer's first report comes after the first layout and before the first paint, so
  // mounting the toolbar forces no layout of its own.
  if (typeof ResizeObserver !== 'undefined') {
    resize = new ResizeObserver(update)
    resize.observe(el)
  } else {
    update()
  }
  // Views teleport their controls in after mount, and chips come and go with the filters.
  if (typeof MutationObserver !== 'undefined') {
    mutations = new MutationObserver(() => requestAnimationFrame(update))
    mutations.observe(el, { childList: true, subtree: true })
  }
})
onBeforeUnmount(() => {
  resize?.disconnect()
  mutations?.disconnect()
})

/** Keeps a control that gets keyboard focus fully in view. */
function onFocusIn(e: FocusEvent): void {
  const el = scroller.value
  const target = e.target
  if (!el || !(target instanceof HTMLElement)) return
  const box = el.getBoundingClientRect()
  const r = target.getBoundingClientRect()
  if (r.left < box.left + 24) el.scrollBy({ left: r.left - box.left - 24 })
  else if (r.right > box.right - 24) el.scrollBy({ left: r.right - box.right + 24 })
}
</script>

<template>
  <div class="view-toolbar">
    <div
      ref="scroller"
      class="scroller"
      :class="{ 'fade-start': fadeStart, 'fade-end': fadeEnd }"
      @scroll.passive="update"
      @focusin="onFocusIn"
    >
      <div class="start"><slot name="start" /></div>
      <div class="end"><slot name="end" /></div>
    </div>
  </div>
</template>

<style scoped>
.view-toolbar {
  flex-shrink: 0;
  min-width: 0;
  height: var(--toolbar-h);
  border-bottom: 1px solid var(--border-subtle);
}

.scroller {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: 100%;
  padding: 0 var(--space-4);
  overflow-x: auto;
  overflow-y: hidden;
  overscroll-behavior-x: contain;
  scrollbar-width: none;
  --fade-l: 0px;
  --fade-r: 0px;
  mask-image: linear-gradient(to right, transparent 0, black var(--fade-l), black calc(100% - var(--fade-r)), transparent 100%);
}

.scroller::-webkit-scrollbar {
  display: none;
}

.scroller.fade-start {
  --fade-l: var(--space-6);
}

.scroller.fade-end {
  --fade-r: var(--space-6);
}

.start,
.end {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-2);
}

.end {
  margin-left: auto;
}

.end:empty {
  display: none;
}

/* The end slot is ListPage's teleport target, a div with its own flex row. */
.end :deep(> div) {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

@media (max-width: 639px) {
  .scroller {
    gap: var(--space-1-5);
    padding: 0 var(--space-3);
  }
}
</style>

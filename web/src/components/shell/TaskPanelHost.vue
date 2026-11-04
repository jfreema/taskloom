<script setup lang="ts">
// The task panel container (spec 7.12, 8.10). A first version: it watches ?task= and renders
// TaskDetail docked beside the page from 1024px, and as a full-height overlay below that. It owns
// the focus root (tabindex -1, the `panel` shortcut scope), names the region by TaskDetail's
// hidden heading (titleId), and closes on Esc while focus is inside and no layer is open. The
// measured docked/modal/sheet modes and the other-workspace reroute come with the full version.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useLayerStack } from '../../composables/useLayerStack'
import { useTaskPanel } from '../../composables/useTaskPanel'
import TaskDetail from '../task/TaskDetail.vue'

const route = useRoute()
const panel = useTaskPanel()
const layers = useLayerStack()
const titleId = `${useId()}-task-title`
const root = ref<HTMLElement | null>(null)

const taskId = computed(() => {
  const q = route.query.task
  const v = Array.isArray(q) ? q[0] : q
  return typeof v === 'string' && v ? v : null
})

// Immediate, so a deep link (?task= on first load) also puts focus in the panel, where Esc and the
// panel shortcuts work.
watch(
  taskId,
  (id, was) => {
    if (id && id !== was) void nextTick(() => root.value?.focus({ preventScroll: true }))
    if (!id && was) {
      void nextTick(() => {
        const row = document.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(was)}"]`)
        ;(row ?? document.getElementById('main'))?.focus({ preventScroll: true })
      })
    }
  },
  { immediate: true },
)

function close(): void {
  panel.close()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing || layers.top.value) return
  const t = e.target
  if (t instanceof HTMLElement && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) return
  e.preventDefault()
  close()
}
</script>

<template>
  <aside v-if="taskId" class="task-panel" :aria-labelledby="titleId">
    <div ref="root" class="focus-root" tabindex="-1" data-task-panel-root @keydown="onKeydown">
      <TaskDetail :key="taskId" :task-id="taskId" mode="docked" :title-id="titleId" @close="close" />
    </div>
  </aside>
</template>

<style scoped>
.task-panel {
  display: flex;
  flex-direction: column;
  width: var(--panel-w);
  min-width: 0;
  border-left: 1px solid var(--border-subtle);
  background: var(--bg-surface);
}

.focus-root {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  outline: none;
}

@media (max-width: 1023px) {
  .task-panel {
    position: fixed;
    top: 0;
    right: 0;
    bottom: 0;
    z-index: var(--z-panel);
    width: min(640px, 100vw);
    border-left: 1px solid var(--border-floating);
    background: var(--bg-raised);
    box-shadow: var(--shadow-5);
  }
}
</style>

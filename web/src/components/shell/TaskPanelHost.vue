<script setup lang="ts">
// The task panel's container (7.12, 8.10). It watches ?task= (through useTaskPanel) and renders
// TaskDetail in one of three modes, measured, not by breakpoint:
// - docked: viewport >= 1024px and the content panel keeps >= 720px beside the panel
//   (contentWidth − panelWidth >= 720, a ResizeObserver on the content panel). An <aside> in the
//   content panel's grid; the page narrows over 240ms. No trap.
// - modal: otherwise from 640px: a right UiDrawer, min(640px, 100vw), with a scrim. Trapped.
// - sheet: below 640px: a right UiDrawer at 100vw (TaskDetail shows Back). Trapped; opening pushes
//   a history entry, so the browser's Back closes it (useTaskPanel).
// It owns the focus root (tabindex -1, data-task-panel-root: the `panel` shortcut scope) and
// passes `titleId` for aria-labelledby. Esc: in a text field it only leaves the field; otherwise
// it closes the panel (docked: the panel scope, or the global scope while focus is elsewhere;
// modal and sheet: the drawer's layer). A task of another workspace is rerouted to /t/:id, and a
// task pending delete closes the panel.
import { computed, inject, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../../api'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useLayerStack } from '../../composables/useLayerStack'
import { isTypingTarget, useShortcut } from '../../composables/useShortcut'
import { panelFocusRequest, useTaskPanel } from '../../composables/useTaskPanel'
import { useTasks } from '../../stores/tasks'
import { useWorkspaces } from '../../stores/workspaces'
import type { TaskDetail as TaskDetailData, Uuid } from '../../types'
import TaskDetail from '../task/TaskDetail.vue'
import UiDrawer from '../ui/UiDrawer.vue'
import { contentPanelKey } from './shellContext'

type Mode = 'docked' | 'modal' | 'sheet'

/** The page keeps at least this much beside a docked panel (8.10). */
const MIN_PAGE = 720
/** A click this recent counts as the reason a task opened (the docked mouse exception). */
const CLICK_MS = 1000

const panel = useTaskPanel()
const tasks = useTasks()
const workspaces = useWorkspaces()
const router = useRouter()
const layers = useLayerStack()
const { width: viewport, isPhone, isDesktop } = useBreakpoint()

const titleId = `${useId()}-task-title`
const taskId = panel.openId

// ---- Mode ----

const contentPanel = inject(contentPanelKey, ref<HTMLElement | null>(null))
const contentWidth = ref(0)
/** Desktop needs the content panel's width before it can choose; nothing renders until then. */
const measured = ref(false)
let observer: ResizeObserver | null = null

function measure(el: HTMLElement | null): void {
  observer?.disconnect()
  if (!el) return
  contentWidth.value = el.getBoundingClientRect().width
  measured.value = true
  if (typeof ResizeObserver === 'undefined') return
  observer ??= new ResizeObserver(([entry]) => {
    if (entry) contentWidth.value = entry.borderBoxSize?.[0]?.inlineSize ?? entry.target.getBoundingClientRect().width
  })
  observer.observe(el)
}

watch(contentPanel, measure, { immediate: true, flush: 'post' })
onBeforeUnmount(() => observer?.disconnect())

/** --panel-w: clamp(440px, 36vw, 680px). */
const panelWidth = computed(() => Math.min(680, Math.max(440, viewport.value * 0.36)))

// Measured once the shell has mounted (a post-flush watch), so a deep link never flashes the modal.
const mode = computed<Mode | null>(() => {
  if (isPhone.value) return 'sheet'
  if (!isDesktop.value) return 'modal'
  if (!measured.value) return null
  return contentWidth.value - panelWidth.value >= MIN_PAGE ? 'docked' : 'modal'
})

watch(mode, (m) => (panel.mode.value = m), { immediate: true })
onBeforeUnmount(() => (panel.mode.value = null))

const dockRoot = ref<HTMLElement | null>(null)
const drawerRoot = ref<HTMLElement | null>(null)
const root = computed(() => (mode.value === 'docked' ? dockRoot.value : drawerRoot.value))

/**
 * A mode change swaps containers at once; only opening and closing animate the dock. Focus that
 * was in the drawer follows the task into the dock (a modal or sheet focuses its root itself).
 */
const switchingMode = ref(false)
watch(mode, (m) => {
  if (!taskId.value) return
  switchingMode.value = true
  const hadFocus = !!drawerRoot.value?.contains(document.activeElement)
  void nextTick(() => {
    requestAnimationFrame(() => (switchingMode.value = false))
    const a = document.activeElement
    const lost = !a || a === document.body || !a.isConnected
    if (m === 'docked' && hadFocus && lost) dockRoot.value?.focus({ preventScroll: true })
  })
})

// ---- Focus ----

/** The last input: a task opened by a mouse click in the page while docked keeps focus there. */
let lastInput: { kind: 'pointer' | 'key'; at: number; inPage: boolean } | null = null

function onPointerDown(e: PointerEvent): void {
  const t = e.target instanceof Element ? e.target : null
  lastInput = { kind: 'pointer', at: performance.now(), inPage: !!t?.closest('#main') }
}

function onKeyDown(): void {
  lastInput = { kind: 'key', at: performance.now(), inPage: false }
}

onMounted(() => {
  document.addEventListener('pointerdown', onPointerDown, true)
  document.addEventListener('keydown', onKeyDown, true)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onPointerDown, true)
  document.removeEventListener('keydown', onKeyDown, true)
})

/** The task whose panel root should take focus once it is rendered. */
const focusWanted = ref<Uuid | null>(null)

function focusRoot(): void {
  root.value?.focus({ preventScroll: true })
}

// Opening from nothing (a click, a link, Forward) always focuses the panel.
watch(
  taskId,
  (id, prev) => {
    if (id && !prev) focusWanted.value = id
  },
  { immediate: true },
)

// Every open() asks for focus, except one: docked and already open, a mouse click on another row
// in the page keeps focus on that row (8.10). J and K never ask.
watch(panelFocusRequest, (req) => {
  if (!req) return
  const click = lastInput?.kind === 'pointer' && lastInput.inPage && performance.now() - lastInput.at < CLICK_MS
  if (mode.value === 'docked' && taskId.value && click) return
  focusWanted.value = req.id
})

watch(
  [taskId, root, focusWanted],
  ([id, el, wanted]) => {
    if (!id || !el || wanted !== id) return
    focusWanted.value = null
    void nextTick(() => el.focus({ preventScroll: true }))
  },
  { immediate: true, flush: 'post' },
)

// Switching tasks: a short cross-fade, and focus stays in the panel if its element went away.
watch(taskId, (id, prev) => {
  if (!id || !prev || id === prev) return
  const el = root.value
  const hadFocus = !!el && el.contains(document.activeElement)
  void nextTick(() => {
    const now = root.value
    if (!now) return
    if (hadFocus && (!document.activeElement || document.activeElement === document.body)) focusRoot()
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    now.animate?.([{ opacity: 0.4 }, { opacity: 1 }], { duration: reduce ? 80 : 120, easing: 'ease-out' })
  })
})

const FOCUSABLE = 'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]'

/**
 * Focuses the task's row: the element itself when it can take focus (a treegrid row has
 * tabindex), else the first focusable element inside it (a plain <li> around a title button).
 * The row scrolls into view only when it is out of sight.
 */
function focusRow(row: HTMLElement): boolean {
  const target = row.matches(FOCUSABLE) ? row : row.querySelector<HTMLElement>(FOCUSABLE)
  if (!target) return false
  target.focus({ preventScroll: true })
  if (document.activeElement !== target) return false
  target.scrollIntoView?.({ block: 'nearest' })
  return true
}

// After the panel closes, focus returns to the task's row, else to #main (8.10). useTaskPanel's
// close() does this too; this covers the browser's Back, rows that can't take focus themselves and
// other ways the query can change.
watch(taskId, (id, prev) => {
  if (id || !prev) return
  void nextTick(() => {
    const a = document.activeElement
    const lost = !a || a === document.body || !a.isConnected || !!a.closest('[data-task-panel-root]')
    if (!lost) return
    const row = document.querySelector<HTMLElement>(`[data-task-row="${CSS.escape(prev)}"]`)
    if (row && focusRow(row)) return
    document.getElementById('main')?.focus({ preventScroll: true })
  })
})

// ---- Closing ----

function close(): void {
  panel.close()
}

function onDrawerOpen(open: boolean): void {
  if (!open && taskId.value) close()
}

/** Esc in a text field leaves the field (focus goes to the panel root); the next Esc closes. */
function onRootKeydown(e: KeyboardEvent): void {
  if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing) return
  if (!isTypingTarget(e.target)) return
  e.preventDefault()
  focusRoot()
}

useShortcut('Escape', () => close(), { scope: 'panel', el: dockRoot, when: () => mode.value === 'docked' && !!taskId.value })
useShortcut(
  'Escape',
  () => {
    if (mode.value !== 'docked' || !taskId.value || layers.top.value) return false
    close()
  },
  { scope: 'global' },
)

// A task pending delete (Mod+Backspace, the row menu) leaves the panel (8.10).
watch(
  () => (taskId.value ? tasks.isPendingDelete(taskId.value) : false),
  (pending) => {
    if (pending) close()
  },
)

// ---- Another workspace ----

// The task store only holds the current workspace's tasks. For a task it doesn't have, look the
// task up; if it belongs to another workspace, the full page switches workspace and shows it.
watch(
  taskId,
  async (id) => {
    if (!id || tasks.get(id)) return
    try {
      const d = await api.get<TaskDetailData>(`/api/tasks/${id}`)
      if (taskId.value !== id || !workspaces.currentId || d.workspaceId === workspaces.currentId) return
      await router.replace(`/t/${id}`)
    } catch {
      // Missing or unreachable: TaskDetail shows its own state.
    }
  },
  { immediate: true },
)

const drawerWidth = computed(() => (mode.value === 'sheet' ? '100vw' : undefined))
</script>

<template>
  <Transition :name="switchingMode ? 'tp-none' : 'tp-dock'">
    <div v-if="taskId && mode === 'docked'" class="dock">
      <aside class="task-panel" :aria-labelledby="titleId">
        <div ref="dockRoot" class="focus-root" tabindex="-1" data-task-panel-root @keydown="onRootKeydown">
          <TaskDetail :task-id="taskId" mode="docked" :title-id="titleId" @close="close" />
        </div>
      </aside>
    </div>
  </Transition>
  <UiDrawer
    v-if="mode === 'modal' || mode === 'sheet'"
    :open="!!taskId"
    side="right"
    :width="drawerWidth"
    :aria-labelledby="titleId"
    initial-focus="[data-task-panel-root]"
    @update:open="onDrawerOpen"
  >
    <div ref="drawerRoot" class="focus-root" :class="`mode-${mode}`" tabindex="-1" data-task-panel-root @keydown="onRootKeydown">
      <TaskDetail v-if="taskId" :task-id="taskId" :mode="mode" :title-id="titleId" @close="close" />
    </div>
  </UiDrawer>
</template>

<style scoped>
/* Docked: the content panel's second grid column. Only the column's width animates; the panel
   inside keeps its width, so its content slides in instead of reflowing. */
.dock {
  width: var(--panel-w);
  min-height: 0;
  overflow: hidden;
  border-left: 1px solid var(--border-subtle);
}

.task-panel {
  display: flex;
  flex-direction: column;
  width: var(--panel-w);
  height: 100%;
  background: var(--bg-surface);
}

.focus-root {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  outline: none;
  overscroll-behavior: contain;
}

.tp-dock-enter-active,
.tp-dock-leave-active {
  transition: width var(--dur-4) var(--ease-in-out);
}

.tp-dock-enter-from,
.tp-dock-leave-to {
  width: 0;
}
</style>

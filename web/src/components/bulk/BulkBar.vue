<script setup lang="ts">
// The bulk bar (8.7), rendered by the List view as <BulkBar /> with no props: it reads the selection
// and the list page context. Shown while tasks of this list are selected; never on phones.
// - A toolbar "Bulk actions" on --bg-inverse, fixed at the bottom centre of the page, 16px up, 48px
//   high: "N selected", then Status, Assignees, Due date, Priority, Tags, Move and Delete (ghost
//   buttons in the inverse tone), then "Clear selection" {Esc}. Arrow keys move between them.
// - Below 720px of page width the buttons show only their icons; their labels stay their names.
// - Each edit is one bulk update with one undo toast (useBulkActions). Move opens "Move N tasks";
//   Delete hides the tasks with a delayed delete and "Deleted N tasks" with Undo. Both take their
//   tasks out of the selection, so the bar closes; focus then returns to the list.
// - Where the toasts (bottom left) would cover it, the bar rises above them while they show.
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { FolderInput, Trash2, X } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useListPageContext } from '../../composables/useListPageContext'
import { useRovingFocus } from '../../composables/useRovingFocus'
import { useSelection } from '../../stores/selection'
import UiIconButton from '../ui/UiIconButton.vue'
import BulkAssigneeMenu from './BulkAssigneeMenu.vue'
import BulkButton from './BulkButton.vue'
import BulkDueDate from './BulkDueDate.vue'
import BulkPriorityMenu from './BulkPriorityMenu.vue'
import BulkStatusMenu from './BulkStatusMenu.vue'
import BulkTagMenu from './BulkTagMenu.vue'
import { useBulkActions } from './useBulkActions'

const ICON_ONLY_BELOW = 720
const EDGE = 16
const GAP = 8

const ctx = useListPageContext()
const selection = useSelection()
const actions = useBulkActions()
const { isPhone } = useBreakpoint()

const root = ref<HTMLElement | null>(null)
const bar = ref<HTMLElement | null>(null)
const toolbar = ref<HTMLElement | null>(null)

const count = computed(() => actions.selected.value.length)
const visible = computed(
  () => !isPhone.value && ctx.canEdit.value && selection.listId === ctx.listId.value && count.value > 0,
)

useRovingFocus(toolbar, { itemSelector: '[data-bulk-item]', orientation: 'horizontal' })

// ---- The page box and the toasts ----
// The bar is fixed to the viewport but centred on the page (#main, beside the sidebar and a docked
// task panel), 16px above its bottom edge.

const pageWidth = ref(Infinity)
const iconOnly = computed(() => pageWidth.value < ICON_ONLY_BELOW)
const box = ref<{ left: number; width: number; bottom: number } | null>(null)
const lift = ref(0)

let pageObserver: ResizeObserver | null = null
let toastObserver: ResizeObserver | null = null

function mainEl(): HTMLElement | null {
  return root.value?.closest<HTMLElement>('#main') ?? null
}

function toastHost(): HTMLElement | null {
  return document.querySelector<HTMLElement>('section[aria-label="Notifications"]')
}

/** How far up the bar goes so the toasts don't cover it (0 when they don't meet). */
function measureLift(): void {
  const el = toolbar.value
  const host = toastHost()
  if (!el || !host || !host.querySelector('[role="status"], [role="alert"]')) {
    lift.value = 0
    return
  }
  const b = el.getBoundingClientRect()
  const h = host.getBoundingClientRect()
  const meets = h.height > 0 && b.left < h.right + GAP && b.right > h.left - GAP
  const bottom = box.value?.bottom ?? EDGE
  lift.value = meets ? Math.max(0, Math.round(window.innerHeight - h.top + GAP - bottom)) : 0
}

function measurePage(): void {
  const main = mainEl()
  if (main) {
    const r = main.getBoundingClientRect()
    pageWidth.value = main.clientWidth
    box.value = { left: r.left, width: r.width, bottom: Math.max(0, window.innerHeight - r.bottom) + EDGE }
  } else {
    pageWidth.value = Infinity
    box.value = null
  }
  measureLift()
}

function stop(): void {
  pageObserver?.disconnect()
  toastObserver?.disconnect()
  pageObserver = null
  toastObserver = null
  window.removeEventListener('resize', measurePage)
}

watch(
  visible,
  (on) => {
    stop()
    if (!on) {
      lift.value = 0
      return
    }
    window.addEventListener('resize', measurePage, { passive: true })
    // The observer's first report measures the page after the frame's own layout, before its paint:
    // measuring here would force a layout of the whole list in the task that selected (Mod+A).
    const main = mainEl()
    if (typeof ResizeObserver === 'undefined' || !main) {
      measurePage()
      return
    }
    pageObserver = new ResizeObserver(measurePage)
    pageObserver.observe(main)
    const host = toastHost()
    toastObserver = new ResizeObserver(measureLift)
    if (host) toastObserver.observe(host)
  },
  { flush: 'post', immediate: true },
)

onBeforeUnmount(stop)

// The bar's own width changes with its labels; the lift follows.
watch([iconOnly, count], () => requestAnimationFrame(measureLift), { flush: 'post' })

const wrapStyle = computed(() => {
  const b = box.value
  return {
    ...(b ? { left: `${b.left}px`, width: `${b.width}px`, bottom: `${b.bottom}px` } : {}),
    ...(lift.value ? { transform: `translateY(-${lift.value}px)` } : {}),
  }
})

// ---- Focus ----

let focusInside = false

function onFocusIn(): void {
  focusInside = true
}

function onFocusOut(e: FocusEvent): void {
  if (!(e.relatedTarget instanceof Node) || !bar.value?.contains(e.relatedTarget)) focusInside = false
}

/** The List's tab stop row (else its first row), else the page. */
function focusList(): void {
  const main = root.value?.closest<HTMLElement>('#main')
  const grid = main?.querySelector<HTMLElement>('[role="treegrid"]')
  const target =
    grid?.querySelector<HTMLElement>('[role="row"][data-task-row][tabindex="0"]') ??
    grid?.querySelector<HTMLElement>('[role="row"][data-task-row]') ??
    main
  target?.focus({ preventScroll: true })
}

// Move, Delete and Clear selection close the bar under the focus: directly, or once the Move dialog
// it opened hands focus back to a button that is fading out. Focus then goes back to the list. Focus
// that left the bar for somewhere else (the list, another field) stays where it is.
watch(visible, (on, was) => {
  if (on || !was) return
  const fromBar = focusInside
  focusInside = false
  const start = performance.now()
  const check = () => {
    const active = document.activeElement
    const lost = !active || active === document.body || !active.isConnected || !!root.value?.contains(active)
    const elapsed = performance.now() - start
    if (lost && (fromBar || elapsed > 100)) {
      focusList()
      return
    }
    if (elapsed < 1500 && (lost || active?.closest('[role="dialog"], [role="menu"]'))) requestAnimationFrame(check)
  }
  requestAnimationFrame(check)
})

function clear(): void {
  selection.clear()
}

</script>

<template>
  <div ref="root" class="bulk-root">
    <Transition name="bulk">
      <div v-if="visible" ref="bar" class="bulk-bar-wrap" :style="wrapStyle">
        <div
          ref="toolbar"
          class="bulk-bar surface-inverse"
          :class="{ 'is-icon-only': iconOnly }"
          role="toolbar"
          aria-label="Bulk actions"
          aria-orientation="horizontal"
          @focusin="onFocusIn"
          @focusout="onFocusOut"
        >
          <span class="count tabular">{{ count }} selected</span>
          <span class="divider" aria-hidden="true" />
          <BulkStatusMenu :actions="actions" :list-id="ctx.listId.value" :icon-only="iconOnly" />
          <BulkAssigneeMenu :actions="actions" :icon-only="iconOnly" />
          <BulkDueDate :actions="actions" :icon-only="iconOnly" />
          <BulkPriorityMenu :actions="actions" :icon-only="iconOnly" />
          <BulkTagMenu :actions="actions" :icon-only="iconOnly" />
          <BulkButton :icon="FolderInput" label="Move" :icon-only="iconOnly" @click="actions.move()" />
          <BulkButton :icon="Trash2" label="Delete" :icon-only="iconOnly" danger @click="actions.remove()" />
          <span class="divider" aria-hidden="true" />
          <UiIconButton :icon="X" label="Clear selection" kbd="Esc" tone="inverse" size="sm" data-bulk-item @click="clear" />
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.bulk-root {
  height: 0;
}

/* A strip as wide as the page (set from #main), holding the bar in its centre. Only the bar takes
   pointer events, so the rows beside it stay clickable. */
.bulk-bar-wrap {
  position: fixed;
  bottom: 16px;
  left: 0;
  z-index: var(--z-bulkbar);
  display: flex;
  justify-content: center;
  width: 100%;
  pointer-events: none;
}

.bulk-bar {
  display: flex;
  align-items: center;
  gap: var(--space-0-5);
  height: 48px;
  min-width: 0;
  max-width: calc(100% - var(--space-8));
  pointer-events: auto;
  padding: 0 var(--space-2) 0 var(--space-4);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-4);
  overflow-x: auto;
  scrollbar-width: none;
}

.count {
  flex: none;
  margin-right: var(--space-1);
  color: var(--text-inverse);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  white-space: nowrap;
}

.divider {
  flex: none;
  width: 1px;
  height: 20px;
  margin: 0 var(--space-1-5);
  background: color-mix(in srgb, var(--text-inverse) 22%, transparent);
}

.bulk-bar > :deep(*) {
  flex: none;
}

.bulk-enter-active {
  transition: opacity var(--dur-3) var(--ease-out);
}

.bulk-leave-active {
  transition: opacity var(--dur-2) var(--ease-in);
  pointer-events: none;
}

.bulk-enter-from,
.bulk-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .bulk-bar-wrap {
    transition: transform var(--dur-3) var(--ease-in-out);
  }

  .bulk-enter-active .bulk-bar {
    transition: transform var(--dur-3) var(--ease-out);
  }

  .bulk-leave-active .bulk-bar {
    transition: transform var(--dur-2) var(--ease-in);
  }

  .bulk-enter-from .bulk-bar,
  .bulk-leave-to .bulk-bar {
    transform: translateY(8px);
  }
}
</style>

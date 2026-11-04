// Drag and drop (9.3): the only module that imports the @atlaskit packages (7.13). Pragmatic drag and
// drop 4 runs on native drag events: mouse and pen everywhere, touch where the browser supports
// native drag. Dragging is off below 640px and for guests. Esc cancels a drag (the browser does it).
//
// Drop targets: `over` is true while the drag is over the target or a target nested in it; `edge` is
// the closest allowed edge, set only while this target is the innermost one (a column body under a
// card shows no indicator). onDrop runs on the innermost target only. Targets with `edges` are
// sticky, so the indicator doesn't flicker across the gaps between rows and cards.
import { draggable, dropTargetForElements, monitorForElements } from '@atlaskit/pragmatic-drag-and-drop/element/adapter'
import { pointerOutsideOfPreview } from '@atlaskit/pragmatic-drag-and-drop/element/pointer-outside-of-preview'
import { setCustomNativeDragPreview } from '@atlaskit/pragmatic-drag-and-drop/element/set-custom-native-drag-preview'
import { autoScrollForElements } from '@atlaskit/pragmatic-drag-and-drop-auto-scroll/element'
import { attachClosestEdge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/closest-edge/attach-closest-edge'
import { extractClosestEdge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/closest-edge/extract-closest-edge'
import type { Edge } from '@atlaskit/pragmatic-drag-and-drop-hitbox/types'
import { computed, ref, shallowRef, watch, type Ref } from 'vue'
import { useWorkspaces } from '../stores/workspaces'
import type { IsoDate, Uuid } from '../types'
import { useBreakpoint } from './useBreakpoint'

export type { Edge }

export type DragData = { kind: 'task'; id: Uuid; listId: Uuid; fromStatusId?: Uuid; fromDate?: IsoDate | null }

export interface DraggableOptions {
  data: () => DragData
  /** Only this element starts the drag (the List row grip). Without it, the whole element does, except its controls. */
  handle?: Ref<HTMLElement | null>
  disabled?: () => boolean
  /** 'lift' renders a tilted copy with --shadow-4 as the drag image (7.8, Board cards only). Default 'native'. */
  preview?: 'native' | 'lift'
}

export interface DropTargetOptions {
  data: () => Record<string, unknown>
  accepts: (d: DragData) => boolean
  edges?: Edge[]
  onDrop: (d: DragData, edge: Edge | null) => void
}

/** Drags that start on these (inside a draggable without a handle) are the control's, not a drag. */
const CONTROL_SELECTOR = [
  'button',
  'a[href]',
  'input',
  'textarea',
  'select',
  'label',
  '[contenteditable]:not([contenteditable="false"])',
  '[role="button"]',
  '[role="checkbox"]',
  '[role="combobox"]',
  '[role="menuitem"]',
  '[role="switch"]',
  '[role="textbox"]',
  '[data-no-drag]',
].join(', ')

/** Room around the lifted card for its rotation and --shadow-4 (0 16px 40px -8px). */
const LIFT_PADDING = '8px 16px 32px'

const active = shallowRef<DragData | null>(null)
/** The task being dragged (any of our drags), or null. */
export const activeDrag: Readonly<Ref<DragData | null>> = active

/** The source data as DragData, or null for anything that isn't one of our task drags. */
function toDragData(data: Record<string, unknown>): DragData | null {
  if (data.kind !== 'task' || typeof data.id !== 'string' || typeof data.listId !== 'string') return null
  const d: DragData = { kind: 'task', id: data.id, listId: data.listId }
  if (typeof data.fromStatusId === 'string') d.fromStatusId = data.fromStatusId
  if (typeof data.fromDate === 'string' || data.fromDate === null) d.fromDate = data.fromDate
  return d
}

let monitoring = false

function ensureMonitor(): void {
  if (monitoring || typeof window === 'undefined') return
  monitoring = true
  monitorForElements({
    canMonitor: ({ source }) => toDragData(source.data) !== null,
    onDragStart: ({ source }) => {
      active.value = toDragData(source.data)
    },
    onDrop: () => {
      active.value = null
    },
  })
}

function startsOnControl(element: HTMLElement, target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false
  const control = target.closest(CONTROL_SELECTOR)
  return !!control && control !== element && element.contains(control)
}

function liftPreview(element: HTMLElement, nativeSetDragImage: DataTransfer['setDragImage'] | null): void {
  const rect = element.getBoundingClientRect()
  setCustomNativeDragPreview({
    nativeSetDragImage,
    getOffset: pointerOutsideOfPreview({ x: '16px', y: '8px' }),
    render({ container }) {
      const clone = element.cloneNode(true) as HTMLElement
      clone.removeAttribute('id')
      clone.querySelectorAll('[id]').forEach((n) => n.removeAttribute('id'))
      clone.setAttribute('aria-hidden', 'true')
      clone.inert = true
      Object.assign(clone.style, {
        boxSizing: 'border-box',
        width: `${rect.width}px`,
        height: `${rect.height}px`,
        margin: '0',
        background: 'var(--bg-raised)',
        border: '1px solid var(--border-floating)',
        borderRadius: 'var(--radius-md)',
        boxShadow: 'var(--shadow-4)',
        transform: 'rotate(1.5deg) scale(1.02)',
      })
      container.style.padding = LIFT_PADDING
      container.appendChild(clone)
      return () => clone.remove()
    },
  })
}

/** Members and above, at 640px and wider (9.3). */
function useDragAllowed(): Ref<boolean> {
  const workspaces = useWorkspaces()
  const { isPhone } = useBreakpoint()
  return computed(() => !isPhone.value && workspaces.canEdit())
}

/**
 * Makes `el` draggable while it is mounted and dragging is allowed. `dragging` is true from the
 * drag's start (after the preview is taken) to its end, for the source's placeholder.
 */
export function useDraggable(el: Ref<HTMLElement | null>, o: DraggableOptions): { dragging: Ref<boolean> } {
  ensureMonitor()
  const allowed = useDragAllowed()
  const dragging = ref(false)
  const enabled = computed(() => allowed.value && !(o.disabled?.() ?? false))
  const handle = computed(() => o.handle?.value ?? null)
  let pressed: EventTarget | null = null
  const onPointerDown = (e: PointerEvent) => {
    pressed = e.target
  }

  watch(
    [el, handle, enabled],
    ([element, dragHandle, on], _old, onCleanup) => {
      dragging.value = false
      if (!element || !on) return
      element.addEventListener('pointerdown', onPointerDown, true)
      const cleanup = draggable({
        element,
        ...(dragHandle ? { dragHandle } : {}),
        // disabled() is asked again here, in case it reads state that isn't reactive.
        canDrag: () => allowed.value && !(o.disabled?.() ?? false) && (dragHandle !== null || !startsOnControl(element, pressed)),
        getInitialData: () => ({ ...o.data() }),
        onGenerateDragPreview: ({ nativeSetDragImage }) => {
          if (o.preview === 'lift') liftPreview(element, nativeSetDragImage)
        },
        onDragStart: () => {
          dragging.value = true
        },
        onDrop: () => {
          dragging.value = false
          pressed = null
        },
      })
      onCleanup(() => {
        cleanup()
        element.removeEventListener('pointerdown', onPointerDown, true)
        dragging.value = false
      })
    },
    { immediate: true, flush: 'post' },
  )

  return { dragging }
}

/** Makes `el` a drop target for task drags that `accepts` allows. */
export function useDropTarget(el: Ref<HTMLElement | null>, o: DropTargetOptions): { over: Ref<boolean>; edge: Ref<Edge | null> } {
  ensureMonitor()
  const over = ref(false)
  const edge = ref<Edge | null>(null)
  const reset = () => {
    over.value = false
    edge.value = null
  }

  watch(
    el,
    (element, _old, onCleanup) => {
      reset()
      if (!element) return
      const track = (self: { data: Record<string | symbol, unknown> }, targets: ReadonlyArray<{ element: Element }>) => {
        const inside = targets.some((t) => t.element === element)
        over.value = inside
        edge.value = inside && targets[0]?.element === element ? extractClosestEdge(self.data) : null
      }
      const cleanup = dropTargetForElements({
        element,
        canDrop: ({ source }) => {
          const d = toDragData(source.data)
          return d !== null && o.accepts(d)
        },
        getData: ({ input, element: target }) => {
          const data = o.data()
          return o.edges?.length ? attachClosestEdge(data, { element: target, input, allowedEdges: o.edges }) : data
        },
        getIsSticky: () => !!o.edges?.length,
        onDragEnter: ({ self, location }) => track(self, location.current.dropTargets),
        onDrag: ({ self, location }) => track(self, location.current.dropTargets),
        onDropTargetChange: ({ self, location }) => track(self, location.current.dropTargets),
        onDragLeave: reset,
        onDrop: ({ self, source, location }) => {
          reset()
          const d = toDragData(source.data)
          if (!d || location.current.dropTargets[0]?.element !== element) return
          o.onDrop(d, extractClosestEdge(self.data))
        },
      })
      onCleanup(() => {
        cleanup()
        reset()
      })
    },
    { immediate: true, flush: 'post' },
  )

  return { over, edge }
}

/** Scrolls `el` near its edges while a task drag is over it. */
export function useAutoScroll(el: Ref<HTMLElement | null>): void {
  watch(
    el,
    (element, _old, onCleanup) => {
      if (!element) return
      onCleanup(autoScrollForElements({ element, canScroll: ({ source }) => toDragData(source.data) !== null }))
    },
    { immediate: true, flush: 'post' },
  )
}

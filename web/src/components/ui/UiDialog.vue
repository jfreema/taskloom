<script setup lang="ts">
// Modal dialog (spec 7.10). A modal layer (7.7): scrim, focus trap, inert #app, scroll lock and
// focus return. The title is an h2 that labels the dialog. On phones it is a bottom sheet (the
// panel is marked data-sheet, so toasts rise above it).
// No native <dialog>: its top layer would hide toasts and pickers.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { X } from '@lucide/vue'
import { useLayer } from '../../composables/useLayerStack'
import { focusFirst, tabbables, useFocusTrap } from '../../composables/useFocusTrap'
import UiIconButton from './UiIconButton.vue'

export type DialogCloseReason = 'escape' | 'scrim' | 'button' | 'programmatic'

const open = defineModel<boolean>('open', { required: true })

const props = withDefaults(
  defineProps<{
    title: string
    description?: string
    size?: 'sm' | 'md' | 'lg'
    /** Esc, scrim click and the close button. */
    dismissible?: boolean
    /** CSS selector; default is the first focusable element in the body. */
    initialFocus?: string
    role?: 'dialog' | 'alertdialog'
    /** 'top' = 12vh from the top (the palette). */
    position?: 'center' | 'top'
    /** The title becomes visually hidden. */
    hideHeader?: boolean
    /** false hides the header's close button while Esc and the scrim still dismiss (confirms). */
    closeButton?: boolean
  }>(),
  {
    description: undefined,
    size: 'md',
    dismissible: true,
    initialFocus: undefined,
    role: 'dialog',
    position: 'center',
    hideHeader: false,
    closeButton: true,
  },
)

const emit = defineEmits<{ close: [reason: DialogCloseReason]; 'after-leave': [] }>()

const id = useId()
const titleId = `${id}-title`
const descId = `${id}-desc`
const root = ref<HTMLElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const body = ref<HTMLElement | null>(null)
const footer = ref<HTMLElement | null>(null)
let pendingReason: DialogCloseReason | null = null
let downOnScrim = false

function requestClose(reason: DialogCloseReason): void {
  if (reason !== 'programmatic' && !props.dismissible) return
  if (!open.value) return
  pendingReason = reason
  open.value = false
}

const layer = useLayer(() => open.value, {
  modal: true,
  el: () => root.value,
  close: (reason) => {
    if (reason === 'escape') requestClose('escape')
  },
})
useFocusTrap(panel, open)

// Layers stack in the order they opened, whatever their order in #overlay-root: a confirm opened
// from a dialog (or over a popover) sits above it. The z-index stays put while the dialog fades out.
const rootStyle = computed(() => ({ zIndex: layer.z.value }))

function placeInitialFocus(): void {
  const p = panel.value
  if (!p) return
  if (props.initialFocus && p.querySelector(props.initialFocus)) return focusFirst(p, props.initialFocus)
  if (body.value && tabbables(body.value).length) return focusFirst(body.value)
  if (footer.value && tabbables(footer.value).length) return focusFirst(footer.value)
  p.focus({ preventScroll: true })
}

watch(
  open,
  (on, was) => {
    if (on) {
      void nextTick(placeInitialFocus)
    } else if (was) {
      emit('close', pendingReason ?? 'programmatic')
      pendingReason = null
    }
  },
  { immediate: true, flush: 'post' },
)

function onScrimDown(e: PointerEvent): void {
  downOnScrim = e.target === e.currentTarget
}

function onScrimClick(e: MouseEvent): void {
  if (downOnScrim && e.target === e.currentTarget) requestClose('scrim')
  downOnScrim = false
}
</script>

<template>
  <Teleport to="#overlay-root">
    <Transition name="ui-dialog" :duration="{ enter: 180, leave: 130 }" @after-leave="emit('after-leave')">
      <div v-if="open" ref="root" class="ui-dialog-root" :class="`pos-${position}`" :style="rootStyle">
        <div class="scrim" aria-hidden="true" @pointerdown="onScrimDown" @click="onScrimClick" />
        <div
          ref="panel"
          class="panel"
          :class="`size-${size}`"
          :role="role"
          aria-modal="true"
          :aria-labelledby="titleId"
          :aria-describedby="description ? descId : undefined"
          tabindex="-1"
          data-sheet
        >
          <header class="header" :class="{ 'sr-only': hideHeader }">
            <div class="heading">
              <h2 :id="titleId" class="title">{{ title }}</h2>
              <p v-if="description" :id="descId" class="description">{{ description }}</p>
            </div>
            <div v-if="!hideHeader && ($slots['header-actions'] || (dismissible && closeButton))" class="header-actions">
              <slot name="header-actions" />
              <UiIconButton
                v-if="dismissible && closeButton"
                :icon="X"
                label="Close"
                kbd="Escape"
                @click="requestClose('button')"
              />
            </div>
          </header>
          <div v-if="$slots.default" ref="body" class="body" :class="{ 'no-header': hideHeader }"><slot /></div>
          <footer v-if="$slots.footer" ref="footer" class="footer"><slot name="footer" /></footer>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.ui-dialog-root {
  position: fixed;
  inset: 0;
  z-index: var(--z-modal);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--space-4);
}

.ui-dialog-root.pos-top {
  align-items: flex-start;
  padding-top: 12vh;
}

.scrim {
  position: absolute;
  inset: 0;
  background: var(--bg-scrim);
}

.panel {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  max-width: var(--modal-md);
  max-height: calc(100dvh - 2 * var(--space-4));
  background: var(--bg-raised);
  border: 1px solid var(--border-floating);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-5);
  color: var(--text-primary);
  overflow: hidden;
  outline: none;
}

.pos-top .panel {
  max-height: calc(100dvh - 12vh - var(--space-4));
}

.size-sm {
  max-width: var(--modal-sm);
}

.size-lg {
  max-width: var(--modal-lg);
}

.header {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
  padding: var(--space-5) var(--space-6);
  flex-shrink: 0;
}

.heading {
  flex: 1;
  min-width: 0;
}

.title {
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
  overflow-wrap: anywhere;
}

.description {
  margin-top: var(--space-1);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-secondary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  margin: -2px calc(var(--space-2) * -1) -2px 0;
}

.body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0 var(--space-6) var(--space-5);
}

.body.no-header {
  padding-top: var(--space-5);
}

.footer {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: var(--space-2);
  padding: var(--space-4) var(--space-6);
  flex-shrink: 0;
}

/* Fade, and scale .97 → 1 when motion is welcome (7.8). */
.ui-dialog-enter-active .scrim,
.ui-dialog-enter-active .panel {
  transition: opacity var(--dur-3) var(--ease-out), transform var(--dur-3) var(--ease-out);
}

.ui-dialog-leave-active .scrim,
.ui-dialog-leave-active .panel {
  transition: opacity calc(var(--dur-3) * 0.7) var(--ease-in), transform calc(var(--dur-3) * 0.7) var(--ease-in);
}

.ui-dialog-enter-from .scrim,
.ui-dialog-leave-to .scrim,
.ui-dialog-enter-from .panel,
.ui-dialog-leave-to .panel {
  opacity: 0;
}

@media (prefers-reduced-motion: no-preference) {
  .ui-dialog-enter-from .panel,
  .ui-dialog-leave-to .panel {
    transform: scale(0.97);
  }
}

/* Phones: a bottom sheet, full width, radius 16 at the top, max 92dvh, sticky footer. */
@media (max-width: 639px) {
  .ui-dialog-root,
  .ui-dialog-root.pos-top {
    align-items: flex-end;
    padding: 0;
  }

  .panel,
  .pos-top .panel {
    max-width: none;
    max-height: 92dvh;
    border-bottom: 0;
    border-radius: var(--radius-2xl) var(--radius-2xl) 0 0;
  }

  .header {
    padding: var(--space-4);
  }

  .body {
    padding: 0 var(--space-4) var(--space-4);
  }

  .footer {
    padding: var(--space-3) var(--space-4) calc(var(--space-3) + env(safe-area-inset-bottom, 0px));
    border-top: 1px solid var(--border-subtle);
  }

  .footer :deep(.ui-button) {
    flex: 1;
  }
}

@media (max-width: 639px) and (prefers-reduced-motion: no-preference) {
  .ui-dialog-enter-from .panel,
  .ui-dialog-leave-to .panel {
    transform: translateY(24px);
  }
}
</style>

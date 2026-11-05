<script setup lang="ts">
// The list search (7.12, 8.4): a searchbox named "Search this list", 200px wide and 280px while
// focused. It filters on titles, client-side; typing reaches the view after 100ms (Enter sends at
// once). "/" focuses it: the shell's "/" shortcut looks for the search box in #main. Esc clears
// the text first, then a second Esc leaves the box for the page. ArrowDown, and Enter with some
// text, send the text at once and move focus to the first row (or card, or the Calendar's first day
// with a task) in view. On phones the box shows only while it holds text (the app bar's Search
// opens the palette instead).
// Shrinking back when focus leaves would slide the controls to its right under the pointer: a
// press on the Filter button moves focus on pointerdown, so the box stays wide until the press ends
// and its click has landed.
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Search } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useDebouncedFn } from '../../composables/useDebouncedFn'
import UiInput from '../ui/UiInput.vue'

const props = withDefaults(defineProps<{ modelValue: string; placeholder?: string }>(), { placeholder: 'Search this list' })

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const { isPhone } = useBreakpoint()
const text = ref(props.modelValue)
const root = ref<HTMLElement | null>(null)
const wide = ref(false)
let pressing = false
let collapsePending = false

const send = useDebouncedFn((v: string) => {
  if (v !== props.modelValue) emit('update:modelValue', v)
}, 100)

watch(text, (v) => send(v))

// A change from outside (the list changed, "Clear filters") replaces the text.
watch(
  () => props.modelValue,
  (v) => {
    if (v === text.value) return
    send.cancel()
    text.value = v
  },
)

function onEscape(e: KeyboardEvent): void {
  // The browser would clear a search field by itself; the page's Esc shortcuts must not run either.
  e.preventDefault()
  if (text.value) {
    text.value = ''
    sendNow('')
    return
  }
  const main = document.getElementById('main')
  if (main) main.focus({ preventScroll: true })
  else (e.target as HTMLElement | null)?.blur()
}

function onEnter(): void {
  sendNow(text.value)
  if (text.value.trim()) void focusFirstRow()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key !== 'ArrowDown' || e.isComposing || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
  e.preventDefault()
  sendNow(text.value)
  void focusFirstRow()
}

/**
 * The view's first item in view, else its first item: a List row or Board card (their focus
 * targets carry data-task-row), or the Calendar day that holds the first task chip.
 */
function firstRow(): HTMLElement | null {
  const grid = document.getElementById('main')?.querySelector<HTMLElement>('[data-shortcut-scope="grid"]')
  if (!grid) return null
  const items = Array.from(grid.querySelectorAll<HTMLElement>('[data-task-row]'), (el) => el.closest<HTMLElement>('[data-day]') ?? el)
  const top = Math.max(grid.getBoundingClientRect().top, 0)
  const inView = items.find((el) => {
    const r = el.getBoundingClientRect()
    return r.height > 0 && r.top + r.height / 2 > top && r.top + r.height / 2 < window.innerHeight
  })
  return inView ?? items[0] ?? null
}

/** After the view has rendered the sent text (a second try a frame later, while rows mount). */
async function focusFirstRow(): Promise<void> {
  await nextTick()
  let el = firstRow()
  if (!el) {
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
    el = firstRow()
  }
  if (!el || !root.value?.contains(document.activeElement)) return
  el.focus({ preventScroll: true })
  el.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
}

/** Sends the text at once and drops the pending debounce (the watcher runs after this, and sees the same value). */
function sendNow(v: string): void {
  send.cancel()
  if (v !== props.modelValue) emit('update:modelValue', v)
}

function onFocusIn(): void {
  collapsePending = false
  wide.value = true
}

function onFocusOut(e: FocusEvent): void {
  if (e.relatedTarget instanceof Node && root.value?.contains(e.relatedTarget)) return
  if (pressing) collapsePending = true
  else wide.value = false
}

function onPointerDown(): void {
  pressing = true
}

function onPointerUp(): void {
  pressing = false
  if (!collapsePending) return
  collapsePending = false
  // After the click that follows this pointerup.
  setTimeout(() => {
    if (!root.value?.contains(document.activeElement)) wide.value = false
  }, 0)
}

onMounted(() => {
  window.addEventListener('pointerdown', onPointerDown, true)
  window.addEventListener('pointerup', onPointerUp, true)
  window.addEventListener('pointercancel', onPointerUp, true)
})
onBeforeUnmount(() => {
  window.removeEventListener('pointerdown', onPointerDown, true)
  window.removeEventListener('pointerup', onPointerUp, true)
  window.removeEventListener('pointercancel', onPointerUp, true)
})
</script>

<template>
  <span
    v-show="!isPhone || text !== ''"
    ref="root"
    class="view-search"
    :class="{ 'is-wide': wide }"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
    @keydown="onKeydown"
  >
    <UiInput
      v-model="text"
      class="input"
      type="search"
      size="sm"
      :icon="Search"
      :placeholder="placeholder"
      :aria-label="placeholder"
      autocomplete="off"
      clearable
      @escape="onEscape"
      @enter="onEnter"
    />
  </span>
</template>

<style scoped>
.view-search {
  display: inline-flex;
  flex-shrink: 0;
  width: 200px;
  transition: width var(--dur-3) var(--ease-out);
}

.view-search.is-wide {
  width: 280px;
}

.input {
  width: 100%;
}

@media (max-width: 639px) {
  .view-search,
  .view-search.is-wide {
    width: 200px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .view-search {
    transition: none;
  }
}
</style>

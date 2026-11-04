<script setup lang="ts">
// View tabs (7.12, 3.5): <nav aria-label="Views"> with the links List, Board and Calendar, the
// current one marked aria-current="page" and underlined by a sliding 2px accent bar.
// - `compact` (the header is narrower than 720px): icons only, named by aria-label, with tooltips.
// - `segmented` (phones): a full-width segmented control under the app bar.
// Switching view keeps the query, so an open task panel (?task=) stays open. `label` names the
// nav ("Views"); a page that shows several (the /_ui gallery) gives each its own name.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { CalendarDays, List, SquareKanban } from '@lucide/vue'
import type { Uuid, ViewKind } from '../../types'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    listId: Uuid
    current: ViewKind
    compact?: boolean
    segmented?: boolean
    label?: string
  }>(),
  { compact: false, segmented: false, label: 'Views' },
)

const VIEWS = [
  { kind: 'list', label: 'List', icon: List },
  { kind: 'board', label: 'Board', icon: SquareKanban },
  { kind: 'calendar', label: 'Calendar', icon: CalendarDays },
] as const

const route = useRoute()
const nav = ref<HTMLElement | null>(null)
const bar = ref<{ left: number; width: number } | null>(null)
/** The bar jumps into place on first paint, then slides. */
const ready = ref(false)

const iconOnly = computed(() => props.compact && !props.segmented)

function to(view: ViewKind) {
  return { name: 'list', params: { listId: props.listId, view }, query: route.query }
}

function measure(): void {
  const root = nav.value
  const active = root?.querySelector<HTMLElement>('[aria-current="page"]')
  bar.value = root && active ? { left: active.offsetLeft, width: active.offsetWidth } : null
}

let observer: ResizeObserver | null = null
onMounted(() => {
  measure()
  requestAnimationFrame(() => (ready.value = true))
  if (nav.value && typeof ResizeObserver !== 'undefined') {
    observer = new ResizeObserver(() => measure())
    observer.observe(nav.value)
  }
})
onBeforeUnmount(() => observer?.disconnect())
watch(
  () => [props.current, props.compact, props.segmented],
  () => void nextTick(measure),
)

const barStyle = computed(() =>
  bar.value ? { transform: `translateX(${bar.value.left}px)`, width: `${bar.value.width}px` } : { opacity: 0 },
)
</script>

<template>
  <nav ref="nav" class="view-tabs" :class="{ 'is-compact': iconOnly, 'is-segmented': segmented }" :aria-label="label">
    <RouterLink v-for="v in VIEWS" :key="v.kind" v-slot="{ href, navigate }" :to="to(v.kind)" custom>
      <a
        v-tooltip="iconOnly ? v.label : undefined"
        :href="href"
        class="tab"
        :aria-current="v.kind === current ? 'page' : undefined"
        :aria-label="iconOnly ? v.label : undefined"
        @click="navigate"
      >
        <UiIcon :icon="v.icon" :size="16" />
        <span v-if="!iconOnly" class="label">{{ v.label }}</span>
      </a>
    </RouterLink>
    <span v-if="!segmented" class="bar" :class="{ 'is-ready': ready }" :style="barStyle" aria-hidden="true" />
  </nav>
</template>

<style scoped>
.view-tabs {
  position: relative;
  display: flex;
  align-items: center;
  align-self: stretch;
  gap: var(--space-0-5);
  flex-shrink: 0;
  height: var(--topbar-h);
}

.tab {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-1-5);
  height: var(--control-md);
  padding: 0 var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  text-decoration: none;
  white-space: nowrap;
  -webkit-tap-highlight-color: transparent;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.tab:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.tab[aria-current="page"] {
  color: var(--text-primary);
}

.is-compact .tab {
  width: var(--control-md);
  padding: 0;
}

.bar {
  position: absolute;
  bottom: 0;
  left: 0;
  height: 2px;
  border-radius: 2px 2px 0 0;
  background: var(--accent);
  pointer-events: none;
}

.bar.is-ready {
  transition:
    transform var(--dur-3) var(--ease-out),
    width var(--dur-3) var(--ease-out);
}

/* ---- segmented (phones) ---- */
/* The look of UiSegmented: a --bg-subtle track and a raised --bg-card segment. */
.view-tabs.is-segmented {
  align-self: auto;
  align-items: stretch;
  width: 100%;
  height: var(--control-lg);
  padding: var(--space-0-5);
  gap: var(--space-0-5);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  box-shadow: inset 0 0 0 1px var(--border-subtle);
}

.is-segmented .tab {
  flex: 1 1 0;
  height: auto;
  min-width: 0;
  border-radius: var(--radius-sm);
}

.is-segmented .tab:hover {
  background: transparent;
}

.is-segmented .tab[aria-current="page"] {
  background: var(--bg-card);
  box-shadow: var(--shadow-2), 0 0 0 1px var(--border-subtle);
}

@media (prefers-reduced-motion: reduce) {
  .bar.is-ready {
    transition: none;
  }
}
</style>

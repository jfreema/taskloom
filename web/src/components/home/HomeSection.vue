<script setup lang="ts">
// A Home section (8.5): an h2 button (aria-expanded) with the label, the count and a chevron, then
// its rows while expanded (collapsed sections render none). The button's name is the label alone,
// the count is its description, and the rows are a list named by the heading. Today shows a muted
// "Nothing due today" row when empty. A long section renders 100 rows, then "Show N more" (8.1).
import { computed, useId } from 'vue'
import { ChevronRight } from '@lucide/vue'
import { pluralize } from '../../lib/format'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import type { HomeSectionKey } from './homeSections'

const props = withDefaults(
  defineProps<{
    sectionKey: HomeSectionKey
    label: string
    count: number
    collapsed: boolean
    /** The muted row shown when the section is empty and expanded. */
    emptyText?: string
    /** Rows past the rendered ones ("Show N more"). */
    hiddenCount?: number
  }>(),
  { emptyText: undefined, hiddenCount: 0 },
)

const emit = defineEmits<{ toggle: []; more: [] }>()

defineSlots<{
  /** Header actions, revealed on hover or focus ("Move all to today"). */
  actions?(): unknown
  /** The rows (HomeTaskRow elements). */
  default?(): unknown
}>()

const id = useId()
const headingId = `${id}-heading`
const countId = `${id}-count`
const listId = `${id}-rows`
const countText = computed(() => pluralize(props.count, 'task'))
const moreText = computed(() => `Show ${Math.min(props.hiddenCount, 100)} more`)
</script>

<template>
  <section class="home-section" :data-section="sectionKey" :data-count="count">
    <div class="head" data-row>
      <h2 :id="headingId" class="heading">
        <button
          type="button"
          class="toggle"
          :class="{ 'is-danger': sectionKey === 'overdue' }"
          :aria-expanded="collapsed ? 'false' : 'true'"
          :aria-controls="collapsed ? undefined : listId"
          :aria-describedby="countId"
          data-section-toggle
          @click="emit('toggle')"
        >
          <UiIcon class="chevron" :class="{ 'is-open': !collapsed }" :icon="ChevronRight" :size="14" />
          <span class="label">{{ label }}</span>
          <span class="count tabular" aria-hidden="true">{{ count }}</span>
        </button>
      </h2>
      <span :id="countId" class="sr-only">{{ countText }}</span>
      <span v-if="$slots.actions" class="actions"><slot name="actions" /></span>
    </div>
    <template v-if="!collapsed">
      <template v-if="count > 0">
        <ul :id="listId" class="rows" role="list" :aria-labelledby="headingId">
          <slot />
        </ul>
        <div v-if="hiddenCount > 0" class="more">
          <UiButton variant="ghost" size="sm" @click="emit('more')">{{ moreText }}</UiButton>
        </div>
      </template>
      <p v-else-if="emptyText" :id="listId" class="empty">{{ emptyText }}</p>
    </template>
  </section>
</template>

<style scoped>
.home-section + .home-section {
  margin-top: var(--space-5);
}

.head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--control-md);
  margin-bottom: var(--space-0-5);
}

.heading {
  min-width: 0;
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
}

.toggle {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1-5);
  min-height: var(--control-sm);
  padding: 0 var(--space-2) 0 var(--space-1);
  margin-left: calc(var(--space-1) * -1);
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  cursor: pointer;
  transition: background-color var(--dur-1) var(--ease-out);
}

.toggle:hover {
  background: var(--bg-hover);
}

.toggle.is-danger .label {
  color: var(--danger-text);
}

.chevron {
  flex: none;
  color: var(--text-tertiary);
  transition: transform var(--dur-2) var(--ease-out);
}

.chevron.is-open {
  transform: rotate(90deg);
}

.label {
  white-space: nowrap;
}

.count {
  color: var(--text-tertiary);
  font-weight: var(--fw-medium);
}

.actions {
  display: inline-flex;
  align-items: center;
  margin-left: auto;
}

.rows {
  display: flex;
  flex-direction: column;
  margin: 0;
  padding: 0;
  list-style: none;
}

.more {
  padding: var(--space-1) 0 0 calc(var(--space-2) + var(--control-xs) + var(--space-3) - var(--space-2));
}

.empty {
  display: flex;
  align-items: center;
  min-height: 40px;
  margin: 0;
  padding: 0 var(--space-2) 0 calc(var(--space-2) + var(--control-xs) + var(--space-3));
  color: var(--text-tertiary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}
</style>

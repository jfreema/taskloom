<script setup lang="ts">
// Tag list (7.11): the first `max` tags as chips, then a "+N" chip whose tooltip lists the rest.
// Unknown ids (a tag deleted elsewhere) are skipped. One line by default; `wrap` lets it wrap.
import { computed } from 'vue'
import { useTags } from '../../stores/tags'
import type { Tag, Uuid } from '../../types'
import TagChip from './TagChip.vue'

const props = withDefaults(
  defineProps<{
    tagIds: readonly Uuid[]
    max?: number
    size?: 'sm' | 'md'
    wrap?: boolean
  }>(),
  { max: 2, size: 'sm', wrap: false },
)

const tags = useTags()

const known = computed<Tag[]>(() => props.tagIds.map((id) => tags.get(id)).filter((t): t is Tag => !!t))
const shown = computed(() => known.value.slice(0, Math.max(0, props.max)))
const rest = computed(() => known.value.slice(shown.value.length))
const restTip = computed(() => rest.value.map((t) => t.name).join(', '))
</script>

<template>
  <span v-if="known.length" class="tag-list" :class="{ 'is-wrapping': wrap }">
    <TagChip v-for="t in shown" :key="t.id" :tag="t" :size="size" />
    <span
      v-if="rest.length"
      v-tooltip="restTip"
      class="more tabular"
      :class="`more--${size}`"
      role="img"
      :aria-label="`${rest.length} more: ${restTip}`"
    >+{{ rest.length }}</span>
  </span>
</template>

<style scoped>
.tag-list {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  max-width: 100%;
  overflow: hidden;
}

.tag-list.is-wrapping {
  flex-wrap: wrap;
  overflow: visible;
}

.more {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  height: 18px;
  padding: 0 var(--space-1-5);
  border-radius: var(--radius-full);
  background: var(--bg-subtle);
  box-shadow: inset 0 0 0 1px var(--border-subtle);
  color: var(--text-secondary);
  font-size: var(--fs-2xs);
  line-height: var(--lh-2xs);
  font-weight: var(--fw-medium);
}

.more--md {
  height: 22px;
  padding: 0 var(--space-2);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}
</style>

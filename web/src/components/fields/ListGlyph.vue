<script setup lang="ts">
// A list's icon (Look 2): its chosen icon (lib/listIcons.ts) in its colour, or the plain list icon
// in --text-tertiary when it has none. Decorative: the list name is always written next to it.
// `active` is the sidebar's current list, whose uncoloured icon takes the accent like its text.
import { computed } from 'vue'
import { listIconOf } from '../../lib/listIcons'
import UiIcon from '../ui/UiIcon.vue'

const props = withDefaults(
  defineProps<{
    list: { color: string | null; icon: string | null }
    size?: 14 | 16 | 20
    active?: boolean
  }>(),
  { size: 16, active: false },
)

const icon = computed(() => listIconOf(props.list.icon).icon)
</script>

<template>
  <UiIcon
    class="list-glyph"
    :class="{ 'has-color': !!list.color, 'is-active': active }"
    :style="list.color ? { '--c': list.color } : undefined"
    :icon="icon"
    :size="size"
    data-list-glyph
    :data-icon="list.icon ?? 'list'"
  />
</template>

<style scoped>
.list-glyph {
  flex: none;
  color: var(--text-tertiary);
}

.list-glyph.is-active {
  color: var(--accent-text);
}

.list-glyph.has-color {
  color: var(--c);
}
</style>

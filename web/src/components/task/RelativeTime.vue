<script setup lang="ts">
// An activity or comment time (8.10): "just now", "5m", "2h", "Yesterday", "Nov 3" in a
// <time datetime>, with the full timestamp as its tooltip.
import { computed } from 'vue'
import { formatRelativeTime, formatTimestamp } from '../../lib/dates'
import type { IsoTimestamp } from '../../types'
import { useNow } from './useNow'

const props = defineProps<{ at: IsoTimestamp }>()

const now = useNow()
const text = computed(() => formatRelativeTime(props.at, now.value))
const full = computed(() => formatTimestamp(props.at))
</script>

<template>
  <time v-tooltip="full" class="relative-time tabular" :datetime="at">{{ text }}</time>
</template>

<style scoped>
.relative-time {
  white-space: nowrap;
}
</style>

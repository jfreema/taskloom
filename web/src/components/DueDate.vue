<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ date: string | null; done?: boolean }>()
const emit = defineEmits<{ change: [date: string | null] }>()

const label = computed(() => {
  if (!props.date) return ''
  const d = new Date(props.date + 'T00:00:00')
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const days = Math.round((d.getTime() - today.getTime()) / 86400000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Tomorrow'
  if (days === -1) return 'Yesterday'
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: d.getFullYear() === today.getFullYear() ? undefined : 'numeric' })
})
const overdue = computed(() => !props.done && !!props.date && new Date(props.date + 'T23:59:59') < new Date())
</script>

<template>
  <label class="due" :class="{ overdue, empty: !date }">
    <span>{{ date ? label : '📅' }}</span>
    <input type="date" :value="date ?? ''" @change="emit('change', ($event.target as HTMLInputElement).value || null)" />
  </label>
</template>

<style scoped>
.due { position: relative; display: inline-flex; align-items: center; padding: 3px 6px; border-radius: 6px; cursor: pointer; white-space: nowrap; font-size: 13px; }
.due:hover { background: var(--bg-hover); }
.due.empty { filter: grayscale(1); opacity: 0.5; }
.due.overdue { color: var(--danger); }
.due input { position: absolute; inset: 0; opacity: 0; cursor: pointer; border: none; padding: 0; }
.due input::-webkit-calendar-picker-indicator { position: absolute; inset: 0; width: 100%; height: 100%; cursor: pointer; }
</style>

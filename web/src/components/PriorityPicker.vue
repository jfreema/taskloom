<script setup lang="ts">
import { computed } from 'vue'
import Popover from './Popover.vue'
import { priorities, type Priority } from '../types'

const props = defineProps<{ priority: Priority; showLabel?: boolean }>()
const emit = defineEmits<{ change: [priority: Priority] }>()
const current = computed(() => priorities.find((p) => p.value === props.priority)!)
</script>

<template>
  <Popover>
    <template #trigger>
      <button class="flag" :title="current.label">
        <span v-if="priority === 'None'" class="faint">⚑</span>
        <span v-else :style="{ color: current.color }">⚑</span>
        <span v-if="showLabel" :class="{ faint: priority === 'None' }">{{ priority === 'None' ? 'Empty' : current.label }}</span>
      </button>
    </template>
    <template #default="{ close }">
      <button v-for="p in priorities" :key="p.value" @click="emit('change', p.value); close()">
        <span :style="{ color: p.value === 'None' ? 'var(--text-faint)' : p.color }">⚑</span> {{ p.label }}
      </button>
    </template>
  </Popover>
</template>

<style scoped>
.flag { display: inline-flex; align-items: center; gap: 6px; }
</style>

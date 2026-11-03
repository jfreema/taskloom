<script setup lang="ts">
import { computed } from 'vue'
import Popover from './Popover.vue'
import { useList } from '../stores/list'

const props = defineProps<{ statusId: string; compact?: boolean }>()
const emit = defineEmits<{ change: [statusId: string] }>()
const list = useList()
const status = computed(() => list.statusById(props.statusId))
</script>

<template>
  <Popover>
    <template #trigger>
      <button v-if="compact" class="status-dot" :title="status?.name" :style="{ borderColor: status?.color, background: status?.category === 'Done' ? status?.color : 'transparent' }">
        <span v-if="status?.category === 'Done'">✓</span>
      </button>
      <button v-else class="status-pill" :style="{ background: status?.color }">{{ status?.name }}</button>
    </template>
    <template #default="{ close }">
      <button v-for="s in list.statuses" :key="s.id" @click="emit('change', s.id); close()">
        <span class="dot" :style="{ background: s.color }" /> {{ s.name }}
        <span v-if="s.id === statusId" class="spacer" /><span v-if="s.id === statusId">✓</span>
      </button>
    </template>
  </Popover>
</template>

<style scoped>
.status-pill { color: #fff; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.03em; padding: 3px 8px; border-radius: 4px; white-space: nowrap; }
.status-pill:hover { filter: brightness(1.1); background-image: none; }
.status-dot { width: 16px; height: 16px; border-radius: 50%; border: 2px solid; padding: 0; color: #fff; font-size: 9px; line-height: 1; display: grid; place-items: center; flex: none; }
</style>

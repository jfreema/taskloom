<script setup lang="ts">
import { computed } from 'vue'
import Popover from './Popover.vue'
import Avatar from './Avatar.vue'
import { useList } from '../stores/list'

const props = defineProps<{ assigneeId: string | null; showLabel?: boolean }>()
const emit = defineEmits<{ change: [assigneeId: string | null] }>()
const list = useList()
const assignee = computed(() => list.memberById(props.assigneeId))
</script>

<template>
  <Popover>
    <template #trigger>
      <button class="who" :title="assignee?.displayName ?? 'Assign'">
        <Avatar v-if="assignee" :name="assignee.displayName" />
        <span v-else class="empty-avatar">👤</span>
        <span v-if="showLabel" :class="{ faint: !assignee }">{{ assignee?.displayName ?? 'Empty' }}</span>
      </button>
    </template>
    <template #default="{ close }">
      <button v-for="m in list.detail?.members ?? []" :key="m.id" @click="emit('change', m.id); close()">
        <Avatar :name="m.displayName" /> {{ m.displayName }}
      </button>
      <button v-if="assigneeId" class="muted" @click="emit('change', null); close()">Unassign</button>
    </template>
  </Popover>
</template>

<style scoped>
.who { display: inline-flex; align-items: center; gap: 6px; }
.empty-avatar { width: 22px; height: 22px; border-radius: 50%; border: 1px dashed var(--text-faint); display: inline-grid; place-items: center; font-size: 10px; filter: grayscale(1); opacity: 0.7; }
</style>

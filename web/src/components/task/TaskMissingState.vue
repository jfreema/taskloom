<script setup lang="ts">
// A task that doesn't exist, was deleted or is out of reach (8.10), in the not-found pattern of the
// list and task pages (CircleAlert): the page offers Home (primary), the panel offers Close.
import { CircleAlert } from '@lucide/vue'
import UiButton from '../ui/UiButton.vue'
import UiEmptyState from '../ui/UiEmptyState.vue'

defineProps<{ page: boolean }>()
const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <UiEmptyState v-if="page" :icon="CircleAlert" title="This task doesn't exist or you don't have access.">
    <template #actions>
      <UiButton variant="primary" to="/">Go to Home</UiButton>
    </template>
  </UiEmptyState>
  <UiEmptyState v-else :icon="CircleAlert" title="This task doesn't exist or was deleted.">
    <template #actions>
      <UiButton @click="emit('close')">Close</UiButton>
    </template>
  </UiEmptyState>
</template>

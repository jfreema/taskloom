<script setup lang="ts">
// The full-page task /t/:taskId (spec 8.10). Placeholder until B4 builds the page: it renders
// TaskDetail in page mode with its own title id.
import type { Uuid } from '../types'
import TaskDetail from '../components/task/TaskDetail.vue'

defineProps<{ taskId: Uuid }>()
</script>

<template>
  <div class="task-page">
    <TaskDetail :task-id="taskId" mode="page" title-id="task-page-title" />
  </div>
</template>

<style scoped>
.task-page {
  width: 100%;
  max-width: var(--reading-w);
  margin: 0 auto;
  padding: var(--space-6);
}
</style>

<script setup lang="ts">
// Calendar view (spec 8.9). Placeholder until B3 builds it.
import { CalendarDays } from '@lucide/vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
</script>

<template>
  <UiEmptyState :icon="CalendarDays" title="Coming soon" description="The calendar view is on its way." />
</template>

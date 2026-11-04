<script setup lang="ts">
// The guest notice (spec 7.12): ListPage shows it under the page header for guests, TaskDetail at
// the top of the panel and the task page. role="status" comes from UiInlineAlert (info).
import UiInlineAlert from '../ui/UiInlineAlert.vue'
</script>

<template>
  <UiInlineAlert class="guest-notice" tone="info">You're a guest: you can comment but not edit.</UiInlineAlert>
</template>

<script setup lang="ts">
// The list page (8.6). Placeholder until A3 part 2 builds it: the list name as the page heading.
import { computed } from 'vue'
import { useWorkspaces } from '../stores/workspaces'

const props = defineProps<{ listId: string; view: string }>()
const workspaces = useWorkspaces()

const name = computed(() => workspaces.findList(props.listId)?.list.name ?? 'List')
</script>

<template>
  <h1 class="list-name">{{ name }}</h1>
</template>

<style scoped>
.list-name {
  margin: 0;
  padding: var(--space-4) var(--space-6);
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  font-weight: var(--fw-semibold);
}
</style>

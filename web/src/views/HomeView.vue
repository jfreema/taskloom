<script setup lang="ts">
import { watchEffect } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkspaces } from '../stores/workspaces'

const ws = useWorkspaces()
const router = useRouter()

watchEffect(() => {
  if (ws.firstListId) router.replace({ name: 'list', params: { listId: ws.firstListId, view: 'list' } })
})
</script>

<template>
  <div class="empty" v-if="ws.loaded && !ws.firstListId">
    <h2>Nothing here yet</h2>
    <p>Create a Space in the sidebar, then add a List to start tracking tasks.</p>
  </div>
</template>

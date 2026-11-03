<script setup lang="ts">
import { onMounted } from 'vue'
import Sidebar from '../components/Sidebar.vue'
import { useWorkspaces } from '../stores/workspaces'

const ws = useWorkspaces()
onMounted(() => ws.load())
</script>

<template>
  <div class="shell">
    <Sidebar />
    <main class="main"><RouterView /></main>
  </div>
</template>

<style scoped>
.shell { display: flex; height: 100%; }
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; overflow: hidden; }
@media (max-width: 720px) { .shell { flex-direction: column; } }
</style>

<script setup lang="ts">
// /_ui gallery: loading states, progress, empty and error states, inline alerts.
import { ref } from 'vue'
import { List, Plus } from '@lucide/vue'
import { useToast } from '../../../composables/useToast'
import UiButton from '../UiButton.vue'
import UiEmptyState from '../UiEmptyState.vue'
import UiErrorState from '../UiErrorState.vue'
import UiInlineAlert from '../UiInlineAlert.vue'
import UiLoadingBar from '../UiLoadingBar.vue'
import UiProgress from '../UiProgress.vue'
import UiSkeleton from '../UiSkeleton.vue'
import UiSpinner from '../UiSpinner.vue'
import UiSwitch from '../UiSwitch.vue'
import GalleryCard from './GalleryCard.vue'

const toast = useToast()
const loading = ref(true)
</script>

<template>
  <GalleryCard title="UiSkeleton">
    <div class="row align-start">
      <UiSkeleton variant="circle" width="32px" height="32px" />
      <div class="grow"><UiSkeleton :lines="3" /></div>
    </div>
    <UiSkeleton variant="rect" height="56px" />
  </GalleryCard>

  <GalleryCard title="UiSpinner and UiLoadingBar">
    <div class="row muted">
      <UiSpinner :size="14" /><UiSpinner /><UiSpinner :size="20" label="Loading tasks" />
    </div>
    <div class="bar-demo">
      <span>Page header</span>
      <UiLoadingBar :active="loading" />
    </div>
    <UiSwitch v-model="loading" label="Loading" />
  </GalleryCard>

  <GalleryCard title="UiProgress">
    <div class="stack">
      <UiProgress :value="2" :max="5" label="Subtasks, 2 of 5 done" />
      <UiProgress :value="3" :max="3" label="Subtasks, all done" />
      <div class="row">
        <UiProgress variant="ring" :value="1" :max="4" label="1 of 4" />
        <UiProgress variant="ring" :value="3" :max="4" label="3 of 4" />
        <UiProgress variant="ring" :value="4" :max="4" label="4 of 4" />
      </div>
    </div>
  </GalleryCard>

  <GalleryCard title="UiEmptyState">
    <UiEmptyState :icon="List" title="No tasks yet" description="Tasks you add to this list show up here." kbd="N">
      <template #actions><UiButton variant="primary" :icon="Plus">New task</UiButton></template>
    </UiEmptyState>
  </GalleryCard>

  <GalleryCard title="UiErrorState">
    <UiErrorState :retry="() => toast.show({ message: 'Retrying…' })" />
  </GalleryCard>

  <GalleryCard title="UiInlineAlert" wide>
    <div class="stack">
      <UiInlineAlert tone="info">You're a guest: you can comment but not edit.</UiInlineAlert>
      <UiInlineAlert tone="warning" title="Filters are hiding 12 tasks">Clear them to see everything.</UiInlineAlert>
      <UiInlineAlert tone="danger" title="Couldn't sign you in" dismissible>We couldn't verify your email address.</UiInlineAlert>
      <UiInlineAlert tone="success">
        Invites sent.
        <template #actions><UiButton size="sm" variant="link">Copy sign-in link</UiButton></template>
      </UiInlineAlert>
    </div>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.bar-demo {
  position: relative;
  display: flex;
  align-items: center;
  height: var(--topbar-h);
  padding: 0 var(--space-3);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  overflow: hidden;
}
</style>

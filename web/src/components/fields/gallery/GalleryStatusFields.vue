<script setup lang="ts">
// /_ui: StatusIcon (every status of the sample list, and the sizes), StatusPill (sizes,
// interactive, icon only), StatusPicker (icon, pill, chip and read-only; 1–9 pick while open) and
// a picker opened against a card with hideTrigger + anchor, the way a Board card's S works.
import { computed, ref } from 'vue'
import { useWorkspaces } from '../../../stores/workspaces'
import type { Uuid } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import UiButton from '../../ui/UiButton.vue'
import StatusIcon from '../StatusIcon.vue'
import StatusPicker from '../StatusPicker.vue'
import StatusPill from '../StatusPill.vue'

const props = defineProps<{ listId: Uuid }>()

const workspaces = useWorkspaces()
const statuses = computed(() => workspaces.statusesFor(props.listId))
const at = (i: number) => statuses.value[Math.min(i, statuses.value.length - 1)]?.id ?? ''

const iconValue = ref(at(0))
const pillValue = ref(at(1))
const chipValue = ref(at(2))
const cardValue = ref(at(1))
const cardOpen = ref(false)
const card = ref<HTMLElement | null>(null)

const nameOf = (id: Uuid) => workspaces.statusById(id)?.name ?? 'none'
const first = computed(() => statuses.value[1] ?? statuses.value[0])
</script>

<template>
  <GalleryCard title="Status fields" wide>
    <div class="row">
      <span v-for="s in statuses" :key="s.id" class="labelled">
        <StatusIcon :status="s" :size="16" :decorative="false" />
        <span class="small muted">{{ s.name }}</span>
      </span>
    </div>
    <div v-if="first" class="row">
      <StatusIcon v-for="size in [14, 16, 18, 20] as const" :key="size" :status="first" :size="size" />
      <StatusPill :status="first" :size="20" />
      <StatusPill :status="first" :size="24" />
      <StatusPill :status="first" :size="28" />
      <StatusPill :status="first" :size="24" icon-only />
    </div>
    <div class="row">
      <StatusPicker v-model="iconValue" :list-id="listId" variant="icon" />
      <StatusPicker v-model="pillValue" :list-id="listId" variant="pill" />
      <StatusPicker v-model="chipValue" :list-id="listId" variant="chip" />
      <StatusPicker :model-value="pillValue" :list-id="listId" readonly />
      <span class="small muted">Icon: {{ nameOf(iconValue) }} · Pill: {{ nameOf(pillValue) }} · Chip: {{ nameOf(chipValue) }}</span>
    </div>
    <div class="row">
      <div ref="card" class="demo-card" role="group" tabindex="0" aria-label="Sample card" @keydown.s.exact.prevent="cardOpen = true">
        <span class="small muted">Card · status {{ nameOf(cardValue) }}</span>
      </div>
      <UiButton size="sm" @click="cardOpen = true">Open the status picker on the card</UiButton>
      <StatusPicker v-model="cardValue" v-model:open="cardOpen" :list-id="listId" hide-trigger :anchor="card" />
    </div>
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

<style scoped>
.labelled {
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
}

.demo-card {
  display: flex;
  align-items: center;
  width: 220px;
  height: 56px;
  padding: 0 var(--space-3);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  box-shadow: var(--shadow-1);
}
</style>

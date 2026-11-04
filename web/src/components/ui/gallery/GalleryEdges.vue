<script setup lang="ts">
// /_ui gallery: triggers pinned to the viewport's corners and right edge, to check that popovers
// and menus flip and shift to stay on screen.
import { ref } from 'vue'
import { Calendar, Ellipsis } from '@lucide/vue'
import type { IsoDate } from '../../../types'
import UiButton from '../UiButton.vue'
import UiDatePicker from '../UiDatePicker.vue'
import UiIconButton from '../UiIconButton.vue'
import UiMenu from '../UiMenu.vue'
import UiPopover from '../UiPopover.vue'
import UiSwitch from '../UiSwitch.vue'
import GalleryCard from './GalleryCard.vue'
import type { MenuItem } from '../menu'

const on = ref(false)
const date = ref<IsoDate | null>(null)

function pick(v: IsoDate | null, close: () => void): void {
  date.value = v
  close()
}

const items = (): MenuItem[] => [
  { id: 'one', label: 'First item', onSelect: () => undefined },
  { id: 'two', label: 'Second item with a longer label', onSelect: () => undefined },
  {
    kind: 'submenu',
    id: 'more',
    label: 'More options',
    items: [
      { id: 'a', label: 'Option A', onSelect: () => undefined },
      { id: 'b', label: 'Option B', onSelect: () => undefined },
    ],
  },
  { id: 'three', label: 'Third item', onSelect: () => undefined },
]

const corners = [
  { id: 'top-left', label: 'Menu at top left', kind: 'menu' },
  { id: 'top-right', label: 'Menu at top right', kind: 'menu' },
  { id: 'mid-right', label: 'Menu at right edge', kind: 'menu' },
  { id: 'bottom-left', label: 'Date at bottom left', kind: 'date' },
  { id: 'bottom-right', label: 'Date at bottom right', kind: 'date' },
] as const
</script>

<template>
  <GalleryCard title="Popovers at the edges">
    <UiSwitch v-model="on" label="Pin triggers to the edges" description="Each opens a menu or a date picker that has to flip or shift." />
    <div v-if="on" class="edges">
      <template v-for="c in corners" :key="c.id">
        <UiMenu v-if="c.kind === 'menu'" :items="items" :aria-label="c.label">
          <template #trigger="{ props }">
            <UiIconButton v-bind="props" :class="['edge', c.id]" :icon="Ellipsis" :label="c.label" variant="secondary" />
          </template>
        </UiMenu>
        <UiPopover v-else :aria-label="c.label">
          <template #trigger="{ props }">
            <UiButton v-bind="props" :class="['edge', c.id]" :icon="Calendar" size="sm">{{ c.label }}</UiButton>
          </template>
          <template #default="{ close }">
            <UiDatePicker :model-value="date" @update:model-value="pick($event, close)" />
          </template>
        </UiPopover>
      </template>
    </div>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

<style scoped>
.edge {
  position: fixed;
  z-index: var(--z-panel);
}

.top-left {
  top: var(--space-1);
  left: var(--space-1);
}

.top-right {
  top: var(--space-1);
  right: var(--space-1);
}

.mid-right {
  top: 50%;
  right: var(--space-1);
}

.bottom-left {
  bottom: var(--space-1);
  left: var(--space-1);
}

.bottom-right {
  bottom: var(--space-1);
  right: var(--space-1);
}
</style>

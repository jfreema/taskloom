<script setup lang="ts">
// One tag on the Tags page (8.13): its TagChip as a preview, the name (inline rename, Member+), a
// colour button that opens UiColorSwatches (Member+) and Delete (Admin+). Guests and others who
// can't edit see the chip and the name only. The page applies the changes.
import { Trash2 } from '@lucide/vue'
import type { Tag } from '../../types'
import TagChip from '../fields/TagChip.vue'
import SwatchPopover from '../hierarchy/SwatchPopover.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import UiInlineEdit from '../ui/UiInlineEdit.vue'

defineProps<{
  tag: Tag
  /** Member+: rename and recolour. */
  canEdit: boolean
  /** Admin+: delete. */
  canDelete: boolean
}>()

const emit = defineEmits<{ rename: [name: string]; recolour: [hex: string]; remove: [] }>()
</script>

<template>
  <li class="tag-row" :data-tag-id="tag.id">
    <span class="preview" aria-hidden="true"><TagChip :tag="tag" size="md" /></span>
    <span class="name">
      <UiInlineEdit v-if="canEdit" :model-value="tag.name" aria-label="Tag name" :maxlength="50" @save="emit('rename', $event)" />
      <span v-else class="truncate">{{ tag.name }}</span>
    </span>
    <SwatchPopover v-if="canEdit" :color="tag.color" :label="`Colour for ${tag.name}`" @pick="emit('recolour', $event)">
      <span class="dot" :style="{ '--c': tag.color }" />
    </SwatchPopover>
    <UiIconButton v-if="canDelete" class="remove" :icon="Trash2" :label="`Delete ${tag.name}`" size="sm" @click="emit('remove')" />
  </li>
</template>

<style scoped>
.tag-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: 48px;
  padding: var(--space-1-5) var(--space-3) var(--space-1-5) var(--space-4);
}

.tag-row + .tag-row {
  border-top: 1px solid var(--border-subtle);
}

.preview {
  display: flex;
  flex: 0 0 128px;
  min-width: 0;
}

.name {
  display: flex;
  flex: 1;
  min-width: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-primary);
}

.tag-row .remove:not(.is-disabled):hover {
  background-color: var(--danger-subtle);
  color: var(--danger-text);
}

.dot {
  width: 14px;
  height: 14px;
  border-radius: var(--radius-full);
  background: var(--c);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--text-primary) 14%, transparent);
}

@media (max-width: 639px) {
  .tag-row {
    gap: var(--space-2);
  }

  .preview {
    flex-basis: 96px;
  }
}
</style>

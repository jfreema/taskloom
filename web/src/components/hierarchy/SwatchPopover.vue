<script setup lang="ts">
// A colour button that opens UiColorSwatches in a popover (8.13): the status editor's colour
// button and each tag's colour button on the Tags page. The slot is what the button shows (a
// StatusIcon, a colour dot). Picking a colour closes the popover, returns focus to the button and
// emits `pick` when the colour changed.
import { ref } from 'vue'
import UiColorSwatches from '../ui/UiColorSwatches.vue'
import UiPopover from '../ui/UiPopover.vue'

const props = defineProps<{
  color: string
  /** The button's name and tooltip, e.g. "Colour for Review". */
  label: string
}>()

const emit = defineEmits<{ pick: [hex: string] }>()

const open = ref(false)

function onPick(hex: string, close: () => void): void {
  close()
  if (hex.trim().toLowerCase() !== props.color.trim().toLowerCase()) emit('pick', hex)
}
</script>

<template>
  <UiPopover v-model:open="open" :aria-label="label" placement="bottom-start">
    <template #trigger="{ props: trigger }">
      <button v-bind="trigger" v-tooltip="label" type="button" class="swatch-button" :aria-label="label">
        <slot />
      </button>
    </template>
    <template #default="{ close }">
      <div class="swatch-panel">
        <UiColorSwatches :model-value="color" :aria-label="label" @pick="onPick($event, () => close('select'))" />
      </div>
    </template>
  </UiPopover>
</template>

<style scoped>
.swatch-button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: var(--control-sm);
  height: var(--control-sm);
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: background-color var(--dur-1) var(--ease-out);
}

.swatch-button:hover,
.swatch-button[aria-expanded="true"] {
  background: var(--bg-hover);
}

/* A 44px hit area on touch screens, without changing the layout. */
@media (pointer: coarse) {
  .swatch-button::after {
    content: "";
    position: absolute;
    inset: calc((var(--touch-min) - var(--control-sm)) / -2);
  }
}

.swatch-panel {
  padding: var(--space-2);
}
</style>

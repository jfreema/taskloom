<script setup lang="ts">
// On/off switch (spec 7.10): a <button role="switch" aria-checked>, 28×16 with a 12px thumb and an
// accent track when on. The label names it; the description is linked with aria-describedby.
import { useId } from 'vue'

const model = defineModel<boolean>({ required: true })

withDefaults(
  defineProps<{
    label: string
    description?: string
    labelHidden?: boolean
    disabled?: boolean
  }>(),
  { description: undefined, labelHidden: false, disabled: false },
)

const id = useId()

function toggle(): void {
  model.value = !model.value
}
</script>

<template>
  <div class="ui-switch" :class="{ 'is-disabled': disabled }">
    <button
      :id="id"
      type="button"
      role="switch"
      class="track"
      :class="{ 'is-on': model }"
      :aria-checked="model ? 'true' : 'false'"
      :aria-labelledby="`${id}-label`"
      :aria-describedby="description ? `${id}-desc` : undefined"
      :disabled="disabled"
      @click="toggle"
    >
      <span class="thumb" aria-hidden="true" />
    </button>
    <span class="text" :class="{ 'sr-only': labelHidden && !description }">
      <label :id="`${id}-label`" class="label" :class="{ 'sr-only': labelHidden }" :for="id">{{ label }}</label>
      <span v-if="description" :id="`${id}-desc`" class="description">{{ description }}</span>
    </span>
  </div>
</template>

<style scoped>
.ui-switch {
  display: inline-flex;
  align-items: flex-start;
  gap: var(--space-2);
  min-width: 0;
}

.track {
  position: relative;
  flex-shrink: 0;
  width: 28px;
  height: 16px;
  margin-top: 2px;
  padding: 0;
  border: 0;
  border-radius: var(--radius-full);
  background: var(--border-control);
  cursor: pointer;
  transition: background-color var(--dur-2) var(--ease-in-out);
}

.track.is-on {
  background: var(--accent);
}

.track:not(:disabled):hover {
  background: var(--text-secondary);
}

.track.is-on:not(:disabled):hover {
  background: var(--accent-hover);
}

.thumb {
  position: absolute;
  top: 2px;
  left: 2px;
  width: 12px;
  height: 12px;
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  box-shadow: var(--shadow-1);
}

.track.is-on .thumb {
  transform: translateX(12px);
}

@media (prefers-reduced-motion: no-preference) {
  .thumb {
    transition: transform var(--dur-2) var(--ease-in-out);
  }
}

.text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.label {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-primary);
  cursor: pointer;
}

.description {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

.is-disabled {
  opacity: 0.45;
}

.is-disabled .track,
.is-disabled .label {
  cursor: not-allowed;
}

@media (pointer: coarse) {
  .track::after {
    content: "";
    position: absolute;
    left: 50%;
    top: 50%;
    width: var(--touch-min);
    height: var(--touch-min);
    transform: translate(-50%, -50%);
  }
}
</style>

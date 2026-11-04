<script setup lang="ts">
// The inline "New tag" row of the Tags page (8.13): a colour (the next swatch after the newest
// tag's, 7.2) and a name. Enter or "Add tag" adds it and clears the row for the next one; Esc or
// "Cancel" closes it. A name already used in the workspace (any case) is refused under the field.
import { nextTick, onMounted, ref, useId } from 'vue'
import { useTags } from '../../stores/tags'
import SwatchPopover from '../hierarchy/SwatchPopover.vue'
import UiButton from '../ui/UiButton.vue'
import UiInput from '../ui/UiInput.vue'

const emit = defineEmits<{ add: [r: { name: string; color: string }]; close: [] }>()

const tags = useTags()
const errorId = `${useId()}-error`

const name = ref('')
const color = ref(tags.nextColor())
const error = ref('')
const input = ref<InstanceType<typeof UiInput> | null>(null)

onMounted(() => input.value?.focus())

function submit(): void {
  const trimmed = name.value.trim()
  if (!trimmed) {
    error.value = 'Enter a tag name.'
    input.value?.focus()
    return
  }
  const taken = tags.current.find((t) => t.name.trim().toLowerCase() === trimmed.toLowerCase())
  if (taken) {
    error.value = `A tag called “${taken.name}” already exists.`
    input.value?.focus()
    return
  }
  emit('add', { name: trimmed, color: color.value })
  name.value = ''
  error.value = ''
  void nextTick(() => {
    color.value = tags.nextColor()
    input.value?.focus()
  })
}

function onEscape(e: KeyboardEvent): void {
  e.preventDefault()
  emit('close')
}
</script>

<template>
  <form class="new-tag" novalidate @submit.prevent="submit">
    <div class="fields">
      <SwatchPopover :color="color" label="Colour for the new tag" @pick="color = $event">
        <span class="dot" :style="{ '--c': color }" />
      </SwatchPopover>
      <UiInput
        ref="input"
        v-model="name"
        class="name"
        size="sm"
        :maxlength="50"
        placeholder="Tag name, for example finance"
        aria-label="New tag name"
        autocomplete="off"
        :invalid="!!error"
        :aria-describedby="error ? errorId : undefined"
        @update:model-value="error = ''"
        @escape="onEscape"
      />
      <UiButton variant="primary" size="sm" type="submit">Add tag</UiButton>
      <UiButton variant="ghost" size="sm" @click="emit('close')">Cancel</UiButton>
    </div>
    <p v-if="error" :id="errorId" class="error">{{ error }}</p>
  </form>
</template>

<style scoped>
.new-tag {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  padding: var(--space-2) var(--space-3) var(--space-2) var(--space-3);
  background: var(--bg-subtle);
  border-bottom: 1px solid var(--border-subtle);
}

.fields {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.name {
  flex: 1;
  min-width: 0;
}

.dot {
  width: 14px;
  height: 14px;
  border-radius: var(--radius-full);
  background: var(--c);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--text-primary) 14%, transparent);
}

.error {
  padding-left: calc(var(--control-sm) + var(--space-2));
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--danger-text);
}

@media (max-width: 639px) {
  .fields {
    flex-wrap: wrap;
  }

  .name {
    flex-basis: calc(100% - var(--control-sm) - var(--space-2));
  }
}
</style>

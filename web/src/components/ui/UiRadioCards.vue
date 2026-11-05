<script setup lang="ts">
// Radio cards (spec 7.10): native radio inputs inside label cards, so arrow keys and forms work
// natively. The selected card has a 2px accent border and an accent-subtle background.
// Used for theme cards and status presets; the `preview` slot draws a picture above the label.
// Each native radio is transparent and covers its whole card, so a pointer on the card (and
// tools that click the radio itself) reach it directly.
import { useId } from 'vue'

export interface RadioCardOption {
  value: string
  label: string
  description?: string
}

const model = defineModel<string>({ required: true })

withDefaults(
  defineProps<{
    options: RadioCardOption[]
    name: string
    /** Required in practice; optional in the type so `aria-label="…"` also type-checks (it maps to this prop). */
    ariaLabel?: string
    columns?: 1 | 2 | 3
  }>(),
  { columns: 3 },
)

const id = useId()
</script>

<template>
  <div class="ui-radio-cards" :class="`ui-radio-cards--${columns}`" role="radiogroup" :aria-label="ariaLabel">
    <label
      v-for="(option, i) in options"
      :key="option.value"
      class="card"
      :class="{ 'is-selected': option.value === model }"
    >
      <input
        v-model="model"
        class="native"
        type="radio"
        :name="name"
        :value="option.value"
        :aria-labelledby="`${id}-${i}-label`"
        :aria-describedby="option.description ? `${id}-${i}-desc` : undefined"
      />
      <span v-if="$slots.preview" class="preview">
        <slot name="preview" :option="option" :selected="option.value === model" />
      </span>
      <span class="body">
        <span :id="`${id}-${i}-label`" class="label">{{ option.label }}</span>
        <span v-if="option.description" :id="`${id}-${i}-desc`" class="description">{{ option.description }}</span>
      </span>
    </label>
  </div>
</template>

<style scoped>
.ui-radio-cards {
  display: grid;
  gap: var(--space-3);
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.ui-radio-cards--1 {
  grid-template-columns: minmax(0, 1fr);
}

.ui-radio-cards--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.card {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-width: 0;
  padding: var(--space-3);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-md);
  background: var(--bg-surface);
  cursor: pointer;
  transition:
    border-color var(--dur-1) var(--ease-out),
    background-color var(--dur-1) var(--ease-out),
    box-shadow var(--dur-1) var(--ease-out);
}

.card:hover {
  border-color: color-mix(in srgb, var(--border-control) 45%, var(--border-default));
}

/* 2px accent border without moving the content: 1px border plus a 1px inset ring. */
.card.is-selected {
  border-color: var(--accent);
  box-shadow: inset 0 0 0 1px var(--accent);
  background: var(--accent-subtle);
}

/* The radio itself: invisible, above the card's content, and as large as the card. */
.native {
  position: absolute;
  inset: 0;
  z-index: 1;
  width: 100%;
  height: 100%;
  margin: 0;
  opacity: 0;
  appearance: none;
  cursor: pointer;
}

.card:has(input:focus-visible) {
  outline: 2px solid var(--focus-ring);
  outline-offset: 2px;
}

.preview {
  display: block;
  min-width: 0;
}

.body {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  min-width: 0;
}

.label {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.description {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
}

/* Text-only cards stack on narrow screens; cards with a preview (theme pictures) keep their columns. */
@media (max-width: 479px) {
  .ui-radio-cards--2:not(:has(.preview)),
  .ui-radio-cards--3:not(:has(.preview)) {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>

<script setup lang="ts">
// Internal to UiListbox: one role="option" row (an option, the clear row or the create row) with
// its icon, label, description and check. Pointer listeners fall through to the row; the default
// slot replaces an option's icon and text (UiListbox's `option` slot).
import { Check, Plus } from '@lucide/vue'
import UiIcon from './UiIcon.vue'
import type { ListboxRow } from './listbox'

defineProps<{
  row: ListboxRow
  id: string
  selected: boolean
  active: boolean
  /** The inset focus ring: the row is active, was reached by keyboard and the list has focus (9.6). */
  ring: boolean
  /** The create row's text, "Create “q”". */
  createText: string
}>()
</script>

<template>
  <div
    :id="id"
    class="option"
    :class="{
      'is-active': active,
      'is-ring': ring,
      'is-disabled': row.kind === 'option' && row.option.disabled,
      'is-create': row.kind === 'create',
      'is-clear': row.kind === 'clear',
    }"
    role="option"
    :aria-selected="selected ? 'true' : 'false'"
    :aria-disabled="row.kind === 'option' && row.option.disabled ? 'true' : undefined"
  >
    <template v-if="row.kind === 'option'">
      <slot>
        <UiIcon v-if="row.option.icon" class="option-icon" :icon="row.option.icon" :size="16" />
        <span class="option-text">
          <span class="option-label">{{ row.option.label }}</span>
          <span v-if="row.option.description" class="option-desc">{{ row.option.description }}</span>
        </span>
      </slot>
    </template>
    <template v-else-if="row.kind === 'create'">
      <UiIcon class="option-icon" :icon="Plus" :size="16" />
      <span class="option-text"><span class="option-label">{{ createText }}</span></span>
    </template>
    <span v-else class="option-text"><span class="option-label">{{ row.label }}</span></span>
    <UiIcon v-if="selected" class="option-check" :icon="Check" :size="16" />
  </div>
</template>

<style scoped>
.option {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-menu);
  padding: var(--space-1-5) var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  cursor: pointer;
  user-select: none;
  -webkit-tap-highlight-color: transparent;
}

.option.is-active {
  background: var(--bg-hover);
}

/* Reached by keyboard while the list has focus: an inset ring as on menu items (9.6). */
.option.is-ring {
  outline: 2px solid var(--focus-ring);
  outline-offset: -2px;
}

.option.is-disabled {
  color: var(--text-disabled);
  cursor: default;
}

.option.is-create {
  color: var(--accent-text);
}

.option.is-create .option-icon {
  color: currentColor;
}

.option.is-clear {
  color: var(--text-secondary);
}

.option-icon {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.option-text {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}

.option-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.option-desc {
  overflow: hidden;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.option-check {
  flex-shrink: 0;
  color: var(--accent-text);
}
</style>

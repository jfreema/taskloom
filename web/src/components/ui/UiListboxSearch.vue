<script setup lang="ts">
// Internal to UiListbox: the search field above the list. It is the combobox of the ARIA 1.2
// pattern: focus stays in it while aria-activedescendant names the active option. Keys bubble to
// UiListbox, which handles them (a @keydown on this component lands on its root).
import { ref } from 'vue'
import { Search } from '@lucide/vue'
import UiIcon from './UiIcon.vue'

const query = defineModel<string>({ required: true })

defineProps<{
  listId: string
  activeId?: string
  /** The combobox's name (the listbox's label). */
  label: string
  placeholder: string
}>()

const input = ref<HTMLInputElement | null>(null)

defineExpose({ focus: (): void => input.value?.focus({ preventScroll: true }) })
</script>

<template>
  <div class="search">
    <UiIcon class="search-icon" :icon="Search" :size="16" />
    <input
      ref="input"
      v-model="query"
      class="search-input"
      type="text"
      role="combobox"
      :aria-label="label"
      aria-autocomplete="list"
      aria-expanded="true"
      :aria-controls="listId"
      :aria-activedescendant="activeId"
      :placeholder="placeholder"
      autocomplete="off"
      autocapitalize="off"
      spellcheck="false"
    />
  </div>
</template>

<style scoped>
.search {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  gap: var(--space-2);
  height: 40px;
  padding: 0 var(--space-3);
  border-bottom: 1px solid var(--border-subtle);
}

.search-icon {
  color: var(--text-tertiary);
}

.search-input {
  flex: 1;
  min-width: 0;
  height: 100%;
  padding: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.search-input:focus-visible {
  outline: none;
}

.search-input::placeholder {
  color: var(--text-tertiary);
  opacity: 1;
}
</style>

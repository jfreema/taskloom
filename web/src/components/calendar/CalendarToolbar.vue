<script setup lang="ts">
// The Calendar's toolbar control (spec 8.4), teleported into the toolbar's end: the "Show completed
// tasks" toggle (aria-pressed, Eye or EyeOff), stored in the view settings like the List's.
// On phones it is a round chip like the rest of the toolbar row.
import { computed } from 'vue'
import { Eye, EyeOff } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useListPageContext } from '../../composables/useListPageContext'
import UiIcon from '../ui/UiIcon.vue'

const ctx = useListPageContext()
const { isPhone } = useBreakpoint()

const shown = computed(() => ctx.config.value.showCompleted)

function toggle(): void {
  ctx.config.value.showCompleted = !ctx.config.value.showCompleted
}
</script>

<template>
  <div class="calendar-toolbar" :class="{ 'is-phone': isPhone }">
    <button
      v-tooltip="'Show completed tasks'"
      type="button"
      class="tool"
      :class="{ 'is-active': !shown }"
      aria-label="Show completed tasks"
      :aria-pressed="shown ? 'true' : 'false'"
      @click="toggle"
    >
      <UiIcon :icon="shown ? Eye : EyeOff" :size="16" />
    </button>
  </div>
</template>

<style scoped>
.calendar-toolbar {
  display: flex;
  align-items: center;
}

.tool {
  display: inline-flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: var(--control-sm);
  height: var(--control-sm);
  padding: 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.tool:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}

.tool.is-active {
  color: var(--accent-text);
}

.is-phone .tool {
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-primary);
}

.is-phone .tool.is-active {
  border-color: var(--accent);
  background: var(--bg-selected);
  color: var(--accent-text);
}
</style>

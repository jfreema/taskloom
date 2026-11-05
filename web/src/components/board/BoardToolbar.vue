<script setup lang="ts">
// The Board's end of the view toolbar (8.4): the "Show completed tasks" toggle, which hides the
// Done columns (ViewConfig.showCompleted, saved per user, list and view). On phones it is a chip
// like the rest of the toolbar row.
import { computed } from 'vue'
import { Eye, EyeOff } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useListPageContext } from '../../composables/useListPageContext'
import UiIcon from '../ui/UiIcon.vue'

const ctx = useListPageContext()
const { isPhone } = useBreakpoint()

const shown = computed(() => ctx.config.value.showCompleted)

function toggle(): void {
  ctx.config.value.showCompleted = !shown.value
}
</script>

<template>
  <div class="board-toolbar" :class="{ 'is-phone': isPhone }">
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
.board-toolbar {
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

/* Phones: a round chip, like the rest of the toolbar row. */
.is-phone .tool {
  min-width: var(--control-sm);
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

/* Touch (7.5): 44px wide, and an ::after makes it 44px tall (the toolbar clips that to its row). */
@media (pointer: coarse) {
  .tool,
  .is-phone .tool {
    position: relative;
    width: auto;
    min-width: var(--touch-min);
  }

  .tool::after {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    top: 50%;
    height: max(100%, var(--touch-min));
    transform: translateY(-50%);
  }
}
</style>

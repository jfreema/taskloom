<script setup lang="ts">
// A Board card's "..." menu (8.8), in the card's top-right corner over the end of the title: the
// card variant of the row menu (Open, Copy link, Mark complete, Move to status ›, Move up and down,
// Due date…, Assign…, Move to list…, Delete). It shows while the card is hovered or holds focus or
// the menu is open, and always on touch screens (.reveal-touch). Once shown it takes the card's own
// colour (BoardCard's --card-bg and --card-tint), so revealing it moves nothing. The menu mounts on
// first use (LazyMenu). BoardCard opens it on a right-click by clicking its button.
import { ref } from 'vue'
import { Ellipsis } from '@lucide/vue'
import { useTaskActions } from '../../composables/useTaskActions'
import type { Task } from '../../types'
import LazyMenu from '../fields/LazyMenu.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { useBoard } from './boardContext'

const props = defineProps<{
  task: Task
  /** Focus is in the card: only then is the button in the tab order, so Tab steps from card to card. */
  focusable: boolean
}>()

const board = useBoard()
const actions = useTaskActions()
const open = ref(false)

function items() {
  return actions.rowMenuItems(props.task, {
    variant: 'card',
    canReorder: true,
    siblingIds: () => board.model.siblingIds(props.task.id),
    onPick: (field) => board.pick(props.task.id, field),
  })
}
</script>

<template>
  <div class="corner">
    <LazyMenu v-model:open="open" lazy :items="items" :aria-label="`Actions for ${task.title}`" placement="bottom-end">
      <template #trigger="{ props: t }">
        <UiIconButton
          v-bind="t"
          class="reveal reveal-touch"
          data-card-menu
          :tabindex="focusable ? 0 : -1"
          :icon="Ellipsis"
          :label="`More actions for ${task.title}`"
          size="xs"
        />
      </template>
    </LazyMenu>
  </div>
</template>

<style scoped>
.corner {
  position: absolute;
  top: 6px;
  right: 6px;
  border-radius: var(--radius-sm);
}

.board-card:is(:hover, :active, :focus-visible) .corner,
.board-card:has(:focus-visible) .corner,
.corner:has([aria-expanded="true"]) {
  background: linear-gradient(var(--card-tint), var(--card-tint)), var(--card-bg);
}
</style>

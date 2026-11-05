<script setup lang="ts">
// Internal to UiMenu: the rows of one menu level. The Back row of a submenu shown in place comes
// first, then groups with their labels, separators, swatches rows and items. UiMenuLevel owns the
// keys, focus and submenus; this renders the rows and reports clicks and pointer moves.
import { computed } from 'vue'
import { ArrowLeft } from '@lucide/vue'
import UiColorSwatches from './UiColorSwatches.vue'
import UiMenuItem from './UiMenuItem.vue'
import { BACK_ID, menuBlocks, type MenuActionItem, type MenuItem, type SwatchesItem } from './menu'

const props = defineProps<{
  items: MenuItem[]
  /** Prefix for the rows' element ids. */
  uid: string
  /** Shows a Back row first. */
  back: boolean
  /** The submenu item whose submenu is open. */
  expandedId: string | null
}>()

const emit = defineEmits<{
  activate: [item: MenuItem]
  pick: [item: SwatchesItem, hex: string]
  back: []
  /** The pointer moved over a row (the Back row included). */
  hover: [item: MenuActionItem, e: PointerEvent]
  /** The pointer moved over a swatches row. */
  away: []
}>()

const blocks = computed(() => menuBlocks(props.items))
const hasIcons = computed(() => props.items.some((i) => 'icon' in i && !!i.icon))
const indexOf = computed(() => new Map(props.items.map((i, n) => [i.id, n])))
const backItem: MenuActionItem = { id: BACK_ID, label: 'Back', icon: ArrowLeft, onSelect: () => emit('back') }

function idBase(item: MenuItem): string {
  return `${props.uid}-${indexOf.value.get(item.id) ?? 'back'}`
}
</script>

<template>
  <template v-if="back">
    <UiMenuItem :item="backItem" :id-base="idBase(backItem)" :icon-slot="false" @click="emit('back')" @pointermove="emit('hover', backItem, $event)" />
    <div v-if="blocks.length" role="separator" class="separator" />
  </template>
  <template v-for="block in blocks" :key="block.key">
    <div v-if="block.type === 'separator'" role="separator" class="separator" />
    <div v-else class="block" :role="block.type === 'group' ? 'group' : 'none'" :aria-label="block.type === 'group' ? block.label : undefined">
      <div v-if="block.type === 'group'" class="group-label" aria-hidden="true">{{ block.label }}</div>
      <template v-for="item in block.items" :key="item.id">
        <div v-if="item.kind === 'swatches'" class="swatches" role="none" @pointermove="emit('away')">
          <div class="group-label" aria-hidden="true">{{ item.label }}</div>
          <UiColorSwatches
            :model-value="item.value"
            :aria-label="item.label"
            :size="20"
            menu
            data-menu-stop
            data-menu-swatches
            :data-menu-id="item.id"
            @pick="emit('pick', item, $event)"
          />
        </div>
        <UiMenuItem
          v-else-if="item.kind !== 'separator' && item.kind !== 'label'"
          :item="item"
          :id-base="idBase(item)"
          :icon-slot="hasIcons"
          :expanded="expandedId === item.id"
          @click="emit('activate', item)"
          @pointermove="emit('hover', item, $event)"
        />
      </template>
    </div>
  </template>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
}

.separator {
  height: 1px;
  margin: var(--space-1) calc(var(--space-1) * -1);
  background: var(--border-subtle);
}

.group-label {
  padding: var(--space-1-5) var(--space-2) var(--space-0-5);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.swatches {
  padding: 0 var(--space-1) var(--space-1);
}

.swatches .group-label {
  padding-left: var(--space-1);
}

/* Touch (7.5): the last swatch row's 44px hit areas end above the next item's. */
@media (pointer: coarse) {
  .swatches {
    padding-bottom: var(--space-2);
  }
}
</style>

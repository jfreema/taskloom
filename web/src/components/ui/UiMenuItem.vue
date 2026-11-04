<script setup lang="ts">
// Internal to UiMenu: one item row (menuitem, menuitemcheckbox or menuitemradio) with its icon,
// label, hint, kbd hint, check and submenu chevron. Click and pointer listeners fall through to the
// row. Short hints sit on the right; longer ones (why an item is disabled) go on a second line.
import { computed } from 'vue'
import { Check, ChevronRight } from '@lucide/vue'
import UiIcon from './UiIcon.vue'
import UiKbd from './UiKbd.vue'
import type { MenuActionItem } from './menu'

const props = defineProps<{
  item: MenuActionItem
  /** Prefix for the row's element ids. */
  idBase: string
  /** Another item of the menu has an icon: an empty slot keeps the labels aligned. */
  iconSlot: boolean
  /** Its submenu is open. */
  expanded?: boolean
}>()

const plain = computed(() => (props.item.kind === undefined || props.item.kind === 'item' ? props.item : null))
const hint = computed(() => plain.value?.hint)
const hintBelow = computed(() => (hint.value?.length ?? 0) > 18)
const checked = computed(() => (props.item.kind === 'checkbox' || props.item.kind === 'radio') && props.item.checked)
const role = computed(() =>
  props.item.kind === 'checkbox' ? 'menuitemcheckbox' : props.item.kind === 'radio' ? 'menuitemradio' : 'menuitem',
)
</script>

<template>
  <div
    :id="`${idBase}-item`"
    :role="role"
    class="item focus-inset"
    :class="{
      'is-danger': plain?.danger,
      'is-disabled': item.disabled,
      'is-expanded': expanded,
      'has-hint-below': hintBelow,
    }"
    tabindex="-1"
    data-menu-stop
    :data-menu-id="item.id"
    :data-menu-label="item.label"
    :aria-labelledby="`${idBase}-label`"
    :aria-describedby="hint ? `${idBase}-hint` : undefined"
    :aria-disabled="item.disabled ? 'true' : undefined"
    :aria-checked="item.kind === 'checkbox' || item.kind === 'radio' ? (checked ? 'true' : 'false') : undefined"
    :aria-haspopup="item.kind === 'submenu' ? 'menu' : undefined"
    :aria-expanded="item.kind === 'submenu' ? (expanded ? 'true' : 'false') : undefined"
  >
    <UiIcon v-if="item.icon" class="item-icon" :icon="item.icon" :size="16" />
    <span v-else-if="iconSlot" class="item-icon" aria-hidden="true" />
    <span class="item-text">
      <span :id="`${idBase}-label`" class="item-label">{{ item.label }}</span>
      <span v-if="hint && hintBelow" :id="`${idBase}-hint`" class="item-hint-below">{{ hint }}</span>
    </span>
    <span v-if="hint && !hintBelow" :id="`${idBase}-hint`" class="item-hint">{{ hint }}</span>
    <span v-if="plain?.kbd" class="item-kbd" aria-hidden="true"><UiKbd :keys="plain.kbd" /></span>
    <UiIcon v-if="checked" class="item-check" :icon="Check" :size="16" />
    <UiIcon v-if="item.kind === 'submenu'" class="item-chevron" :icon="ChevronRight" :size="16" />
  </div>
</template>

<style scoped>
.item {
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

.item:focus,
.item.is-expanded {
  background: var(--bg-hover);
}

.item.is-danger {
  color: var(--danger-text);
}

.item.is-danger:focus {
  background: var(--danger-subtle);
}

.item.is-disabled {
  color: var(--text-disabled);
  cursor: default;
}

.item.has-hint-below {
  align-items: flex-start;
}

.item-icon {
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
}

.item.has-hint-below .item-icon,
.item.has-hint-below .item-check,
.item.has-hint-below .item-chevron {
  margin-top: 2px;
}

.item.is-danger .item-icon {
  color: currentColor;
}

.item.is-disabled .item-icon {
  color: var(--text-disabled);
}

.item-text {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
}

.item-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-hint-below {
  max-width: 260px;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

.item-hint {
  flex-shrink: 0;
  margin-left: var(--space-3);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.item-kbd {
  display: inline-flex;
  flex-shrink: 0;
  margin-left: var(--space-3);
}

.item-check {
  flex-shrink: 0;
  color: var(--accent-text);
}

.item-chevron {
  flex-shrink: 0;
  margin-right: calc(var(--space-1) * -1);
  color: var(--text-tertiary);
}

@media (max-width: 639px) {
  .item-kbd {
    display: none;
  }
}
</style>

<script setup lang="ts">
// Keyboard shortcuts help (spec 9.5, 3.6). Placeholder until B5 builds it: a UiDialog bound to
// ui.shortcutsOpen that lists SHORTCUTS by group.
import { computed } from 'vue'
import { SHORTCUTS, type ShortcutInfo } from '../../lib/shortcuts'
import { useUi } from '../../stores/ui'
import UiDialog from '../ui/UiDialog.vue'
import UiKbd from '../ui/UiKbd.vue'

const ui = useUi()

const groups = computed(() => {
  const map = new Map<string, ShortcutInfo[]>()
  for (const s of SHORTCUTS) map.set(s.group, [...(map.get(s.group) ?? []), s])
  return [...map.entries()].map(([name, items]) => ({ name, items }))
})
</script>

<template>
  <UiDialog v-model:open="ui.shortcutsOpen" title="Keyboard shortcuts" size="lg">
    <section v-for="g in groups" :key="g.name" class="group">
      <h3 class="group-title">{{ g.name }}</h3>
      <dl class="items">
        <div v-for="s in g.items" :key="`${g.name}-${s.keys}-${s.label}`" class="item">
          <dt class="label">{{ s.label }}</dt>
          <dd class="keys"><UiKbd :keys="s.keys" /></dd>
        </div>
      </dl>
    </section>
  </UiDialog>
</template>

<style scoped>
.group + .group {
  margin-top: var(--space-5);
}

.group-title {
  margin-bottom: var(--space-1);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
  color: var(--text-tertiary);
}

.items {
  margin: 0;
}

.item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  min-height: var(--row-menu);
  border-bottom: 1px solid var(--border-subtle);
}

.label {
  color: var(--text-primary);
}

.keys {
  margin: 0;
}
</style>

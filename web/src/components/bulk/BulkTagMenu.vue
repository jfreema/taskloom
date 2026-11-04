<script setup lang="ts">
// The bulk bar's Tags button (8.7): "Add tag ›" (the workspace's tags, then "New tag…", Member+)
// and "Remove tag ›" (the tags on any selected task). "New tag…" opens a name field by the button:
// Enter creates the tag (in the next swatch colour) and adds it to every selected task; a name that
// already exists adds that tag instead.
import { computed, h, nextTick, onMounted, ref, shallowRef } from 'vue'
import { Tag as TagIcon } from '@lucide/vue'
import { newId } from '../../api'
import { noop } from '../../lib/async'
import { pluralize } from '../../lib/format'
import { useTags } from '../../stores/tags'
import type { Uuid } from '../../types'
import type { MenuItem } from '../ui/menu'
import UiMenu from '../ui/UiMenu.vue'
import UiPopover from '../ui/UiPopover.vue'
import BulkButton from './BulkButton.vue'
import BulkTagDot from './BulkTagDot.vue'
import { menuGlyphs } from './menuGlyph'
import type { BulkActions } from './useBulkActions'

const props = defineProps<{ actions: BulkActions; iconOnly: boolean }>()

const tags = useTags()
const open = ref(false)
const creating = ref(false)
const draft = ref('')
const wrap = ref<HTMLElement | null>(null)
const anchor = shallowRef<HTMLElement | null>(null)

onMounted(() => void tags.ensure().catch(() => undefined))

/** Each tag's colour as its item's icon. */
const dotOf = menuGlyphs<Uuid>((id, cls) => h(BulkTagDot, { color: tags.get(id)?.color ?? 'var(--text-tertiary)', class: cls }))

/** How many selected tasks carry each tag. */
const counts = computed(() => {
  const out = new Map<Uuid, number>()
  for (const t of props.actions.selected.value) for (const id of t.tagIds) out.set(id, (out.get(id) ?? 0) + 1)
  return out
})

function startCreate(): void {
  draft.value = ''
  anchor.value = wrap.value?.querySelector<HTMLElement>('[data-bulk-item]') ?? null
  // After the menu has closed and handed focus back to the button, so the field keeps it.
  void nextTick(() => requestAnimationFrame(() => (creating.value = true)))
}

function addItems(): MenuItem[] {
  const total = props.actions.selected.value.length
  const out: MenuItem[] = tags.current.map((tag) => {
    const onAll = (counts.value.get(tag.id) ?? 0) >= total
    return {
      id: `add-${tag.id}`,
      label: tag.name,
      icon: dotOf(tag.id),
      hint: onAll ? 'Already on every task' : undefined,
      disabled: onAll,
      onSelect: () => props.actions.addTag(tag.id),
    }
  })
  if (out.length) out.push({ kind: 'separator', id: 'sep-new' })
  out.push({ id: 'new-tag', label: 'New tag…', onSelect: startCreate })
  return out
}

function removeItems(): MenuItem[] {
  return [...counts.value.entries()]
    .map(([id, n]) => ({ id, n, name: tags.get(id)?.name ?? 'Deleted tag' }))
    .sort((a, b) => a.name.localeCompare(b.name))
    .map(({ id, n, name }) => ({
      id: `remove-${id}`,
      label: name,
      icon: dotOf(id),
      hint: `On ${pluralize(n, 'task')}`,
      onSelect: () => props.actions.removeTag(id),
    }))
}

function items(): MenuItem[] {
  return [
    { kind: 'submenu', id: 'add-tag', label: 'Add tag', items: addItems() },
    { kind: 'submenu', id: 'remove-tag', label: 'Remove tag', disabled: counts.value.size === 0, items: removeItems() },
  ]
}

function create(): void {
  const name = draft.value.trim()
  if (!name) return
  const lower = name.toLocaleLowerCase()
  const existing = tags.current.find((t) => t.name.toLocaleLowerCase() === lower)
  let id = existing?.id
  if (!id) {
    id = newId()
    void tags.create({ name, id }).catch(noop)
  }
  props.actions.addTag(id)
  creating.value = false
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229) return
  if (e.key === 'Enter') {
    e.preventDefault()
    create()
  }
}
</script>

<template>
  <span ref="wrap" class="slot">
    <UiMenu v-model:open="open" :items="items" aria-label="Tags" placement="top-start" :min-width="220">
      <template #trigger="{ props: t }">
        <BulkButton v-bind="t" :icon="TagIcon" label="Tags" :icon-only="iconOnly" />
      </template>
    </UiMenu>
  </span>
  <UiPopover v-model:open="creating" :anchor="anchor" placement="top-start" aria-label="New tag" :width="260">
    <div class="new-tag">
      <label class="label" for="bulk-new-tag">New tag</label>
      <input
        id="bulk-new-tag"
        v-model="draft"
        class="field"
        type="text"
        autocomplete="off"
        maxlength="50"
        placeholder="Tag name"
        data-autofocus
        @keydown="onKeydown"
      />
      <p class="hint">Enter to add it to {{ pluralize(actions.selected.value.length, 'task') }}</p>
    </div>
  </UiPopover>
</template>

<style scoped>
.slot {
  display: contents;
}

.new-tag {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
  padding: var(--space-3);
}

.label {
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
}

.field {
  height: var(--control-md);
  padding: 0 var(--space-2);
  border: 1px solid var(--border-control);
  border-radius: var(--radius-md);
  background: var(--bg-surface);
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.field:focus-visible {
  border-color: var(--accent);
  outline: none;
  box-shadow: 0 0 0 3px var(--accent-subtle);
}

.hint {
  margin: 0;
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}
</style>

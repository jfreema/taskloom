<script setup lang="ts">
// Tag picker (7.11): a multiple-select, searchable listbox of the workspace's tags. When
// `canCreate` (Member+ by default), a query with no exact match offers "Create “q”": the tag is
// created with the next swatch colour and a client id, and selected at once (tags.create is
// optimistic). Footer: "Manage tags", a link to /settings/tags.
// Triggers, named "Tags: <names>, change" or "Tags: none, add":
// - 'chips': the List cell, a TagList (max 2 and "+N"), a muted tag icon when empty;
// - 'icon': the tag icon alone; 'field': every tag, wrapping, or "Add" (the task panel);
// - 'chip': a round chip, "Tags" when empty (quick create).
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { Settings, Tag as TagIcon } from '@lucide/vue'
import { newId } from '../../api'
import { useTags } from '../../stores/tags'
import { useWorkspaces } from '../../stores/workspaces'
import type { Tag, Uuid } from '../../types'
import type { ListboxOption } from '../ui/listbox'
import UiIcon from '../ui/UiIcon.vue'
import UiListbox from '../ui/UiListbox.vue'
import UiPopover from '../ui/UiPopover.vue'
import FieldTrigger from './FieldTrigger.vue'
import TagList from './TagList.vue'
import { joinNames, triggerLabel } from './fieldLabel'

defineOptions({ inheritAttrs: false })

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(
  defineProps<{
    modelValue: Uuid[]
    variant?: 'chips' | 'icon' | 'field' | 'chip'
    /** Offers "Create “q”". Defaults to Member+ in the current workspace. */
    canCreate?: boolean
    /** Chips before "+N" in the 'chips' variant. */
    max?: number
    readonly?: boolean
    anchor?: HTMLElement | null
    hideTrigger?: boolean
  }>(),
  { variant: 'chips', canCreate: undefined, max: 2, readonly: false, anchor: null, hideTrigger: false },
)

const emit = defineEmits<{ 'update:modelValue': [ids: Uuid[]] }>()

const tags = useTags()
const workspaces = useWorkspaces()

const creatable = computed(() => props.canCreate ?? workspaces.canEdit())
const known = computed<Tag[]>(() => props.modelValue.map((id) => tags.get(id)).filter((t): t is Tag => !!t))
const label = computed(() => triggerLabel('Tags', joinNames(known.value.map((t) => t.name))))
const chipText = computed(() => {
  const n = known.value.length
  if (n === 0) return 'Tags'
  return n === 1 ? known.value[0].name : `${n} tags`
})
const tip = computed(() => {
  if (props.variant === 'icon') return known.value.length ? `Tags: ${known.value.map((t) => t.name).join(', ')}` : 'Tags'
  return undefined
})

function options(): ListboxOption[] {
  return tags.current.map((t) => ({ value: t.id, label: t.name }))
}

function colorOf(id: string): string {
  return tags.get(id)?.color ?? 'var(--text-tertiary)'
}

function update(next: string | string[] | null): void {
  emit('update:modelValue', Array.isArray(next) ? next : [])
}

function create(query: string): void {
  const name = query.trim()
  if (!name || !creatable.value) return
  const folded = name.toLocaleLowerCase()
  const existing = tags.current.find((t) => t.name.trim().toLocaleLowerCase() === folded)
  if (existing) {
    if (!props.modelValue.includes(existing.id)) emit('update:modelValue', [...props.modelValue, existing.id])
    return
  }
  const id = newId()
  // Optimistic: the tag exists locally at once; a failure toasts and removes it (tags store), and a
  // task edit that names it waits for the create (tags.whenCreated).
  void tags.create({ id, name, color: tags.nextColor() }).catch(() => undefined)
  emit('update:modelValue', [...props.modelValue, id])
}
</script>

<template>
  <span v-if="readonly" v-bind="$attrs" class="static" :class="`static--${variant}`">
    <TagList v-if="known.length && variant !== 'icon'" :tag-ids="modelValue" :max="variant === 'chips' ? max : 99" :wrap="variant === 'field'" />
    <span v-else-if="variant === 'field'" class="muted">None</span>
  </span>
  <UiPopover v-else v-model:open="open" role="listbox" :anchor="anchor" :width="260">
    <template v-if="!hideTrigger" #trigger="{ props: t }">
      <FieldTrigger
        v-bind="{ ...$attrs, ...t }"
        :variant="variant === 'field' ? 'ghost' : variant === 'chip' ? 'chip' : variant === 'icon' ? 'icon' : 'cell'"
        :label="label"
        :empty="known.length === 0"
        :tooltip="tip"
      >
        <UiIcon v-if="variant === 'icon'" :icon="TagIcon" :size="16" />
        <template v-else-if="variant === 'chip'">
          <UiIcon :icon="TagIcon" :size="16" />
          <span class="chip-text">{{ chipText }}</span>
        </template>
        <template v-else-if="known.length">
          <TagList :tag-ids="modelValue" :max="variant === 'chips' ? max : 99" :wrap="variant === 'field'" :size="variant === 'field' ? 'md' : 'sm'" />
        </template>
        <UiIcon v-else-if="variant === 'chips'" :icon="TagIcon" :size="16" />
        <template v-else>
          <UiIcon :icon="TagIcon" :size="16" />
          <span>Add</span>
        </template>
      </FieldTrigger>
    </template>
    <template #default>
      <UiListbox
        :options="options"
        :model-value="modelValue"
        multiple
        :searchable="true"
        :creatable="creatable"
        search-placeholder="Search or create tags"
        aria-label="Tags"
        @update:model-value="update"
        @create="create"
      >
        <template #option="{ option }">
          <span class="dot" :style="{ '--c': colorOf(option.value) }" aria-hidden="true" />
          <span class="option-label">{{ option.label }}</span>
        </template>
        <template #empty="{ query }">
          <span v-if="query">No tags match “{{ query }}”.</span>
          <span v-else>No tags yet.</span>
        </template>
        <template #footer>
          <RouterLink to="/settings/tags" class="footer-action focus-inset" @click="open = false">
            <UiIcon :icon="Settings" :size="16" />
            <span>Manage tags</span>
          </RouterLink>
        </template>
      </UiListbox>
    </template>
  </UiPopover>
</template>

<style scoped>
.static {
  display: inline-flex;
  align-items: center;
  min-width: 0;
  min-height: var(--control-sm);
  padding: 0 var(--space-1-5);
}

.static--field {
  min-height: var(--control-md);
  padding: var(--space-1) var(--space-2);
}

.static--icon {
  display: none;
}

.muted {
  color: var(--text-tertiary);
}

.chip-text {
  min-width: 0;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.dot {
  flex-shrink: 0;
  width: 10px;
  height: 10px;
  margin: 0 var(--space-1);
  border-radius: var(--radius-full);
  background: var(--c);
}

.option-label {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.footer-action {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-height: var(--row-menu);
  padding: var(--space-1-5) var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  text-decoration: none;
}

.footer-action:hover {
  background: var(--bg-hover);
  color: var(--text-primary);
}
</style>

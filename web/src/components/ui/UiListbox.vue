<script setup lang="ts">
// Listbox, the base of every picker (spec 7.10).
// - With search (on above 7 options when 'auto') it follows the ARIA 1.2 combobox pattern: the
//   input is role="combobox" with aria-activedescendant and aria-controls; the list is the
//   listbox. Without search the list itself takes focus.
// - Keys: Up and Down move, Home and End jump, PageUp and PageDown move by 8, Enter picks. In
//   multiple mode Space toggles while the query is empty. Esc clears the query first; with an
//   empty query it emits `escape` and lets the popover close.
// - `options` may be a function: it runs when the list mounts, so closed pickers build nothing.
// - Groups keep first-appearance order. A clear row (clearLabel) comes first, a create row
//   ("Create “q”") last when nothing matches the query exactly.
import { computed, onMounted, ref, shallowRef, useId, watch } from 'vue'
import UiListboxRow from './UiListboxRow.vue'
import UiListboxSearch from './UiListboxSearch.vue'
import { listboxSections, type ListboxOption, type ListboxRow as Row } from './listbox'
import { useListboxNav } from './listboxNav'

const props = withDefaults(
  defineProps<{
    options: ListboxOption[] | (() => ListboxOption[])
    modelValue: string | string[] | null
    multiple?: boolean
    searchable?: boolean | 'auto'
    searchPlaceholder?: string
    emptyText?: string
    creatable?: boolean
    createLabel?: (q: string) => string
    clearLabel?: string
    /** Required in practice; optional in the type so `aria-label="…"` also type-checks (it maps to this prop). */
    ariaLabel?: string
    maxHeight?: number
    filter?: (o: ListboxOption, q: string) => boolean
  }>(),
  {
    multiple: false,
    searchable: 'auto',
    searchPlaceholder: 'Search…',
    emptyText: 'No matches',
    creatable: false,
    createLabel: undefined,
    clearLabel: undefined,
    ariaLabel: 'Options',
    maxHeight: 320,
    filter: undefined,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string | string[] | null]
  select: [value: string]
  create: [query: string]
  escape: []
}>()

defineSlots<{
  option?(p: { option: ListboxOption; selected: boolean; active: boolean }): unknown
  empty?(p: { query: string }): unknown
  footer?(): unknown
}>()

const uid = useId()
const listId = `${uid}-list`
const search = shallowRef<InstanceType<typeof UiListboxSearch> | null>(null)
const listEl = ref<HTMLElement | null>(null)
const query = ref('')
const hasFocus = ref(false)

const all = computed(() => (typeof props.options === 'function' ? props.options() : props.options))
const searchOn = computed(() => props.searchable === true || (props.searchable === 'auto' && all.value.length > 7))

const q = computed(() => query.value.trim())

const selectedSet = computed(() => {
  const v = props.modelValue
  return new Set(Array.isArray(v) ? v : v ? [v] : [])
})

const sections = computed(() =>
  listboxSections(all.value, q.value, { clearLabel: props.clearLabel, creatable: props.creatable, filter: props.filter }),
)

/** Rows the keys move through, in display order (disabled options are skipped). */
const navRows = computed(() =>
  sections.value.flatMap((s) => s.rows).filter((r) => r.kind !== 'option' || !r.option.disabled),
)

const hasRows = computed(() => sections.value.some((s) => s.rows.length > 0))

function rowId(row: Row): string {
  return `${uid}-${row.key}`
}

const nav = useListboxNav({ rows: navRows, list: listEl, rowId })
const { activeKey, byKeyboard, activeId } = nav

function isSelected(row: Row): boolean {
  if (row.kind === 'option') return selectedSet.value.has(row.option.value)
  if (row.kind === 'clear') return selectedSet.value.size === 0
  return false
}

onMounted(() => {
  const i = navRows.value.findIndex((r) => r.kind === 'option' && isSelected(r))
  nav.setActive(Math.max(i, 0), false)
  nav.scrollIntoView()
})

// A new query starts again at the first match.
watch(q, () => nav.setActive(0, byKeyboard.value))

function choose(row: Row): void {
  if (row.kind === 'option' && row.option.disabled) return
  activeKey.value = row.key
  if (row.kind === 'create') {
    emit('create', row.query)
    query.value = ''
    return
  }
  if (row.kind === 'clear') {
    emit('update:modelValue', props.multiple ? [] : null)
    emit('select', '')
    return
  }
  const value = row.option.value
  if (props.multiple) {
    const current = Array.isArray(props.modelValue) ? props.modelValue : props.modelValue ? [props.modelValue] : []
    const next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value]
    emit('update:modelValue', next)
  } else {
    emit('update:modelValue', value)
  }
  emit('select', value)
}

function onKeydown(e: KeyboardEvent): void {
  if (e.isComposing || e.keyCode === 229 || nav.move(e)) return
  const row = nav.activeRow.value
  switch (e.key) {
    case 'Enter':
      if (!row) return
      e.preventDefault()
      choose(row)
      return
    case ' ':
      // Space types into the search; it toggles in multiple mode while the query is empty, and
      // picks when the list itself has focus.
      if (!row || (searchOn.value && (query.value !== '' || !props.multiple))) return
      e.preventDefault()
      choose(row)
      return
    case 'Escape':
      if (query.value) {
        e.preventDefault()
        query.value = ''
        return
      }
      emit('escape')
      return
  }
}

function onFocusOut(e: FocusEvent): void {
  const root = e.currentTarget as HTMLElement
  if (!(e.relatedTarget instanceof Node) || !root.contains(e.relatedTarget)) hasFocus.value = false
}

function focus(): void {
  if (search.value) search.value.focus()
  else listEl.value?.focus({ preventScroll: true })
}

const createText = (query: string) => (props.createLabel ? props.createLabel(query) : `Create “${query}”`)

defineExpose({ focus })
</script>

<template>
  <div class="ui-listbox" @focusin="hasFocus = true" @focusout="onFocusOut">
    <UiListboxSearch
      v-if="searchOn"
      ref="search"
      v-model="query"
      :list-id="listId"
      :active-id="activeId"
      :label="ariaLabel"
      :placeholder="searchPlaceholder"
      @keydown="onKeydown"
    />
    <div
      :id="listId"
      ref="listEl"
      class="list"
      role="listbox"
      :aria-label="ariaLabel"
      :aria-multiselectable="multiple ? 'true' : undefined"
      :aria-activedescendant="searchOn ? undefined : activeId"
      :tabindex="searchOn ? -1 : 0"
      :hidden="!hasRows"
      :style="{ maxHeight: `${maxHeight}px` }"
      @keydown="searchOn ? undefined : onKeydown($event)"
    >
      <div
        v-for="section in sections"
        :key="section.key"
        class="section"
        :role="section.group ? 'group' : 'none'"
        :aria-label="section.group ?? undefined"
      >
        <div v-if="section.group" class="group-label" aria-hidden="true">{{ section.group }}</div>
        <UiListboxRow
          v-for="row in section.rows"
          :id="rowId(row)"
          :key="row.key"
          :row="row"
          :selected="isSelected(row)"
          :active="row.key === activeKey"
          :ring="hasFocus && byKeyboard && row.key === activeKey"
          :create-text="row.kind === 'create' ? createText(row.query) : ''"
          @pointermove="nav.hover(row, $event)"
          @mousedown.prevent
          @click="choose(row)"
        >
          <template v-if="$slots.option && row.kind === 'option'" #default>
            <slot name="option" :option="row.option" :selected="isSelected(row)" :active="row.key === activeKey" />
          </template>
        </UiListboxRow>
      </div>
    </div>
    <div class="empty" aria-live="polite">
      <template v-if="!hasRows">
        <slot name="empty" :query="q">{{ emptyText }}</slot>
      </template>
    </div>
    <div v-if="$slots.footer" class="footer"><slot name="footer" /></div>
  </div>
</template>

<style scoped>
.ui-listbox {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.list {
  flex: 1 1 auto;
  min-height: 0;
  padding: var(--space-1);
  overflow-y: auto;
  overscroll-behavior: contain;
  outline: none;
}

.list:focus-visible {
  outline: none;
}

.section + .section {
  margin-top: var(--space-1);
}

.group-label {
  padding: var(--space-1-5) var(--space-2) var(--space-0-5);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-medium);
}

.empty:not(:empty) {
  padding: var(--space-4) var(--space-3);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  text-align: center;
}

.footer {
  display: flex;
  flex-shrink: 0;
  flex-direction: column;
  padding: var(--space-1);
  border-top: 1px solid var(--border-subtle);
}
</style>

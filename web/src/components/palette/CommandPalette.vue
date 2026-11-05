<script setup lang="ts">
// Command palette (8.11): a UiDialog at the top (640px; a full-height sheet on phones) holding a
// combobox input and a listbox of results, one group per heading. The input keeps focus and points
// at the active option with aria-activedescendant (keys: usePaletteNav); Esc closes.
// `ui.openPalette(q)` prefills the query; it clears when the palette has closed.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { Search } from '@lucide/vue'
import { useBreakpoint } from '../../composables/useBreakpoint'
import { useUi } from '../../stores/ui'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import PaletteHints from './PaletteHints.vue'
import PaletteOptionRow from './PaletteOption.vue'
import { usePaletteItems, type PaletteOption } from './usePaletteItems'
import { usePaletteNav } from './usePaletteNav'
import { usePaletteSearch } from './usePaletteSearch'

const ui = useUi()
const id = useId()
const listboxId = `${id}-listbox`

const open = computed({
  get: () => ui.paletteOpen,
  set: (v: boolean) => {
    ui.paletteOpen = v
  },
})
const query = computed({
  get: () => ui.paletteQuery,
  set: (v: string) => {
    ui.paletteQuery = v
  },
})

/** The full placeholder fits from 385px; narrower phones get a shorter one (the input's name stays the same). */
const { width } = useBreakpoint()
const placeholder = computed(() => (width.value < 385 ? 'Search or type a command…' : 'Search tasks or type a command…'))

/** "Show all N results" was chosen for this query. */
const expanded = ref(false)

function close(): void {
  ui.paletteOpen = false
}

const search = usePaletteSearch({ query, active: open })
const { groups, noResults, trimmed } = usePaletteItems({ query, expanded, search, close })

const options = computed<PaletteOption[]>(() => groups.value.flatMap((g) => g.options))
const hasRecent = computed(() => groups.value.some((g) => g.key === 'recent'))

function domId(o: PaletteOption): string {
  return `${id}-${o.key}`
}

const nav = usePaletteNav({ options, query, domId })
const { active } = nav

watch(trimmed, () => {
  nav.reset()
  expanded.value = false
})

const input = ref<HTMLInputElement | null>(null)

watch(
  open,
  (on) => {
    if (!on) return
    nav.reset()
    expanded.value = false
    // A prefilled query (openPalette(q)): the caret goes to its end.
    void nextTick(() => {
      const el = input.value
      if (el && document.activeElement === el) el.setSelectionRange(el.value.length, el.value.length)
    })
  },
  { flush: 'post' },
)

function onAfterLeave(): void {
  if (ui.paletteOpen) return
  ui.paletteQuery = ''
  expanded.value = false
  nav.reset()
}

/** The result count, read out when the results settle ("No results" is the visible line instead). */
const liveText = computed(() => {
  if (!open.value || !trimmed.value || search.searching.value || noResults.value) return ''
  const n = options.value.filter((o) => o.kind === 'task' || o.kind === 'list' || o.kind === 'space' || o.kind === 'action').length
  return n === 1 ? '1 result' : `${n} results`
})
</script>

<template>
  <UiDialog
    v-model:open="open"
    title="Command palette"
    position="top"
    size="palette"
    sheet="full"
    hide-header
    :close-button="false"
    @after-leave="onAfterLeave"
  >
    <div class="command-palette">
      <div class="field">
        <UiIcon class="field-icon" :icon="Search" :size="20" />
        <input
          ref="input"
          v-model="query"
          class="input"
          type="text"
          role="combobox"
          aria-label="Search tasks or type a command"
          aria-expanded="true"
          aria-autocomplete="list"
          :aria-controls="listboxId"
          :aria-activedescendant="active ? domId(active) : undefined"
          :placeholder="placeholder"
          autocomplete="off"
          autocapitalize="off"
          spellcheck="false"
          enterkeyhint="go"
          maxlength="200"
          @keydown="nav.onKeydown"
        />
        <UiSpinner v-if="search.searching.value && trimmed" class="spinner" :size="16" />
        <UiButton class="cancel" variant="ghost" size="sm" @click="close">Cancel</UiButton>
      </div>

      <div class="results">
        <!-- One live region, always there: the visible "No results" line or the count, read out when the results settle. -->
        <div aria-live="polite">
          <p v-if="noResults" class="empty">No results for “{{ trimmed }}”.</p>
          <span v-else class="sr-only">{{ liveText }}</span>
        </div>
        <div v-if="!trimmed && !hasRecent" class="recent-empty">
          <div class="group-label">Recent</div>
          <p class="recent-hint">Tasks and lists you open show up here.</p>
        </div>
        <div :id="listboxId" class="listbox" role="listbox" aria-label="Results">
          <div
            v-for="g in groups"
            :key="g.key"
            class="group"
            role="group"
            :aria-labelledby="noResults ? undefined : `${id}-group-${g.key}`"
            :aria-label="noResults ? g.label : undefined"
          >
            <div v-if="!noResults" :id="`${id}-group-${g.key}`" class="group-label" role="presentation">{{ g.label }}</div>
            <PaletteOptionRow
              v-for="o in g.options"
              :id="domId(o)"
              :key="o.key"
              :option="o"
              :active="active?.key === o.key"
              :query="trimmed"
              @pointermove="nav.onPointerMove(o)"
              @mousedown.prevent
              @click="nav.onClick(o, $event)"
            />
          </div>
        </div>
      </div>

      <PaletteHints />
    </div>
  </UiDialog>
</template>

<style scoped>
/* The palette draws its own edges, so the dialog body loses its padding (UiDialog has no prop for
   it). The body stays the one scroll container: the input and the key hints stick to its edges, and
   the region axe asks to be reachable by keyboard holds the input. */
:global(.ui-dialog-root .panel > .body:has(> .command-palette)) {
  padding: 0;
  scroll-padding-top: calc(48px + var(--space-2));
  scroll-padding-bottom: calc(36px + var(--space-2));
}

@media (min-width: 640px) {
  :global(.ui-dialog-root .panel > .body:has(> .command-palette)) {
    max-height: min(560px, calc(100dvh - 12vh - var(--space-4) - 2px));
  }
}

.command-palette {
  display: flex;
  flex-direction: column;
  min-height: 100%;
}

.field {
  position: sticky;
  top: 0;
  z-index: 1;
  display: flex;
  flex: none;
  align-items: center;
  gap: var(--space-3);
  height: 48px;
  padding: 0 var(--space-4);
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-raised);
}

.field-icon {
  flex: none;
  color: var(--text-tertiary);
}

.input {
  flex: 1;
  min-width: 0;
  height: 100%;
  padding: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--text-primary);
  font: inherit;
  font-size: var(--fs-lg);
  line-height: var(--lh-lg);
  text-overflow: ellipsis;
}

.input::placeholder {
  color: var(--text-tertiary);
  text-overflow: ellipsis;
}

.spinner {
  flex: none;
}

.cancel {
  display: none;
}

.results {
  flex: 1 0 auto;
  padding: var(--space-2);
}

.empty {
  margin: 0;
  padding: var(--space-3) var(--space-3) var(--space-2);
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  overflow-wrap: anywhere;
}

.group + .group,
.recent-empty + .listbox {
  margin-top: var(--space-2);
}

.recent-hint {
  margin: 0;
  padding: var(--space-1) var(--space-3) var(--space-2);
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.group-label {
  padding: var(--space-2) var(--space-3) var(--space-1);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
}

/* Phones: a full-height sheet with a Cancel button (and no key hints); toasts keep clear of the
   last results. */
@media (max-width: 639px) {
  :global(.ui-dialog-root .panel > .body:has(> .command-palette)) {
    scroll-padding-top: calc(56px + var(--space-2));
    scroll-padding-bottom: 88px;
  }

  .field {
    height: 56px;
    padding: 0 var(--space-2) 0 var(--space-4);
  }

  /* The button's own padding already spaces its label from the input, so the placeholder fits at 390px. */
  .cancel {
    display: inline-flex;
    margin-left: calc(var(--space-2) * -1);
  }
}
</style>

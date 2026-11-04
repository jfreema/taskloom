<script setup lang="ts">
// Date picker, the content of date popovers (spec 7.10): quick picks, each with its resolved day,
// then the month grid (UiMonthGrid). Stacked on phones.
// - A quick pick is named by its label alone ("Tomorrow"); its day ("Thu") is its description.
//   Up and Down move between the quick picks.
// - On open, focus goes to the selected day, else today (the grid marks it with data-autofocus,
//   which UiPopover's default initial focus prefers).
// - Every pick emits update:modelValue (parents close); Esc emits cancel and lets the popover close.
import { computed, ref, useId } from 'vue'
import UiMonthGrid from './UiMonthGrid.vue'
import { useToday } from '../../composables/useToday'
import { quickPicks as buildQuickPicks } from '../../lib/dates'
import type { IsoDate } from '../../types'

const props = withDefaults(
  defineProps<{
    modelValue: IsoDate | null
    min?: IsoDate
    max?: IsoDate
    /** A ringed day, e.g. the start date while picking a due date. */
    highlight?: IsoDate | null
    quickPicks?: boolean
    clearLabel?: string
  }>(),
  { min: undefined, max: undefined, highlight: null, quickPicks: true, clearLabel: 'No date' },
)

const emit = defineEmits<{ 'update:modelValue': [value: IsoDate | null]; cancel: [] }>()

const uid = useId()
const today = useToday()
const picksEl = ref<HTMLElement | null>(null)

const picks = computed(() =>
  buildQuickPicks(today.value).map((p) => (p.date === null ? { ...p, label: props.clearLabel } : p)),
)

// ISO dates compare correctly as strings.
function inRange(d: IsoDate): boolean {
  return (!props.min || d >= props.min) && (!props.max || d <= props.max)
}

function pick(d: IsoDate | null): void {
  if (d && !inRange(d)) return
  emit('update:modelValue', d)
}

function onPickKeydown(e: KeyboardEvent): void {
  if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
  const buttons = picksEl.value ? Array.from(picksEl.value.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')) : []
  const next = buttons[buttons.indexOf(e.currentTarget as HTMLButtonElement) + (e.key === 'ArrowDown' ? 1 : -1)]
  if (!next) return
  e.preventDefault()
  next.focus()
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape' && !e.defaultPrevented) emit('cancel')
}
</script>

<template>
  <div class="ui-date-picker" @keydown="onKeydown">
    <div v-if="quickPicks" ref="picksEl" class="picks" role="group" aria-label="Quick picks">
      <button
        v-for="(p, i) in picks"
        :key="p.label"
        type="button"
        class="pick"
        :class="{ 'is-current': p.date === modelValue, 'is-clear': p.date === null }"
        :disabled="p.date !== null && !inRange(p.date)"
        :aria-describedby="p.hint ? `${uid}-hint-${i}` : undefined"
        @click="pick(p.date)"
        @keydown="onPickKeydown"
      >
        <span class="pick-label">{{ p.label }}</span>
        <span v-if="p.hint" :id="`${uid}-hint-${i}`" class="pick-hint" aria-hidden="true">{{ p.hint }}</span>
      </button>
    </div>
    <UiMonthGrid :model-value="modelValue" :min="min" :max="max" :highlight="highlight" @pick="pick" />
  </div>
</template>

<style scoped>
.ui-date-picker {
  display: flex;
  align-items: stretch;
}

.picks {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  width: 172px;
  padding: var(--space-2) var(--space-1);
  border-right: 1px solid var(--border-subtle);
}

.pick {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  min-height: var(--row-menu);
  padding: var(--space-1-5) var(--space-2);
  border-radius: var(--radius-sm);
  color: var(--text-primary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  transition: background-color var(--dur-1) var(--ease-out);
}

.pick:hover:not(:disabled) {
  background: var(--bg-hover);
}

.pick:focus-visible {
  outline-offset: -2px;
  background: var(--bg-hover);
}

.pick.is-current .pick-label {
  color: var(--accent-text);
  font-weight: var(--fw-medium);
}

.pick.is-clear {
  color: var(--text-secondary);
}

.pick:disabled {
  color: var(--text-disabled);
  cursor: default;
}

.pick-hint {
  color: var(--text-tertiary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

/* Phones: quick picks above the grid, two per row. */
@media (max-width: 639px) {
  .ui-date-picker {
    flex-direction: column;
    align-items: center;
  }

  .picks {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    width: 100%;
    padding: var(--space-2);
    border-right: 0;
    border-bottom: 1px solid var(--border-subtle);
  }
}
</style>

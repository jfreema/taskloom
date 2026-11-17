<script setup lang="ts">
// Custom repeat (Features 1), opened from RepeatPicker's "Custom…": on a schedule or after it's
// done, every N days, weeks, months or years, the weekdays of a weekly rule, the day of a monthly
// one (the due date's day, its nth or last weekday, or the last day of the month), and when it
// ends. The line under the form reads the rule back ("Every 2 weeks on Mon, Fri"). Save emits the
// request; the server fills what is left out from the due date and sets the dates.
import { computed, ref, useId, watch } from 'vue'
import { Calendar } from '@lucide/vue'
import { useToday } from '../../composables/useToday'
import { addDaysIso, formatDateLong, fromIso, weekStartsOn } from '../../lib/dates'
import {
  describeRepeat,
  isLastWeekOfMonth,
  monthPart,
  toRule,
  WEEKDAYS,
  weekdayName,
  weekdayOf,
  weekOfMonth,
} from '../../lib/repeat'
import type { IsoDate, RepeatBasis, RepeatRequest, RepeatRule, RepeatUnit, Weekday } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDatePicker from '../ui/UiDatePicker.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiInput from '../ui/UiInput.vue'
import UiSegmented from '../ui/UiSegmented.vue'
import UiSelect from '../ui/UiSelect.vue'
import FieldTrigger from './FieldTrigger.vue'
import LazyPopover from './LazyPopover.vue'

const open = defineModel<boolean>('open', { required: true })

const props = defineProps<{
  /** The rule to start from, else a weekly rule on the anchor's weekday. */
  rule: RepeatRule | null
  /** The task's due date, else today. */
  anchor: IsoDate
}>()

const emit = defineEmits<{ save: [rule: RepeatRequest] }>()

const uid = useId()
const formId = `${uid}-form`
const daysLabelId = `${uid}-days`
const today = useToday()

const basis = ref<RepeatBasis>('Schedule')
const every = ref('1')
const unit = ref<RepeatUnit>('Week')
const weekdays = ref<Weekday[]>([])
const month = ref('')
const ends = ref<'never' | 'on'>('never')
const until = ref<IsoDate | null>(null)
const untilOpen = ref(false)
const everyError = ref('')
const untilError = ref('')
/** The month option of the rule the dialog opened with, kept in the list even when the anchor wouldn't offer it. */
const ownMonth = ref<string | null>(null)

const BASIS = [
  { value: 'Schedule', label: 'On a schedule' },
  { value: 'Completion', label: "After it's done" },
]
const ENDS = [
  { value: 'never', label: 'Never' },
  { value: 'on', label: 'On a date' },
]
const UNIT_WORDS: Record<RepeatUnit, [string, string]> = {
  Day: ['day', 'days'],
  Week: ['week', 'weeks'],
  Month: ['month', 'months'],
  Year: ['year', 'years'],
}

type MonthChoice = Pick<RepeatRule, 'monthDay' | 'monthWeek' | 'monthWeekday'>

function monthValue(r: MonthChoice): string {
  return r.monthWeek !== null && r.monthWeekday ? `w:${r.monthWeek}:${r.monthWeekday}` : `d:${r.monthDay ?? fromIso(props.anchor).getDate()}`
}

function parseMonth(v: string): MonthChoice {
  const [kind, n, day] = v.split(':')
  return kind === 'w'
    ? { monthDay: null, monthWeek: Number(n), monthWeekday: day as Weekday }
    : { monthDay: Number(n), monthWeek: null, monthWeekday: null }
}

const count = computed(() => (/^\d+$/.test(every.value.trim()) ? Number(every.value.trim()) : NaN))
const unitOptions = computed(() =>
  (Object.keys(UNIT_WORDS) as RepeatUnit[]).map((u) => ({ value: u, label: UNIT_WORDS[u][count.value === 1 ? 0 : 1] })),
)

/** In the order of the browser's week. */
const dayOrder = computed(() => {
  const s = weekStartsOn()
  return [...WEEKDAYS.slice(s), ...WEEKDAYS.slice(0, s)]
})

const monthOptions = computed(() => {
  const a = props.anchor
  const day = weekdayOf(a)
  const choices: MonthChoice[] = [{ monthDay: fromIso(a).getDate(), monthWeek: null, monthWeekday: null }]
  const week = weekOfMonth(a)
  if (week <= 4) choices.push({ monthDay: null, monthWeek: week, monthWeekday: day })
  if (isLastWeekOfMonth(a)) choices.push({ monthDay: null, monthWeek: -1, monthWeekday: day })
  if (fromIso(addDaysIso(a, 1)).getDate() === 1) choices.push({ monthDay: -1, monthWeek: null, monthWeekday: null })
  const options = choices.map((c) => ({ value: monthValue(c), label: `On ${monthPart(c)}` }))
  if (ownMonth.value && !options.some((o) => o.value === ownMonth.value)) {
    options.unshift({ value: ownMonth.value, label: `On ${monthPart(parseMonth(ownMonth.value))}` })
  }
  return options
})

const schedule = computed(() => basis.value === 'Schedule')

watch(
  open,
  (on) => {
    if (!on) return
    const r = props.rule
    basis.value = r?.basis ?? 'Schedule'
    every.value = String(r?.every ?? 1)
    unit.value = r?.unit ?? 'Week'
    weekdays.value = r?.weekdays.length ? [...r.weekdays] : [weekdayOf(props.anchor)]
    ownMonth.value = r?.unit === 'Month' && r.basis === 'Schedule' ? monthValue(r) : null
    month.value = ownMonth.value ?? `d:${fromIso(props.anchor).getDate()}`
    ends.value = r?.until ? 'on' : 'never'
    until.value = r?.until ?? null
    everyError.value = ''
    untilError.value = ''
  },
  { immediate: true },
)

watch(every, () => (everyError.value = ''))
watch(ends, (v) => {
  untilError.value = ''
  if (v === 'on' && !until.value) untilOpen.value = true
})

function toggleDay(d: Weekday): void {
  const has = weekdays.value.includes(d)
  // A weekly rule falls on at least one day.
  if (has && weekdays.value.length === 1) return
  weekdays.value = has ? weekdays.value.filter((x) => x !== d) : [...weekdays.value, d]
}

function pickUntil(d: IsoDate | null): void {
  untilOpen.value = false
  untilError.value = ''
  if (d) until.value = d
  else ends.value = 'never'
}

/** The rule as it stands, or null while the interval isn't a number from 1 to 365. */
const request = computed<RepeatRequest | null>(() => {
  const n = count.value
  if (!Number.isInteger(n) || n < 1 || n > 365) return null
  const r: RepeatRequest = { unit: unit.value, every: n }
  if (!schedule.value) r.basis = 'Completion'
  else if (unit.value === 'Week') r.weekdays = WEEKDAYS.filter((d) => weekdays.value.includes(d))
  else if (unit.value === 'Month') Object.assign(r, Object.fromEntries(Object.entries(parseMonth(month.value)).filter(([, v]) => v !== null)))
  if (ends.value === 'on' && until.value) r.until = until.value
  return r
})

const summary = computed(() => (request.value ? describeRepeat(toRule(request.value), today.value) : ''))

function save(): void {
  const r = request.value
  if (!r) {
    everyError.value = 'Enter a number from 1 to 365.'
    return
  }
  if (ends.value === 'on' && !until.value) {
    untilError.value = 'Choose the last date, or pick Never.'
    return
  }
  open.value = false
  emit('save', r)
}
</script>

<template>
  <UiDialog v-model:open="open" title="Custom repeat" size="sm">
    <form :id="formId" class="form" novalidate @submit.prevent="save">
      <UiSegmented v-model="basis" :options="BASIS" aria-label="Repeat" />

      <UiField :label="schedule ? 'Every' : 'Due again'" :error="everyError">
        <template #default="{ id, describedBy, invalid }">
          <div class="interval">
            <UiInput
              :id="id"
              v-model="every"
              class="every"
              inputmode="numeric"
              :maxlength="3"
              :invalid="invalid"
              :aria-describedby="describedBy"
              select-on-focus
            />
            <UiSelect v-model="unit" class="unit" :options="unitOptions" aria-label="Unit" />
            <span v-if="!schedule" class="after">after it's done</span>
          </div>
        </template>
      </UiField>

      <div v-if="schedule && unit === 'Week'" class="group">
        <span :id="daysLabelId" class="label">On</span>
        <div class="days" role="group" :aria-labelledby="daysLabelId">
          <button
            v-for="d in dayOrder"
            :key="d"
            type="button"
            class="day"
            :class="{ 'is-on': weekdays.includes(d) }"
            :aria-pressed="weekdays.includes(d) ? 'true' : 'false'"
            :aria-label="weekdayName(d, 'long')"
            @click="toggleDay(d)"
          >
            {{ weekdayName(d) }}
          </button>
        </div>
      </div>

      <UiField v-if="schedule && unit === 'Month'" label="On">
        <template #default="{ id, describedBy }">
          <UiSelect :id="id" v-model="month" :options="monthOptions" :aria-describedby="describedBy" />
        </template>
      </UiField>

      <div class="group">
        <span class="label">Ends</span>
        <div class="ends">
          <UiSegmented v-model="ends" :options="ENDS" aria-label="Ends" />
          <LazyPopover v-if="ends === 'on'" v-model:open="untilOpen" aria-label="Last date" width="auto">
            <template #trigger="{ props: t }">
              <FieldTrigger
                v-bind="t"
                variant="chip"
                :label="until ? `Last date: ${formatDateLong(until)}, change` : 'Last date: none, add'"
                :empty="!until"
              >
                <UiIcon :icon="Calendar" :size="16" />
                <span>{{ until ? formatDateLong(until) : 'Choose a date' }}</span>
              </FieldTrigger>
            </template>
            <template #default>
              <UiDatePicker :model-value="until" :min="today" clear-label="Never" @update:model-value="pickUntil" @cancel="untilOpen = false" />
            </template>
          </LazyPopover>
        </div>
        <p v-if="untilError" class="error" role="alert">{{ untilError }}</p>
      </div>

      <p class="summary" aria-live="polite">{{ summary }}</p>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId">Save</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.interval {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.every {
  width: 72px;
}

.unit {
  width: 128px;
}

.after {
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.days {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}

.day {
  min-width: 44px;
  height: var(--control-md);
  padding: 0 var(--space-2);
  border: 1px solid var(--border-default);
  border-radius: var(--radius-full);
  background: var(--bg-surface);
  color: var(--text-secondary);
  font: inherit;
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  cursor: pointer;
  transition:
    background-color var(--dur-1) var(--ease-out),
    border-color var(--dur-1) var(--ease-out),
    color var(--dur-1) var(--ease-out);
}

.day:hover {
  background: var(--bg-hover);
}

.day.is-on {
  border-color: var(--accent);
  background: var(--accent-subtle);
  color: var(--accent-text);
}

.ends {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-2);
}

.error {
  margin: 0;
  color: var(--danger-text);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.summary {
  min-height: var(--lh-md);
  margin: 0;
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}
</style>

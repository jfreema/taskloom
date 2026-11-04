<script setup lang="ts">
// Home's header (8.5): the greeting is the page h1 (28/34 650, cv11), the subline is the long date
// then the counts joined by " · " (zero counts left out; "Nothing due today" when there are none),
// and the scope control "Show" sits on the right, or under the greeting on narrow pages. The
// greeting follows the clock: the hour is read again every minute.
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useToday } from '../../composables/useToday'
import { formatDateLong } from '../../lib/dates'
import { useSession } from '../../stores/session'
import UiSegmented, { type SegmentedOption } from '../ui/UiSegmented.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import { greetingFor, type HomeScope } from './homeSections'

const props = defineProps<{
  dueToday: number
  overdue: number
  /** The data isn't here yet: the counts are unknown. */
  pending: boolean
  /** The load failed: the subline shows only the date. */
  failed?: boolean
  /** The first load has run past 200ms: the subline shows a skeleton. */
  skeleton: boolean
  showScope: boolean
}>()

const scope = defineModel<HomeScope>('scope', { required: true })

const session = useSession()
const today = useToday()

const hour = ref(new Date().getHours())
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  timer = setInterval(() => {
    hour.value = new Date().getHours()
  }, 60_000)
})
onBeforeUnmount(() => clearInterval(timer))

const greeting = computed(() => greetingFor(hour.value, session.user?.displayName))

const subline = computed(() => {
  const counts: string[] = []
  if (props.dueToday > 0) counts.push(`${props.dueToday} due today`)
  if (props.overdue > 0) counts.push(`${props.overdue} overdue`)
  return [formatDateLong(today.value), ...(counts.length ? counts : ['Nothing due today'])].join(' · ')
})

const scopeOptions: SegmentedOption[] = [
  { value: 'mine', label: 'All mine' },
  { value: 'assigned', label: 'Assigned to me' },
]

const scopeModel = computed<string>({
  get: () => scope.value,
  set: (v) => {
    scope.value = v === 'assigned' ? 'assigned' : 'mine'
  },
})
</script>

<template>
  <header class="home-header">
    <div class="titles">
      <h1 class="greeting cv11">{{ greeting }}</h1>
      <p v-if="!pending" class="subline tabular">{{ subline }}</p>
      <p v-else-if="failed" class="subline">{{ formatDateLong(today) }}</p>
      <p v-else class="subline" aria-hidden="true">
        <UiSkeleton v-if="skeleton" width="240px" height="12px" />
      </p>
    </div>
    <UiSegmented v-if="showScope" v-model="scopeModel" class="scope" :options="scopeOptions" aria-label="Show" />
  </header>
</template>

<style scoped>
.home-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-4);
}

.titles {
  min-width: 0;
}

.greeting {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--text-primary);
  font-size: var(--fs-3xl);
  line-height: var(--lh-3xl);
  font-weight: var(--fw-bold);
  letter-spacing: -0.015em;
}

.subline {
  display: flex;
  align-items: center;
  min-height: var(--lh-md);
  margin: var(--space-1) 0 0;
  color: var(--text-secondary);
  font-size: var(--fs-md);
  line-height: var(--lh-md);
}

.scope {
  flex: none;
  margin-top: 1px;
}

/* Narrow pages: the scope control goes under the greeting. */
@container page (max-width: 639px) {
  .home-header {
    flex-direction: column;
    align-items: stretch;
    gap: var(--space-3);
  }

  .greeting {
    font-size: var(--fs-2xl);
    line-height: var(--lh-2xl);
  }

  .scope {
    align-self: flex-start;
    margin-top: 0;
  }
}
</style>

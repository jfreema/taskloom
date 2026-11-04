<script setup lang="ts">
// The provider buttons of the sign-in page (spec 8.14): Microsoft first, then Google, then any
// others alphabetically, each a large secondary block link to /auth/login/{key}?returnUrl=... with
// the provider's mark. The clicked button shows a spinner until the browser leaves the page, and
// drops it again when the page comes back from the back-forward cache.
import { computed, h, onBeforeUnmount, onMounted, ref, type FunctionalComponent } from 'vue'
import type { AuthProvider } from '../../types'
import ProviderMark from './ProviderMark.vue'
import UiButton from '../ui/UiButton.vue'

const props = defineProps<{ providers: AuthProvider[]; returnUrl: string }>()

const pendingKey = ref<string | null>(null)

function rank(key: string): number {
  const k = key.toLowerCase()
  return k === 'microsoft' ? 0 : k === 'google' ? 1 : 2
}

// UiButton takes its icon as a component; each provider gets a stable one that draws its mark at
// 18px. Declaring UiIcon's props keeps them from falling through onto the mark.
const marks = new Map<string, FunctionalComponent>()
function markFor(p: AuthProvider): FunctionalComponent {
  let mark = marks.get(p.key)
  if (!mark) {
    mark = () => h(ProviderMark, { provider: p.key, name: p.displayName, size: 18 })
    mark.props = { size: Number, strokeWidth: [Number, String], fill: String }
    marks.set(p.key, mark)
  }
  return mark
}

const sorted = computed(() =>
  [...props.providers]
    .sort((a, b) => rank(a.key) - rank(b.key) || a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }))
    .map((p) => ({ ...p, href: hrefFor(p.key), mark: markFor(p) })),
)

function hrefFor(key: string): string {
  return `/auth/login/${encodeURIComponent(key)}?returnUrl=${encodeURIComponent(props.returnUrl)}`
}

function onClick(e: MouseEvent, key: string): void {
  // A modified click opens a new tab: this page stays as it is.
  if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return
  pendingKey.value = key
}

function onPageShow(): void {
  pendingKey.value = null
}

onMounted(() => window.addEventListener('pageshow', onPageShow))
onBeforeUnmount(() => window.removeEventListener('pageshow', onPageShow))
</script>

<template>
  <div class="providers">
    <UiButton
      v-for="p in sorted"
      :key="p.key"
      class="provider"
      size="lg"
      block
      :icon="p.mark"
      :loading="pendingKey === p.key"
      :href="p.href"
      @click="onClick($event, p.key)"
    >
      Continue with {{ p.displayName }}
    </UiButton>
  </div>
</template>

<style scoped>
.providers {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.providers .provider {
  gap: var(--space-2);
}
</style>

<script setup lang="ts">
// A sign-in provider's mark (spec 8.14): the official Microsoft and Google marks as inline SVG
// (this file may hold their brand colours, S1 AC1), and a neutral letter tile for any other
// provider. Always decorative: the button's text names the provider.
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    /** The provider key from GET /auth/providers ("microsoft", "google", ...). */
    provider: string
    /** The provider's display name; its first letter fills the tile of an unknown provider. */
    name?: string
    size?: number
  }>(),
  { name: '', size: 18 },
)

const kind = computed(() => {
  const key = props.provider.trim().toLowerCase()
  return key === 'microsoft' || key === 'google' ? key : 'other'
})

const letter = computed(() => {
  const source = (props.name || props.provider).trim()
  return source ? source.charAt(0).toLocaleUpperCase() : '?'
})
</script>

<template>
  <svg
    v-if="kind === 'microsoft'"
    class="provider-mark"
    viewBox="0 0 21 21"
    :width="size"
    :height="size"
    aria-hidden="true"
    focusable="false"
  >
    <rect x="1" y="1" width="9" height="9" fill="#F25022" />
    <rect x="11" y="1" width="9" height="9" fill="#7FBA00" />
    <rect x="1" y="11" width="9" height="9" fill="#00A4EF" />
    <rect x="11" y="11" width="9" height="9" fill="#FFB900" />
  </svg>
  <svg
    v-else-if="kind === 'google'"
    class="provider-mark"
    viewBox="0 0 48 48"
    :width="size"
    :height="size"
    aria-hidden="true"
    focusable="false"
  >
    <path
      fill="#EA4335"
      d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
    />
    <path
      fill="#4285F4"
      d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
    />
    <path
      fill="#FBBC05"
      d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
    />
    <path
      fill="#34A853"
      d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
    />
  </svg>
  <span v-else class="provider-mark provider-tile" :style="{ '--mark': `${size}px` }" aria-hidden="true">{{ letter }}</span>
</template>

<style scoped>
.provider-mark {
  display: block;
  flex-shrink: 0;
}

.provider-tile {
  display: grid;
  place-items: center;
  width: var(--mark);
  height: var(--mark);
  border-radius: var(--radius-xs);
  background: var(--accent-subtle);
  color: var(--accent-text);
  font-size: calc(var(--mark) * 0.62);
  line-height: 1;
  font-weight: var(--fw-bold);
}
</style>

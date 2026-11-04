<script setup lang="ts">
// The "loop-check" mark and the optional "taskloom" wordmark (spec 7.1). Below 20px the mark is
// the plain check (favicon and 16px uses). The mark keeps #0D7469 in both themes; the wordmark
// takes the surrounding text colour (white on the sign-in brand panel).
withDefaults(defineProps<{ size?: 16 | 20 | 24 | 32; wordmark?: boolean }>(), { size: 24, wordmark: false })
</script>

<template>
  <span class="app-logo" :class="{ 'has-wordmark': wordmark }" :style="{ '--logo': `${size}px` }">
    <svg class="mark" viewBox="0 0 32 32" :width="size" :height="size" aria-hidden="true" focusable="false">
      <rect width="32" height="32" rx="9" fill="#0D7469" />
      <path
        v-if="size < 20"
        d="M8.5 16.5 13 21 23.5 10.5"
        fill="none"
        stroke="#fff"
        stroke-width="3"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
      <path
        v-else
        d="M6.5 15.5 L11 20 C12.6 21.6 15.6 21.4 16.4 18.9 C17.1 16.6 15.4 14.4 13.3 14.9 C11.1 15.4 10.9 18.6 12.9 20.3 C14 21.2 15.4 21.1 16.6 20 L25.5 10.5"
        fill="none"
        stroke="#fff"
        stroke-width="2.5"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
    </svg>
    <span v-if="wordmark" class="wordmark">taskloom</span>
  </span>
</template>

<style scoped>
.app-logo {
  display: inline-flex;
  align-items: center;
  gap: calc(var(--logo) * 0.36);
  color: inherit;
}

.mark {
  display: block;
  flex-shrink: 0;
}

.wordmark {
  font-family: var(--font-sans);
  font-size: calc(var(--logo) * 0.75);
  line-height: 1;
  font-weight: var(--fw-bold);
  letter-spacing: -0.02em;
  font-feature-settings: "cv11";
}
</style>

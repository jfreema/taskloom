<script setup lang="ts">
// Highlights ranges of a single-line <input>'s text (Look 1: the parts quick add recognised). It lies
// over the input's text box, transparent to the pointer, and repeats the text invisibly with the
// ranges tinted, in the input's own font and scrolled with it. Its parent must be the input's
// offset parent (a positioned element). Screen readers get the ranges from the chips beside it.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { TextMark } from './textMarks'

const props = defineProps<{
  input: HTMLInputElement | null
  text: string
  marks: readonly TextMark[]
}>()

const box = ref<Record<string, string>>({})
const scroll = ref(0)
let observer: ResizeObserver | null = null
let frame = 0
let bound: HTMLInputElement | null = null

const segments = computed(() => {
  const out: { text: string; mark: boolean }[] = []
  let at = 0
  for (const m of [...props.marks].sort((a, b) => a.start - b.start)) {
    if (m.start < at || m.end <= m.start) continue
    if (m.start > at) out.push({ text: props.text.slice(at, m.start), mark: false })
    out.push({ text: props.text.slice(m.start, m.end), mark: true })
    at = m.end
  }
  if (at < props.text.length) out.push({ text: props.text.slice(at), mark: false })
  return out
})

function measure(): void {
  const el = props.input
  if (!el) return
  const cs = getComputedStyle(el)
  const padL = parseFloat(cs.paddingLeft) || 0
  const padR = parseFloat(cs.paddingRight) || 0
  const borderL = parseFloat(cs.borderLeftWidth) || 0
  box.value = {
    left: `${el.offsetLeft + borderL + padL}px`,
    top: `${el.offsetTop}px`,
    width: `${Math.max(0, el.clientWidth - padL - padR)}px`,
    height: `${el.offsetHeight}px`,
    fontFamily: cs.fontFamily,
    fontSize: cs.fontSize,
    fontWeight: cs.fontWeight,
    fontStyle: cs.fontStyle,
    fontStretch: cs.fontStretch,
    fontFeatureSettings: cs.fontFeatureSettings,
    fontVariationSettings: cs.fontVariationSettings,
    letterSpacing: cs.letterSpacing,
    wordSpacing: cs.wordSpacing,
    textTransform: cs.textTransform,
  }
  scroll.value = el.scrollLeft
}

function sync(): void {
  if (frame) return
  frame = requestAnimationFrame(() => {
    frame = 0
    if (props.input) scroll.value = props.input.scrollLeft
  })
}

const EVENTS = ['scroll', 'input', 'keydown', 'keyup', 'select', 'click', 'focus', 'blur'] as const

function bind(el: HTMLInputElement | null): void {
  if (bound) for (const ev of EVENTS) bound.removeEventListener(ev, sync)
  observer?.disconnect()
  bound = el
  if (!el) return
  for (const ev of EVENTS) el.addEventListener(ev, sync)
  observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => measure()) : null
  observer?.observe(el)
  measure()
}

onMounted(() => bind(props.input))
watch(() => props.input, (el) => bind(el))
watch(
  () => [props.text, props.marks.length],
  () => void nextTick(() => {
    if (!props.marks.length) return
    measure()
  }),
)

onBeforeUnmount(() => {
  bind(null)
  if (frame) cancelAnimationFrame(frame)
})
</script>

<template>
  <span v-if="marks.length" class="ui-text-marks" :style="box" aria-hidden="true">
    <span class="track" :style="{ transform: `translateX(${-scroll}px)` }"
      ><template v-for="(s, i) in segments" :key="i"
        ><mark v-if="s.mark" class="mark">{{ s.text }}</mark
        ><template v-else>{{ s.text }}</template></template
      ></span
    >
  </span>
</template>

<style scoped>
.ui-text-marks {
  position: absolute;
  z-index: 1;
  display: flex;
  align-items: center;
  overflow: hidden;
  color: transparent;
  pointer-events: none;
  user-select: none;
}

.track {
  white-space: pre;
}

.mark {
  border-radius: 3px;
  background: color-mix(in srgb, var(--accent) 16%, transparent);
  box-shadow: inset 0 -1.5px 0 var(--accent);
  color: transparent;
}
</style>

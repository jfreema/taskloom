<script setup lang="ts">
import { nextTick, onMounted, ref } from 'vue'

// A text box that commits on Enter and cancels on Escape (or on blur while empty).
// With `multi`, Enter commits and clears the box so the next item can be typed straight away.
const props = defineProps<{ placeholder?: string; initial?: string; multi?: boolean }>()
const emit = defineEmits<{ submit: [value: string]; cancel: [] }>()
const value = ref(props.initial ?? '')
const el = ref<HTMLInputElement>()
let finished = false

onMounted(async () => {
  await nextTick()
  el.value?.focus()
  el.value?.select()
})

function commit() {
  const v = value.value.trim()
  if (!v) return cancel()
  if (props.multi) {
    value.value = ''
    emit('submit', v)
    return
  }
  if (finished) return
  finished = true
  emit('submit', v)
}

function cancel() {
  if (finished) return
  finished = true
  emit('cancel')
}

function onBlur() {
  // Blur with text keeps it (single mode) or discards an empty box.
  if (value.value.trim()) commit()
  if (!props.multi || !value.value.trim()) cancel()
}
</script>

<template>
  <input
    ref="el"
    v-model="value"
    :placeholder="placeholder"
    @keydown.enter.prevent="commit"
    @keydown.esc.prevent="cancel"
    @blur="onBlur"
  />
</template>

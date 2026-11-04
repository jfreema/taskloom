<script setup lang="ts">
// Command palette (spec 8.11). Placeholder until B5 builds it: a UiDialog bound to ui.paletteOpen
// with the search field.
import { Search } from '@lucide/vue'
import { useUi } from '../../stores/ui'
import UiDialog from '../ui/UiDialog.vue'
import UiInput from '../ui/UiInput.vue'

const ui = useUi()
</script>

<template>
  <UiDialog v-model:open="ui.paletteOpen" title="Command palette" position="top" hide-header>
    <UiInput
      v-model="ui.paletteQuery"
      :icon="Search"
      size="lg"
      placeholder="Search tasks or type a command"
      aria-label="Search tasks or type a command"
    />
  </UiDialog>
</template>

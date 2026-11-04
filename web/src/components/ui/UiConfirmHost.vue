<script setup lang="ts">
// The confirm dialog behind useConfirm() (spec 7.10), mounted once in App.vue. One confirm at a
// time: further calls queue and open once the current one has finished leaving. Focus starts on
// Cancel; with requireText it starts in the text field, and the confirm button stays disabled
// until the typed text matches (trimmed, case-sensitive). Esc, the scrim and Cancel resolve false.
import { computed, ref, watch } from 'vue'
import { useConfirmHost, type PendingConfirm } from '../../composables/useConfirm'
import UiButton from './UiButton.vue'
import UiDialog from './UiDialog.vue'
import UiField from './UiField.vue'
import UiInput from './UiInput.vue'

const { next, settle } = useConfirmHost()

const current = ref<PendingConfirm | null>(null)
const open = ref(false)
const typed = ref('')
let settled = false

const opts = computed(() => current.value?.options ?? null)
const needsText = computed(() => !!opts.value?.requireText)
const matches = computed(() => !needsText.value || typed.value.trim() === (opts.value?.requireText ?? '').trim())

function showNext(): void {
  if (current.value || !next.value) return
  current.value = next.value
  typed.value = ''
  settled = false
  open.value = true
}

watch(next, showNext, { immediate: true })

function finish(ok: boolean): void {
  const c = current.value
  if (!c || settled) return
  if (ok && !matches.value) return
  settled = true
  settle(c.id, ok)
  open.value = false
}

function onEnter(e: KeyboardEvent): void {
  e.preventDefault()
  finish(true)
}

function onAfterLeave(): void {
  current.value = null
  showNext()
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="opts?.title ?? ''"
    :description="opts?.message"
    size="sm"
    role="alertdialog"
    :close-button="false"
    :initial-focus="needsText ? '[data-confirm-text]' : '[data-confirm-cancel]'"
    @close="finish(false)"
    @after-leave="onAfterLeave"
  >
    <template v-if="opts?.requireText" #default>
      <UiField :label="`Type “${opts.requireText}” to confirm`">
        <template #default="{ id }">
          <UiInput
            :id="id"
            v-model="typed"
            data-confirm-text
            autocomplete="off"
            autocapitalize="off"
            :spellcheck="false"
            @enter="onEnter"
          />
        </template>
      </UiField>
    </template>
    <template #footer>
      <UiButton variant="secondary" data-confirm-cancel @click="finish(false)">{{ opts?.cancelLabel ?? 'Cancel' }}</UiButton>
      <UiButton :variant="opts?.danger ? 'danger' : 'primary'" :disabled="!matches" @click="finish(true)">
        {{ opts?.confirmLabel }}
      </UiButton>
    </template>
  </UiDialog>
</template>

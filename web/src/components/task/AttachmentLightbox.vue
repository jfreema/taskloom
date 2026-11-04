<script setup lang="ts">
// The image lightbox (8.10): a large dialog with the image, its file name as the title and a
// Download link.
import { computed, ref, watch } from 'vue'
import { Download } from '@lucide/vue'
import type { Attachment } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'

const props = defineProps<{ attachment: Attachment | null }>()
const emit = defineEmits<{ close: [] }>()

const open = ref(false)
/** Kept while the dialog animates out. */
const shown = ref<Attachment | null>(null)

watch(
  () => props.attachment,
  (a) => {
    if (a) shown.value = a
    open.value = !!a
  },
  { immediate: true },
)

watch(open, (o) => {
  if (!o && props.attachment) emit('close')
})

const url = computed(() => (shown.value ? `/api/attachments/${shown.value.id}` : ''))
</script>

<template>
  <UiDialog v-if="shown" v-model:open="open" :title="shown.fileName" size="lg" @after-leave="shown = attachment">
    <div class="frame">
      <img class="image" :src="url" :alt="shown.fileName" />
    </div>
    <template #footer>
      <UiButton variant="secondary" :icon="Download" :href="url" :download="shown.fileName">Download</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.frame {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 160px;
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  overflow: hidden;
}

.image {
  display: block;
  max-width: 100%;
  max-height: 70dvh;
  object-fit: contain;
}
</style>

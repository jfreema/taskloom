<script setup lang="ts">
// One attachment (8.10): a raster image up to 5 MB is a 96px thumbnail that opens the lightbox;
// anything else is a tile with its name and size that downloads it. Members reveal "Delete <name>".
import { computed } from 'vue'
import { FileText, Trash2 } from '@lucide/vue'
import { formatBytes } from '../../lib/files'
import type { Attachment } from '../../types'
import UiIcon from '../ui/UiIcon.vue'
import UiIconButton from '../ui/UiIconButton.vue'
import { isThumbnail } from './useAttachmentUploads'

const props = defineProps<{ attachment: Attachment; canDelete: boolean }>()
const emit = defineEmits<{ open: []; delete: [] }>()

const url = computed(() => `/api/attachments/${props.attachment.id}`)
const thumbnail = computed(() => isThumbnail(props.attachment))
const size = computed(() => formatBytes(props.attachment.sizeBytes))
</script>

<template>
  <li class="attachment-tile" data-row :class="thumbnail ? 'is-image' : 'is-file'">
    <button v-if="thumbnail" v-tooltip="attachment.fileName" type="button" class="thumb" @click="emit('open')">
      <img :src="url" loading="lazy" :alt="attachment.fileName" />
    </button>
    <a v-else v-tooltip="{ text: attachment.fileName, overflowOnly: true }" class="file" :href="url" :download="attachment.fileName">
      <UiIcon class="file-icon" :icon="FileText" :size="20" />
      <span class="file-text">
        <span class="name truncate">{{ attachment.fileName }}</span>
        <span class="size tabular">{{ size }}</span>
      </span>
    </a>
    <UiIconButton
      v-if="canDelete"
      class="delete reveal"
      :icon="Trash2"
      :label="`Delete ${attachment.fileName}`"
      size="xs"
      variant="secondary"
      @click="emit('delete')"
    />
  </li>
</template>

<style scoped>
.attachment-tile {
  position: relative;
  min-width: 0;
}

.thumb {
  display: block;
  width: 96px;
  height: 96px;
  padding: 0;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  overflow: hidden;
  cursor: zoom-in;
}

.thumb img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.file {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: 52px;
  padding: 0 var(--space-3);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  background: var(--bg-surface);
  color: var(--text-primary);
  text-decoration: none;
  transition: background-color var(--dur-1) var(--ease-out);
}

.file:hover {
  background: var(--bg-hover);
}

.file-icon {
  flex-shrink: 0;
  color: var(--text-tertiary);
}

.file-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
  padding-right: var(--space-5);
}

.name {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
}

.size {
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-tertiary);
}

.delete {
  position: absolute;
  top: var(--space-1);
  right: var(--space-1);
}

.is-file .delete {
  top: 50%;
  transform: translateY(-50%);
}
</style>

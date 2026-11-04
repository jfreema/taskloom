<script setup lang="ts">
// Attachments (8.10): "Attachments", the count and "Upload" (Member+, a multiple file input), then
// the thumbnails, the file tiles and a tile with a spinner for each upload in progress. "Show" in
// the "Added … to attachments" toast scrolls here.
import { computed, ref, useId, watch } from 'vue'
import { Upload } from '@lucide/vue'
import { useDelayedFlag } from '../../composables/useDelayedFlag'
import { useTasks } from '../../stores/tasks'
import type { Attachment, Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import AttachmentLightbox from './AttachmentLightbox.vue'
import AttachmentTile from './AttachmentTile.vue'
import { useTaskDetailContext } from './taskDetailContext'
import { isThumbnail, showAttachmentsRequest, uploadsFor, useAttachmentUploads } from './useAttachmentUploads'

const props = defineProps<{ taskId: Uuid; attachments: Attachment[] | null }>()

const ctx = useTaskDetailContext()
const tasks = useTasks()
const { remove } = useAttachmentUploads()
const headingId = `${useId()}-attachments`

const section = ref<HTMLElement | null>(null)
const heading = ref<HTMLElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const lightbox = ref<Attachment | null>(null)

const uploads = computed(() => uploadsFor(props.taskId))
const images = computed(() => (props.attachments ?? []).filter(isThumbnail))
const files = computed(() => (props.attachments ?? []).filter((a) => !isThumbnail(a)))
/** While the detail loads, the store's count stands in (and no skeleton shows for none). */
const storeCount = computed(() => tasks.get(props.taskId)?.attachmentCount ?? null)
const count = computed(() => props.attachments?.length ?? storeCount.value ?? 0)
const loading = computed(() => props.attachments === null)
const showSkeleton = useDelayedFlag(computed(() => loading.value && storeCount.value !== 0), 200)

function pick(): void {
  fileInput.value?.click()
}

function onPicked(e: Event): void {
  const input = e.target as HTMLInputElement
  const picked = Array.from(input.files ?? [])
  input.value = ''
  if (picked.length) ctx.addFiles(picked)
}

watch(showAttachmentsRequest, (req) => {
  if (!req || req.taskId !== props.taskId) return
  section.value?.scrollIntoView({ block: 'nearest' })
  heading.value?.focus({ preventScroll: true })
})
</script>

<template>
  <section ref="section" class="attachments" :aria-labelledby="headingId">
    <div class="header">
      <component :is="ctx.headingTag.value" :id="headingId" ref="heading" class="section-title" tabindex="-1">Attachments</component>
      <span v-if="count" class="count tabular">{{ count }}</span>
      <template v-if="ctx.canEdit.value">
        <UiButton class="upload" variant="ghost" size="sm" :icon="Upload" @click="pick">Upload</UiButton>
        <input ref="fileInput" type="file" multiple hidden @change="onPicked" />
      </template>
    </div>
    <div v-if="loading && showSkeleton" class="skeleton" aria-hidden="true">
      <UiSkeleton variant="rect" width="96px" height="96px" />
      <UiSkeleton variant="rect" width="200px" height="52px" />
    </div>
    <template v-else-if="!loading">
      <ul v-if="images.length" class="images">
        <AttachmentTile
          v-for="a in images"
          :key="a.id"
          :attachment="a"
          :can-delete="ctx.canEdit.value"
          @open="lightbox = a"
          @delete="remove(a)"
        />
      </ul>
      <ul v-if="files.length || uploads.length" class="files">
        <AttachmentTile v-for="a in files" :key="a.id" :attachment="a" :can-delete="ctx.canEdit.value" @delete="remove(a)" />
        <li v-for="u in uploads" :key="u.id" class="uploading" aria-busy="true">
          <UiSpinner :size="16" />
          <span class="name truncate">{{ u.file.name }}</span>
          <span class="sr-only">Uploading</span>
        </li>
      </ul>
      <p v-if="!count && !uploads.length && !ctx.canEdit.value" class="none">No attachments</p>
    </template>
    <AttachmentLightbox :attachment="lightbox" @close="lightbox = null" />
  </section>
</template>

<style scoped>
.attachments {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.header {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--control-sm);
}

.section-title {
  margin: 0;
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
  outline: none;
}

.count {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}

.upload {
  margin-left: auto;
}

.images {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.files {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: var(--space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.uploading {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  height: 52px;
  padding: 0 var(--space-3);
  border: 1px dashed var(--border-default);
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.name {
  min-width: 0;
}

.skeleton {
  display: flex;
  gap: var(--space-2);
}

.none {
  margin: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  color: var(--text-tertiary);
}
</style>

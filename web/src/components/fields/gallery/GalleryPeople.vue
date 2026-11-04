<script setup lang="ts">
// /_ui: AssigneePicker (stack, field, chip, read-only; one value holds a person who is no longer a
// member, so the picker shows "Former member") and the tag components: TagChip, TagList ("+N")
// and TagPicker (chips, icon, field, chip; creating a tag selects it at once).
import { computed, ref, watch } from 'vue'
import { SWATCHES } from '../../../lib/colors'
import { useMembers } from '../../../stores/members'
import { useSession } from '../../../stores/session'
import { useTags } from '../../../stores/tags'
import type { Uuid } from '../../../types'
import GalleryCard from '../../ui/gallery/GalleryCard.vue'
import AssigneePicker from '../AssigneePicker.vue'
import TagChip from '../TagChip.vue'
import TagList from '../TagList.vue'
import TagPicker from '../TagPicker.vue'

/** Nobody in the workspace has this id: it stands for someone who left. */
const FORMER_ID = '00000000-0000-4000-8000-0000000000f1'

const session = useSession()
const members = useMembers()
const tags = useTags()

const meId = computed(() => session.user?.id ?? '')
const others = computed(() => members.current.filter((m) => m.userId !== meId.value).map((m) => m.userId))

const stack = ref<Uuid[]>([])
const field = ref<Uuid[]>([])
const chip = ref<Uuid[]>([])

const allTagIds = computed(() => tags.current.map((t) => t.id))
const chipsTags = ref<Uuid[]>([])
const fieldTags = ref<Uuid[]>([])
const iconTags = ref<Uuid[]>([])
const chipTags = ref<Uuid[]>([])
const removable = ref<Uuid[]>([])

// Starting values once the members and tags have loaded (this page loads them itself).
let peopleSet = false
watch(
  () => [meId.value, others.value.length] as const,
  ([me, n]) => {
    if (peopleSet || !me || !n) return
    peopleSet = true
    stack.value = [me, ...others.value.slice(0, 1)]
    field.value = [me, FORMER_ID]
  },
  { immediate: true },
)
let tagsSet = false
watch(
  () => tags.current.length,
  (n) => {
    if (tagsSet || !n) return
    tagsSet = true
    const ids = tags.current.map((t) => t.id)
    chipsTags.value = ids.slice(0, 1)
    fieldTags.value = ids.slice(0, 2)
    removable.value = ids.slice(0, 3)
  },
  { immediate: true },
)

const longTag = { name: 'A tag with a much longer name than fits', color: SWATCHES[1].hex }
const unknownTag = { name: '?', color: SWATCHES[0].hex }

const names = (ids: Uuid[]) => ids.map((id) => members.nameOf(id)).join(', ') || 'nobody'
const tagNames = (ids: Uuid[]) => ids.map((id) => tags.get(id)?.name ?? '?').join(', ') || 'none'
</script>

<template>
  <GalleryCard title="People and tags" wide>
    <div class="row">
      <AssigneePicker v-model="stack" />
      <AssigneePicker v-model="field" variant="field" />
      <AssigneePicker v-model="chip" variant="chip" />
      <AssigneePicker :model-value="stack" readonly />
    </div>
    <p class="small muted">Stack: {{ names(stack) }} · Field: {{ names(field) }} · Chip: {{ names(chip) }}</p>
    <div class="row">
      <TagChip v-for="t in tags.current.slice(0, 2)" :key="t.id" :tag="t" />
      <TagChip v-for="t in tags.current.slice(0, 2)" :key="`md-${t.id}`" :tag="t" size="md" />
      <TagChip :tag="longTag" />
      <TagChip
        v-for="id in removable"
        :key="`rm-${id}`"
        :tag="tags.get(id) ?? unknownTag"
        removable
        @remove="removable = removable.filter((x) => x !== id)"
      />
      <TagList :tag-ids="allTagIds" :max="2" />
    </div>
    <div class="row">
      <TagPicker v-model="chipsTags" />
      <TagPicker v-model="iconTags" variant="icon" />
      <TagPicker v-model="fieldTags" variant="field" />
      <TagPicker v-model="chipTags" variant="chip" />
      <TagPicker :model-value="fieldTags" variant="field" readonly />
    </div>
    <p class="small muted">
      Chips: {{ tagNames(chipsTags) }} · Icon: {{ tagNames(iconTags) }} · Field: {{ tagNames(fieldTags) }} · Chip: {{ tagNames(chipTags) }}
    </p>
  </GalleryCard>
</template>

<style scoped src="../../ui/gallery/gallery.css"></style>

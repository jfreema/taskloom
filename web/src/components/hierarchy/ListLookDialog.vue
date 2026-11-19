<script setup lang="ts">
// Icon and colour (Look 2), opened by ui.openListLook(listId) from the list menus: a preview of the
// list's row, the 13 colours and the list icons. "Save" sends only what changed through
// updateList, which is optimistic and rolls back and toasts on failure. "Use plain icon" puts the
// grey list icon back.
import { computed, ref, useId, watch } from 'vue'
import { DEFAULT_LIST_ICON } from '../../lib/listIcons'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Uuid } from '../../types'
import ListGlyph from '../fields/ListGlyph.vue'
import UiButton from '../ui/UiButton.vue'
import UiColorSwatches from '../ui/UiColorSwatches.vue'
import UiDialog from '../ui/UiDialog.vue'
import ListIconGrid from './ListIconGrid.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const formId = `${useId()}-form`
const colourLabelId = `${useId()}-colour`
const iconLabelId = `${useId()}-icon`

const listId = ref<Uuid | null>(null)
const listName = ref('')
const color = ref<string | null>(null)
const icon = ref(DEFAULT_LIST_ICON)

const found = computed(() => (listId.value ? workspaces.findList(listId.value) : null))

const open = useRequestedDialog(
  () => ui.listLook,
  () => {
    ui.listLook = null
  },
  (r) => {
    const f = workspaces.findList(r.listId)
    listId.value = r.listId
    listName.value = f?.list.name ?? 'list'
    color.value = f?.list.color ?? null
    icon.value = f?.list.icon ?? DEFAULT_LIST_ICON
  },
)

// The list was deleted, or the role was lost.
watch(
  () => [!!found.value, workspaces.canEdit(found.value?.workspace.id)] as const,
  ([exists, canEdit]) => {
    if (open.value && (!exists || !canEdit)) open.value = false
  },
)

const title = computed(() => `Icon and colour for “${listName.value}”`)
const swatch = computed<string>({
  get: () => color.value ?? '',
  set: (hex) => (color.value = hex || null),
})
const preview = computed(() => ({ color: color.value, icon: icon.value === DEFAULT_LIST_ICON ? null : icon.value }))
const plain = computed(() => !color.value && icon.value === DEFAULT_LIST_ICON)

function usePlain(): void {
  color.value = null
  icon.value = DEFAULT_LIST_ICON
}

function save(): void {
  const f = found.value
  if (!f) return
  const changes: { color?: string; icon?: string } = {}
  if ((f.list.color ?? null) !== color.value) changes.color = color.value ?? ''
  if ((f.list.icon ?? DEFAULT_LIST_ICON) !== icon.value) changes.icon = icon.value === DEFAULT_LIST_ICON ? '' : icon.value
  if (changes.color !== undefined || changes.icon !== undefined) void workspaces.updateList(f.list.id, changes).catch(() => undefined)
  open.value = false
}
</script>

<template>
  <UiDialog v-model:open="open" :title="title" description="Shown next to the list's name in the sidebar and wherever lists are listed." size="sm">
    <form :id="formId" class="look" novalidate @submit.prevent="save">
      <div class="preview" aria-hidden="true">
        <ListGlyph :list="preview" :size="20" />
        <span class="preview-name truncate">{{ listName }}</span>
      </div>
      <div class="field">
        <span :id="colourLabelId" class="label">Colour</span>
        <UiColorSwatches v-model="swatch" aria-label="Colour" :size="24" />
      </div>
      <div class="field">
        <span :id="iconLabelId" class="label">Icon</span>
        <ListIconGrid v-model="icon" :color="color" aria-label="Icon" />
      </div>
    </form>
    <template #footer>
      <UiButton class="plain" variant="ghost" :disabled="plain" @click="usePlain">Use plain icon</UiButton>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId">Save</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.look {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.preview {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--row-sidebar);
  padding: var(--space-1-5) var(--space-3);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
}

.preview-name {
  min-width: 0;
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.plain {
  margin-right: auto;
}
</style>

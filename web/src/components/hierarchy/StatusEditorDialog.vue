<script setup lang="ts">
// The status editor (8.13), opened by ui.openStatusEditor(listId) from the list menus, the status
// picker's footer and the board. Three sections, one per type (Not started, Active, Done); every
// change applies at once through the workspaces store, which is optimistic and toasts and rolls
// back when the server refuses. No dragging: Alt+Up/Down or the row menu put the status at the
// midpoint between its new neighbours in its section, a move to another section puts it last
// there, and each move is announced ("Review moved up, 2 of 3"). The guards mirror the server
// (statusRules.ts). Up and Down move between rows, Home and End jump to the first and last.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useAnnouncer } from '../../composables/useAnnouncer'
import { nextSwatch } from '../../lib/colors'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import type { Status, StatusCategory, Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import StatusEditorAdd from './StatusEditorAdd.vue'
import StatusEditorRow from './StatusEditorRow.vue'
import { CATEGORIES, endOf, moveBlocked, movedText, sectionLabel, sectionsOf, stepMove } from './statusRules'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const tasks = useTasks()
const announce = useAnnouncer()
const uid = useId()

const listId = ref<Uuid | null>(null)
/** Kept while the dialog fades out, so the title doesn't change under it. */
const listName = ref('')
const editor = ref<HTMLElement | null>(null)
/**
 * The status the delete dialog was opened for, and the row to focus if it goes: the next row in
 * display order, else the previous one. Its own row (and the menu button focus would return to) is
 * gone by then, and the page under the editor is inert.
 */
let deleting: { id: Uuid; name: string; next: Uuid | null } | null = null

const open = useRequestedDialog(
  () => ui.statusEditor,
  () => {
    ui.statusEditor = null
  },
  (r) => {
    listId.value = r.listId
    listName.value = workspaces.findList(r.listId)?.list.name ?? ''
    deleting = null
  },
)

const found = computed(() => (listId.value ? workspaces.findList(listId.value) : null))

watch(
  () => found.value?.list.name,
  (name) => {
    if (name) listName.value = name
  },
)

// The list was deleted, or the role in its workspace was lost: close.
watch(
  () => !!found.value && workspaces.canEdit(found.value.workspace.id),
  (ok) => {
    if (!ok) open.value = false
  },
)

const statuses = computed<readonly Status[]>(() => (listId.value ? workspaces.statusesFor(listId.value) : []))
const sections = computed(() => sectionsOf(statuses.value))

/** Tasks per status, from the loaded tasks; null while the list isn't loaded. */
const counts = computed<Map<Uuid, number> | null>(() => {
  const id = listId.value
  if (!id || (tasks.listMeta[id]?.loadedAt ?? 0) === 0) return null
  const m = new Map<Uuid, number>()
  for (const t of tasks.inList(id)) m.set(t.statusId, (m.get(t.statusId) ?? 0) + 1)
  return m
})

function countOf(id: Uuid): number | null {
  return counts.value ? (counts.value.get(id) ?? 0) : null
}

function headingId(c: StatusCategory): string {
  return `${uid}-${c}`
}

// ---- Focus ----

/**
 * What in a row had focus, so it can come back after a move: the row, or a button by its
 * aria-haspopup. A move chosen in the row menu runs while focus is still in the menu; it belongs
 * on the menu button, where the menu returns it.
 */
function focusedPart(): string {
  const active = document.activeElement
  if (!(active instanceof HTMLElement) || active.matches('[data-status-row]')) return 'row'
  if (active.closest('[role="menu"]')) return 'menu'
  return active.getAttribute('aria-haspopup') ?? 'row'
}

/** After the DOM update: focus the status's row again (it may have moved, or been rebuilt in another section). */
function refocus(id: Uuid, part = 'row'): void {
  void nextTick(() => {
    const row = editor.value?.querySelector<HTMLElement>(`[data-status-id="${CSS.escape(id)}"]`)
    if (!row) return
    const target = part === 'row' ? row : (row.querySelector<HTMLElement>(`[aria-haspopup="${part}"]`) ?? row)
    target.focus({ preventScroll: true })
    row.scrollIntoView({ block: 'nearest' })
  })
}

function onKeydown(e: KeyboardEvent): void {
  const row = e.target instanceof HTMLElement && e.target.matches('[data-status-row]') ? e.target : null
  if (!row || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey) return
  const rows = Array.from(editor.value?.querySelectorAll<HTMLElement>('[data-status-row]') ?? [])
  const i = rows.indexOf(row)
  let next: HTMLElement | undefined
  if (e.key === 'ArrowDown') next = rows[i + 1]
  else if (e.key === 'ArrowUp') next = rows[i - 1]
  else if (e.key === 'Home') next = rows[0]
  else if (e.key === 'End') next = rows[rows.length - 1]
  else return
  e.preventDefault()
  next?.focus()
  next?.scrollIntoView({ block: 'nearest' })
}

// ---- Changes ----

function step(s: Status, dir: 'up' | 'down'): void {
  const move = stepMove(s, dir, statuses.value)
  if (!move) {
    const section = sectionLabel(s.category)
    announce(dir === 'up' ? `${s.name} is already first in ${section}` : `${s.name} is already last in ${section}`)
    return
  }
  const part = focusedPart()
  void workspaces.updateStatus(s.id, { position: move.position }).catch(() => undefined)
  announce(movedText(s.name, dir, move))
  refocus(s.id, part)
}

function moveTo(s: Status, to: StatusCategory): void {
  if (moveBlocked(s, to, statuses.value)) return
  const part = focusedPart()
  const position = endOf(to, statuses.value, s.id)
  const of = sections.value[to].length + 1
  void workspaces.updateStatus(s.id, { category: to, position }).catch(() => undefined)
  announce(`${s.name} moved to ${sectionLabel(to)}, ${of} of ${of}`)
  refocus(s.id, part)
}

function rename(s: Status, name: string): void {
  void workspaces.updateStatus(s.id, { name }).catch(() => undefined)
}

function recolour(s: Status, color: string): void {
  void workspaces.updateStatus(s.id, { color }).catch(() => undefined)
}

function add(category: StatusCategory, name: string): void {
  const id = listId.value
  if (!id) return
  const byPosition = workspaces.statusesByPosition(id)
  const color = nextSwatch(byPosition[byPosition.length - 1]?.color)
  void workspaces.createStatus(id, { name, color, category }).catch(() => undefined)
  announce(`${name} added to ${sectionLabel(category)}`)
}

function remove(s: Status): void {
  const all = statuses.value
  const i = all.findIndex((x) => x.id === s.id)
  deleting = { id: s.id, name: s.name, next: (all[i + 1] ?? all[i - 1])?.id ?? null }
  ui.openDeleteStatus(s.id)
}

watch(
  () => ui.deleteStatus,
  (now, was) => {
    const d = deleting
    if (now || !was || !d || was.statusId !== d.id) return
    deleting = null
    // Cancelled: the layer stack already returned focus to the row's menu button.
    if (workspaces.statusById(d.id) || !open.value) return
    announce(`${d.name} deleted`)
    if (d.next) refocus(d.next)
  },
)
</script>

<template>
  <UiDialog
    v-model:open="open"
    :title="`Statuses · ${listName}`"
    description="Each status has a type: Not started, Active or Done. Tasks in a Done status count as complete."
    size="lg"
  >
    <div ref="editor" class="editor" @keydown="onKeydown">
      <section v-for="c in CATEGORIES" :key="c" class="section" role="group" :aria-labelledby="headingId(c)">
        <h3 :id="headingId(c)" class="section-title">{{ sectionLabel(c) }}</h3>
        <ul v-if="sections[c].length" class="rows">
          <StatusEditorRow
            v-for="s in sections[c]"
            :key="s.id"
            :status="s"
            :statuses="statuses"
            :count="countOf(s.id)"
            @step="step(s, $event)"
            @move-to="moveTo(s, $event)"
            @rename="rename(s, $event)"
            @recolour="recolour(s, $event)"
            @remove="remove(s)"
          />
        </ul>
        <p v-else class="empty">No statuses of this type yet.</p>
        <StatusEditorAdd :category="c" @add="add(c, $event)" />
      </section>
    </div>
    <template #footer>
      <UiButton variant="primary" @click="open = false">Done</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.editor {
  display: flex;
  flex-direction: column;
  gap: var(--space-5);
}

.section-title {
  margin-bottom: var(--space-1-5);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-semibold);
  color: var(--text-secondary);
}

.rows {
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-lg);
  overflow: hidden;
}

.empty {
  padding: var(--space-3);
  border: 1px dashed var(--border-default);
  border-radius: var(--radius-lg);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-tertiary);
}
</style>

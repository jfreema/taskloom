<script setup lang="ts">
// Keyboard shortcuts (9.5): every row of SHORTCUTS, by group, in two columns on wide screens.
// Entries that share a group and label are one row whose keys read "Delete or Mod+Backspace"; a
// label ending in "(also Space)" shows that key as an alternative too. When single-key shortcuts
// are off (Settings › Appearance), a note says so.
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { Info } from '@lucide/vue'
import { shortcutsEnabled } from '../../composables/useShortcut'
import { noop } from '../../lib/async'
import { SHORTCUTS, type ShortcutGroup } from '../../lib/shortcuts'
import { useUi } from '../../stores/ui'
import UiDialog from '../ui/UiDialog.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiKbd from '../ui/UiKbd.vue'

const ui = useUi()
const router = useRouter()

const open = computed({
  get: () => ui.shortcutsOpen,
  set: (v: boolean) => {
    ui.shortcutsOpen = v
  },
})

interface Row {
  label: string
  keys: string[]
}

/** Spoken key names that labels mention as alternatives, as UiKbd key strings. */
const ALSO: Record<string, string> = {
  'down arrow': 'ArrowDown',
  'up arrow': 'ArrowUp',
  'left arrow': 'ArrowLeft',
  'right arrow': 'ArrowRight',
  space: 'Space',
  enter: 'Enter',
}

const groups = computed<{ name: ShortcutGroup; rows: Row[] }[]>(() => {
  const out = new Map<ShortcutGroup, Row[]>()
  for (const s of SHORTCUTS) {
    let label = s.label
    const keys = [s.keys]
    const also = /\s*\(also ([^)]+)\)$/.exec(label)
    if (also && ALSO[also[1].toLowerCase()]) {
      label = label.slice(0, also.index)
      keys.push(ALSO[also[1].toLowerCase()])
    }
    const rows = out.get(s.group) ?? []
    const row = rows.find((r) => r.label === label)
    if (row) {
      for (const k of keys) if (!row.keys.includes(k)) row.keys.push(k)
    } else {
      rows.push({ label, keys })
    }
    out.set(s.group, rows)
  }
  return [...out.entries()].map(([name, rows]) => ({ name, rows }))
})

function openSettings(): void {
  ui.shortcutsOpen = false
  void router.push('/settings/appearance').catch(noop)
}
</script>

<template>
  <UiDialog v-model:open="open" title="Keyboard shortcuts" size="lg">
    <!-- The list scrolls in its own region, which takes focus first so arrow keys scroll it. -->
    <div class="shortcuts-scroll" tabindex="0" role="region" aria-label="Shortcut list">
      <p v-if="!shortcutsEnabled" class="note">
        <UiIcon class="note-icon" :icon="Info" :size="16" />
        <span>
          Single-key shortcuts are off.
          <button type="button" class="link" @click="openSettings">Turn them on in Settings</button>
        </span>
      </p>
      <div class="columns">
        <section v-for="g in groups" :key="g.name" class="group">
          <h3 class="group-title">{{ g.name }}</h3>
          <dl class="rows">
            <div v-for="r in g.rows" :key="r.label" class="row">
              <dt class="label">{{ r.label }}</dt>
              <dd class="keys">
                <template v-for="(k, i) in r.keys" :key="k">
                  <span v-if="i > 0" class="or">or</span>
                  <UiKbd :keys="k" />
                </template>
              </dd>
            </div>
          </dl>
        </section>
      </div>
    </div>
  </UiDialog>
</template>

<style scoped>
/* The dialog body doesn't scroll; the region inside it does (UiDialog has no prop for this). The
   body's 8px top padding holds the region's focus ring, which the body would otherwise clip. */
:global(.ui-dialog-root .panel > .body:has(> .shortcuts-scroll)) {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  padding-top: var(--space-2);
  padding-right: 0;
  padding-bottom: 0;
}

/* The text keeps the body's left edge; the region reaches 8px past it on the left and top and stops
   8px short of the panel's right and bottom edges, so its focus ring clears the text and the corners. */
.shortcuts-scroll {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
  margin: calc(-1 * var(--space-2)) var(--space-2) var(--space-2) calc(-1 * var(--space-2));
  padding: var(--space-2) var(--space-4) var(--space-3) var(--space-2);
  border-radius: var(--radius-md);
}

.shortcuts-scroll:focus-visible {
  outline-offset: -2px;
}

.note {
  display: flex;
  align-items: flex-start;
  gap: var(--space-2);
  margin: 0 0 var(--space-4);
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  color: var(--text-secondary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.note-icon {
  flex: none;
  margin-top: 1px;
  color: var(--text-tertiary);
}

.link {
  padding: 0;
  border: 0;
  background: none;
  color: var(--accent-text);
  font: inherit;
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
}

.columns {
  column-count: 2;
  column-gap: var(--space-8);
}

.group {
  break-inside: avoid;
  padding-bottom: var(--space-5);
}

.group-title {
  margin: 0 0 var(--space-1);
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  font-weight: var(--fw-semibold);
}

.rows {
  margin: 0;
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  min-height: var(--control-sm);
  padding: var(--space-1) 0;
  border-bottom: 1px solid var(--border-subtle);
}

.row:last-child {
  border-bottom: 0;
}

.label {
  min-width: 0;
  color: var(--text-primary);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
}

.keys {
  display: inline-flex;
  flex: none;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  gap: var(--space-1);
  margin: 0;
}

.or {
  color: var(--text-tertiary);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
}

@media (max-width: 720px) {
  .columns {
    column-count: 1;
  }
}

@media (max-width: 639px) {
  .shortcuts-scroll {
    padding-right: var(--space-2);
  }
}
</style>

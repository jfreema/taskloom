<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useWorkspaces } from '../stores/workspaces'
import { useSession } from '../stores/session'
import Avatar from './Avatar.vue'
import InlineInput from './InlineInput.vue'
import type { Space } from '../types'

const ws = useWorkspaces()
const session = useSession()
const route = useRoute()
const router = useRouter()

const showWorkspaces = ref(false)
const addingWorkspace = ref(false)
const addingSpace = ref(false)
const addingListTo = ref<string | null>(null)
const renaming = ref<string | null>(null)
const collapsed = ref<Record<string, boolean>>({})
const menuFor = ref<string | null>(null)

const canManage = () => ws.current && ws.current.role !== 'Guest'

async function addSpace(name: string) {
  addingSpace.value = false
  await ws.createSpace(name)
}

async function addList(space: Space, name: string) {
  addingListTo.value = null
  collapsed.value[space.id] = false
  const list = await ws.createList(space, name)
  if (list) router.push({ name: 'list', params: { listId: list.id, view: 'list' } })
}

async function addWorkspace(name: string) {
  addingWorkspace.value = false
  showWorkspaces.value = false
  await ws.createWorkspace(name)
  router.push('/')
}

function selectWorkspace(id: string) {
  ws.select(id)
  showWorkspaces.value = false
  router.push('/')
}

async function rename(kind: 'space' | 'list', id: string, name: string, space?: Space) {
  renaming.value = null
  if (kind === 'space' && space) await ws.renameSpace(space, name)
  else await ws.renameList(id, name)
}

async function remove(kind: 'space' | 'list', id: string, space?: Space) {
  menuFor.value = null
  if (kind === 'space' && space) await ws.deleteSpace(space)
  else await ws.deleteList(id)
  if (!ws.findList(route.params.listId as string)) router.push('/')
}

async function signOut() {
  await session.logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <aside class="sidebar" @click.self="menuFor = null">
    <div class="ws-switch">
      <button class="ws-button" @click="showWorkspaces = !showWorkspaces">
        <span class="ws-logo">{{ ws.current?.name[0] ?? 'T' }}</span>
        <span class="ws-name">{{ ws.current?.name ?? 'Taskloom' }}</span>
        <span class="faint">▾</span>
      </button>
      <div v-if="showWorkspaces" class="menu ws-menu">
        <button v-for="w in ws.workspaces" :key="w.id" @click="selectWorkspace(w.id)">
          <span class="ws-logo small">{{ w.name[0] }}</span> {{ w.name }}
          <span v-if="w.id === ws.current?.id" class="spacer" /><span v-if="w.id === ws.current?.id">✓</span>
        </button>
        <InlineInput v-if="addingWorkspace" placeholder="Workspace name" @submit="addWorkspace" @cancel="addingWorkspace = false" />
        <button v-else class="muted" @click="addingWorkspace = true">＋ New workspace</button>
      </div>
    </div>

    <div class="section-title row">
      <span>Spaces</span>
      <span class="spacer" />
      <button v-if="canManage()" class="icon" title="New space" @click="addingSpace = true">＋</button>
    </div>

    <nav class="tree">
      <div v-for="space in ws.current?.spaces ?? []" :key="space.id" class="space">
        <div class="space-row row">
          <button class="caret" @click="collapsed[space.id] = !collapsed[space.id]">{{ collapsed[space.id] ? '▸' : '▾' }}</button>
          <span class="space-icon" :style="{ background: space.color }">{{ space.name[0] }}</span>
          <InlineInput
            v-if="renaming === space.id"
            :initial="space.name"
            @submit="(n) => rename('space', space.id, n, space)"
            @cancel="renaming = null"
          />
          <span v-else class="space-name" @dblclick="renaming = space.id">{{ space.name }}</span>
          <span class="spacer" />
          <button v-if="canManage()" class="icon hover-only" title="Space actions" @click="menuFor = menuFor === space.id ? null : space.id">⋯</button>
          <button v-if="canManage()" class="icon hover-only" title="New list" @click="addingListTo = space.id">＋</button>
          <div v-if="menuFor === space.id" class="menu item-menu">
            <button @click="renaming = space.id; menuFor = null">Rename</button>
            <button class="danger" @click="remove('space', space.id, space)">Delete space</button>
          </div>
        </div>

        <template v-if="!collapsed[space.id]">
          <div v-for="list in space.lists" :key="list.id" class="list-row row" :class="{ active: route.params.listId === list.id }">
            <InlineInput v-if="renaming === list.id" :initial="list.name" @submit="(n) => rename('list', list.id, n)" @cancel="renaming = null" />
            <RouterLink v-else :to="{ name: 'list', params: { listId: list.id, view: (route.params.view as string) || 'list' } }" class="list-link">
              <span class="list-bullet">☰</span> {{ list.name }}
            </RouterLink>
            <button v-if="canManage()" class="icon hover-only" title="List actions" @click="menuFor = menuFor === list.id ? null : list.id">⋯</button>
            <div v-if="menuFor === list.id" class="menu item-menu">
              <button @click="renaming = list.id; menuFor = null">Rename</button>
              <button class="danger" @click="remove('list', list.id)">Delete list</button>
            </div>
          </div>
          <div v-if="addingListTo === space.id" class="list-row">
            <InlineInput placeholder="List name" @submit="(n) => addList(space, n)" @cancel="addingListTo = null" />
          </div>
        </template>
      </div>
      <div v-if="addingSpace" class="space-row">
        <InlineInput placeholder="Space name" @submit="addSpace" @cancel="addingSpace = false" />
      </div>
      <p v-if="ws.loaded && !ws.current?.spaces.length && !addingSpace" class="faint hint">No spaces yet. Use ＋ to add one.</p>
    </nav>

    <div class="me row">
      <Avatar :name="session.user?.displayName" />
      <div class="me-text">
        <div>{{ session.user?.displayName }}</div>
        <div class="faint small">{{ session.user?.email }}</div>
      </div>
      <span class="spacer" />
      <button class="muted" title="Sign out" @click="signOut">Sign out</button>
    </div>
  </aside>
</template>

<style scoped>
.sidebar { width: 260px; flex: none; background: var(--bg-sidebar); border-right: 1px solid var(--border); display: flex; flex-direction: column; padding: 10px 8px; gap: 6px; position: relative; }
.ws-switch { position: relative; }
.ws-button { width: 100%; display: flex; align-items: center; gap: 8px; padding: 6px; font-weight: 700; }
.ws-name { flex: 1; text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ws-logo { width: 24px; height: 24px; border-radius: 6px; background: var(--accent); color: #fff; display: inline-grid; place-items: center; font-size: 12px; font-weight: 700; flex: none; }
.ws-logo.small { width: 20px; height: 20px; font-size: 11px; }
.ws-menu { top: 40px; left: 0; right: 0; }
.ws-menu input { width: 100%; }
.section-title { font-size: 12px; font-weight: 600; color: var(--text-muted); padding: 10px 6px 2px; text-transform: uppercase; letter-spacing: 0.04em; }
.tree { flex: 1; overflow-y: auto; }
.space-row, .list-row { position: relative; padding: 2px 4px; border-radius: 6px; min-height: 30px; }
.space-row:hover, .list-row:hover { background: var(--bg-hover); }
.space-row input, .list-row input { flex: 1; min-width: 0; padding: 3px 6px; }
.caret { width: 18px; padding: 0; color: var(--text-faint); font-size: 11px; }
.space-icon { width: 20px; height: 20px; border-radius: 5px; color: #fff; display: inline-grid; place-items: center; font-size: 11px; font-weight: 700; flex: none; }
.space-name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.list-row { padding-left: 34px; }
.list-row.active { background: var(--bg-active); }
.list-link { flex: 1; color: var(--text); text-decoration: none; padding: 4px 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.list-bullet { color: var(--text-faint); font-size: 11px; margin-right: 4px; }
.icon { padding: 2px 6px; color: var(--text-muted); }
.hover-only { visibility: hidden; }
.space-row:hover .hover-only, .list-row:hover .hover-only { visibility: visible; }
.item-menu { top: 30px; right: 0; }
.hint { padding: 8px 10px; margin: 0; font-size: 13px; }
.me { border-top: 1px solid var(--border); padding: 10px 4px 2px; }
.me-text { min-width: 0; line-height: 1.25; }
.me-text > div { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 130px; }
.small { font-size: 12px; }
@media (max-width: 720px) { .sidebar { width: 100%; border-right: none; border-bottom: 1px solid var(--border); max-height: 45vh; } }
</style>

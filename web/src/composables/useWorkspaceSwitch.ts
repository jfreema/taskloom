// The only way to change the current workspace (8.2). Steps, in order: commit pending deletes,
// clear the selection, empty the task store, select, load members and tags, then (optionally) go Home.
import { useMembers } from '../stores/members'
import { useSelection } from '../stores/selection'
import { useTags } from '../stores/tags'
import { useTasks } from '../stores/tasks'
import { useWorkspaces } from '../stores/workspaces'
import type { Uuid } from '../types'
import { appRouter } from './appRouter'
import { useDelayedDelete } from './useDelayedDelete'

/** Switches run one at a time, in call order. */
let chain: Promise<void> = Promise.resolve()

async function run(wsId: Uuid, o: { navigate: boolean }): Promise<void> {
  const workspaces = useWorkspaces()
  if (workspaces.currentId !== wsId) {
    await useDelayedDelete().flushAll()
    useSelection().clear()
    useTasks().reset()
    await workspaces.select(wsId)
    await Promise.all([useMembers().ensure(wsId), useTags().ensure(wsId)])
  }
  if (o.navigate) {
    const router = appRouter()
    if (router) await router.push('/').catch(() => undefined)
  }
}

export function useWorkspaceSwitch(): { switchTo(wsId: Uuid, o: { navigate: boolean }): Promise<void> } {
  function switchTo(wsId: Uuid, o: { navigate: boolean }): Promise<void> {
    const next = chain.then(() => run(wsId, o))
    // Each step reports its own failures; a failed switch never blocks the next one.
    chain = next.catch(() => undefined)
    return chain
  }
  return { switchTo }
}

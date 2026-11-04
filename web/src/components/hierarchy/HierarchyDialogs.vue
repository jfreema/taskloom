<script setup lang="ts">
// The hierarchy dialogs (8.13), mounted once by AppShell. Each one opens from its ui store request
// (ui.openNewSpace(), ui.openNewList(), ui.openMoveList(), ui.openNewWorkspace(), ui.openInvite(),
// ui.openStatusEditor(), ui.openDeleteStatus()), which applies the role rules, so guests never get
// one. Delete status comes after the status editor, so it opens above it.
import DeleteStatusDialog from './DeleteStatusDialog.vue'
import InviteDialog from './InviteDialog.vue'
import MoveListDialog from './MoveListDialog.vue'
import NewListDialog from './NewListDialog.vue'
import NewSpaceDialog from './NewSpaceDialog.vue'
import NewWorkspaceDialog from './NewWorkspaceDialog.vue'
import StatusEditorDialog from './StatusEditorDialog.vue'
</script>

<template>
  <NewSpaceDialog />
  <NewListDialog />
  <MoveListDialog />
  <NewWorkspaceDialog />
  <InviteDialog />
  <StatusEditorDialog />
  <DeleteStatusDialog />
</template>

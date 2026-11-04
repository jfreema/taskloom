<script setup lang="ts">
// Invite people (8.13), Admin+, opened by ui.openInvite({ email?, assignTo? }) from the workspace
// menu, the members page and the assignee picker. The textarea takes pasted lists (commas,
// semicolons, new lines, "Name <email>"); one summary line counts the people and the invalid
// entries, which are skipped. Each address is posted in turn. Successes toast with "Copy sign-in
// link" (Taskloom sends no email); a single invite from the assignee picker also assigns the person
// to those tasks, with Undo. Failures are listed inline and only they stay in the textarea.
import { computed, nextTick, ref, useId, watch } from 'vue'
import { useClipboard } from '../../composables/useClipboard'
import { errorMessage, useToast } from '../../composables/useToast'
import { parseInviteList, pluralize } from '../../lib/format'
import { useMembers } from '../../stores/members'
import { useTasks } from '../../stores/tasks'
import { useUi } from '../../stores/ui'
import { useWorkspaces } from '../../stores/workspaces'
import { ROLE_DESCRIPTION, ROLE_LABEL, type AssignableRole, type Member, type Uuid } from '../../types'
import UiButton from '../ui/UiButton.vue'
import UiDialog from '../ui/UiDialog.vue'
import UiField from '../ui/UiField.vue'
import UiInlineAlert from '../ui/UiInlineAlert.vue'
import UiRadioCards from '../ui/UiRadioCards.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { useRequestedDialog } from './useRequestedDialog'

const ui = useUi()
const workspaces = useWorkspaces()
const members = useMembers()
const tasks = useTasks()
const toast = useToast()
const clipboard = useClipboard()
const uid = useId()
const formId = `${uid}-form`
const summaryId = `${uid}-summary`
const roleLabelId = `${uid}-role`

const ROLES: AssignableRole[] = ['Member', 'Admin', 'Guest']
const roleOptions = ROLES.map((r) => ({ value: r, label: ROLE_LABEL[r], description: ROLE_DESCRIPTION[r] }))

const text = ref('')
const role = ref<AssignableRole>('Member')
const busy = ref(false)
const formError = ref('')
const failures = ref<{ key: string; message: string }[]>([])
const assignTo = ref<Uuid[]>([])
const area = ref<InstanceType<typeof UiTextarea> | null>(null)

const open = useRequestedDialog(
  () => ui.invite,
  () => {
    ui.invite = null
  },
  (r) => {
    text.value = r.email ?? ''
    role.value = 'Member'
    busy.value = false
    formError.value = ''
    failures.value = []
    assignTo.value = r.assignTo ? [...r.assignTo] : []
  },
)

watch(
  () => [workspaces.isAdmin(), workspaces.currentId] as const,
  ([admin, id], [, wasId]) => {
    if (!admin || id !== wasId) open.value = false
  },
)

const parsed = computed(() => parseInviteList(text.value))

/** "Ana Moreno, sam@example.com, …": the first two, then an ellipsis. */
function shortList(items: readonly string[], max = 2): string {
  return items.length > max ? `${items.slice(0, max).join(', ')}, …` : items.join(', ')
}

const summary = computed(() => {
  const { valid } = parsed.value
  return valid.length ? `${pluralize(valid.length, 'person', 'people')}: ${shortList(valid.map((v) => v.displayName ?? v.email))}` : ''
})

const invalidLine = computed(() => {
  const { invalid } = parsed.value
  return invalid.length ? `${invalid.length} invalid: ${shortList(invalid)}` : ''
})

const submitLabel = computed(() => {
  const n = parsed.value.valid.length
  return n === 0 ? 'Add people' : `Add ${pluralize(n, 'person', 'people')}`
})

const describedBy = (fieldIds: string | undefined) => [fieldIds, summaryId].filter(Boolean).join(' ')

function onInput(): void {
  formError.value = ''
}

async function copyLink(): Promise<void> {
  const ok = await clipboard.copy(window.location.origin)
  if (ok) toast.success('Link copied')
  else toast.show({ message: "Couldn't copy the link.", tone: 'danger' })
}

function reportSuccess(added: Member[]): void {
  const only = added.length === 1 ? added[0] : null
  const loaded = assignTo.value.filter((id) => !!tasks.get(id))
  if (only && loaded.length) {
    void tasks
      .bulkUpdate(
        loaded.map((id) => ({ id, addAssigneeIds: [only.userId] })),
        { undo: `Invited and assigned ${only.displayName}` },
      )
      .catch(() => undefined)
    return
  }
  toast.show({
    message: only
      ? `Added ${only.displayName}. They get access when they sign in with this address.`
      : `Added ${pluralize(added.length, 'person', 'people')}. They get access when they sign in with these addresses.`,
    tone: 'success',
    action: { label: 'Copy sign-in link', run: () => void copyLink() },
  })
}

async function submit(): Promise<void> {
  if (busy.value) return
  const { valid, invalid } = parsed.value
  if (!valid.length) {
    formError.value = invalid.length ? 'None of these is a valid email address.' : 'Enter at least one email address.'
    area.value?.focus()
    return
  }
  busy.value = true
  formError.value = ''
  failures.value = []
  const added: Member[] = []
  const failed: { entry: string; message: string; email: string }[] = []
  for (const v of valid) {
    try {
      added.push(await members.invite({ email: v.email, role: role.value, displayName: v.displayName }))
    } catch (err) {
      const message = errorMessage(err).message || `Couldn't add ${v.email}.`
      failed.push({ entry: v.displayName ? `${v.displayName} <${v.email}>` : v.email, message, email: v.email })
    }
  }
  busy.value = false
  if (!open.value) return
  if (added.length) reportSuccess(added)
  if (!failed.length) {
    open.value = false
    return
  }
  failures.value = failed.map((f) => ({
    key: f.email,
    message: f.message.toLowerCase().includes(f.email.toLowerCase()) ? f.message : `${f.email}: ${f.message}`,
  }))
  text.value = [...failed.map((f) => f.entry), ...invalid].join('\n')
  void nextTick(() => area.value?.focus())
}
</script>

<template>
  <UiDialog
    v-model:open="open"
    title="Invite people"
    description="Taskloom doesn't send emails. Share the sign-in link: people get access when they sign in with the address you add."
  >
    <form :id="formId" class="form" novalidate @submit.prevent="submit">
      <UiInlineAlert v-if="failures.length" tone="danger" :title="`Couldn't add ${pluralize(failures.length, 'person', 'people')}`">
        <ul class="failures">
          <li v-for="f in failures" :key="f.key">{{ f.message }}</li>
        </ul>
      </UiInlineAlert>

      <div class="addresses">
        <UiField label="Email addresses" :error="formError || undefined">
          <template #default="{ id, describedBy: fieldIds, invalid }">
            <UiTextarea
              :id="id"
              ref="area"
              v-model="text"
              :aria-describedby="describedBy(fieldIds)"
              :invalid="invalid"
              :rows="3"
              :max-rows="8"
              placeholder="ana@example.com, Sam Lee <sam@example.com>"
              submit-on="mod-enter"
              spellcheck="false"
              @update:model-value="onInput"
              @submit="submit"
            />
          </template>
        </UiField>
        <p :id="summaryId" class="summary" :class="{ muted: !summary && !invalidLine }">
          <template v-if="summary || invalidLine">
            <span v-if="summary">{{ summary }}</span>
            <span v-if="summary && invalidLine" aria-hidden="true"> · </span>
            <span v-if="invalidLine" class="invalid">{{ invalidLine }}</span>
          </template>
          <template v-else>Separate addresses with commas or new lines.</template>
        </p>
      </div>

      <div class="group">
        <span :id="roleLabelId" class="label">Role</span>
        <UiRadioCards v-model="role" :options="roleOptions" :name="`${uid}-role`" :columns="3" :aria-labelledby="roleLabelId" />
      </div>
    </form>
    <template #footer>
      <UiButton variant="secondary" @click="open = false">Cancel</UiButton>
      <UiButton variant="primary" type="submit" :form="formId" :loading="busy">{{ submitLabel }}</UiButton>
    </template>
  </UiDialog>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.addresses {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}

.summary {
  min-height: var(--lh-xs);
  font-size: var(--fs-xs);
  line-height: var(--lh-xs);
  color: var(--text-secondary);
  overflow-wrap: anywhere;
}

.summary.muted {
  color: var(--text-tertiary);
}

.invalid {
  color: var(--danger-text);
}

.group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1-5);
}

.label {
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}

.failures {
  display: flex;
  flex-direction: column;
  gap: var(--space-0-5);
  padding-left: var(--space-4);
  list-style: disc;
  overflow-wrap: anywhere;
}
</style>

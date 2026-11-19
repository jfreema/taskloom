<script setup lang="ts">
// Settings › Notifications (Features 2): which emails Taskloom sends you. Everything also lands in
// the Inbox, whatever is chosen here.
// - "Assignments and mentions": an email when someone assigns you a task or @mentions you, sent
//   about a minute later and only if you haven't read it in the Inbox by then.
// - "Due-date reminders": one email in the morning, in your time zone, listing your tasks due that
//   day and the ones that became overdue.
// The time zone comes from this browser (stores/session.ts keeps the server's copy in step). When
// the server has no mail provider set up, a note says emails aren't sent yet; the switches still save.
import { computed } from 'vue'
import SettingsPage from '../../components/settings/SettingsPage.vue'
import UiInlineAlert from '../../components/ui/UiInlineAlert.vue'
import UiSwitch from '../../components/ui/UiSwitch.vue'
import { useToast } from '../../composables/useToast'
import { browserTimeZone, useSession } from '../../stores/session'

const session = useSession()
const toast = useToast()

function save(patch: { emailMentions?: boolean; emailReminders?: boolean }): void {
  session.updateNotifications(patch).catch((err: unknown) => {
    toast.error(err, { retry: () => save(patch) })
  })
}

const mentions = computed<boolean>({
  get: () => session.user?.emailMentions ?? true,
  set: (v) => save({ emailMentions: v }),
})

const reminders = computed<boolean>({
  get: () => session.user?.emailReminders ?? true,
  set: (v) => save({ emailReminders: v }),
})

/** "America/Chicago" reads as "America/Chicago (Central Time)" where the browser knows a name for it. */
const zone = computed(() => {
  const id = session.user?.timeZone ?? browserTimeZone()
  if (!id) return null
  try {
    const name = new Intl.DateTimeFormat(undefined, { timeZone: id, timeZoneName: 'longGeneric' }).formatToParts(new Date()).find((p) => p.type === 'timeZoneName')?.value
    return name && name !== id ? `${id.replace(/_/g, ' ')} (${name})` : id.replace(/_/g, ' ')
  } catch {
    return id
  }
})
</script>

<template>
  <SettingsPage title="Notifications" label="Notification settings" description="Everything that happens on your tasks lands in your Inbox. Choose what also reaches you by email.">
    <UiInlineAlert v-if="session.user && !session.user.emailConfigured" tone="info" title="Email isn't set up on this server yet">
      Nothing is emailed for now. Your choices are saved for when it is.
    </UiInlineAlert>

    <section class="block" aria-labelledby="notifications-email">
      <h3 id="notifications-email" class="block-title">Email</h3>
      <p v-if="session.user" class="block-text">Sent to {{ session.user.email }}.</p>
      <UiSwitch
        v-model="mentions"
        label="Assignments and mentions"
        description="When someone assigns you a task or @mentions you in a comment. Skipped if you've already read it in the Inbox."
      />
      <UiSwitch
        v-model="reminders"
        label="Due-date reminders"
        description="One email in the morning with your tasks due that day and the ones that just became overdue."
      />
    </section>

    <section v-if="zone" class="block" aria-labelledby="notifications-zone">
      <h3 id="notifications-zone" class="block-title">Time zone</h3>
      <p class="block-text zone-text">
        Reminders follow <b class="zone">{{ zone }}</b>, the time zone of the browser you last used.
      </p>
    </section>
  </SettingsPage>
</template>

<style scoped>
.block {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.block + .block {
  padding-top: var(--space-6);
  border-top: 1px solid var(--border-subtle);
}

.block-title {
  font-size: var(--fs-md);
  line-height: var(--lh-md);
  font-weight: var(--fw-semibold);
  color: var(--text-primary);
}

.block-text {
  margin-top: calc(var(--space-2) * -1);
  font-size: var(--fs-sm);
  line-height: var(--lh-sm);
  color: var(--text-secondary);
  overflow-wrap: anywhere;
}

.zone {
  font-weight: var(--fw-medium);
  color: var(--text-primary);
}
</style>

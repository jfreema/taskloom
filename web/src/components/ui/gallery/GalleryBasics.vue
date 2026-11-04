<script setup lang="ts">
// /_ui gallery: the logo, icons, buttons, kbd hints, avatars, badges and chips.
import { ref } from 'vue'
import { Calendar, Ellipsis, Flag, House, Link, ListFilter, Plus, Search, SquareKanban, SquarePen, Tag, Trash2, UserRoundPlus } from '@lucide/vue'
import AppLogo from '../../shell/AppLogo.vue'
import UiAvatar from '../UiAvatar.vue'
import UiAvatarStack from '../UiAvatarStack.vue'
import UiBadge from '../UiBadge.vue'
import UiButton from '../UiButton.vue'
import UiChip from '../UiChip.vue'
import UiIcon from '../UiIcon.vue'
import UiIconButton from '../UiIconButton.vue'
import UiKbd from '../UiKbd.vue'
import GalleryCard from './GalleryCard.vue'
import { PEOPLE } from './sample'

const pressed = ref(true)
const chipOn = ref(true)
</script>

<template>
  <GalleryCard title="AppLogo">
    <div class="row">
      <AppLogo :size="16" /><AppLogo :size="20" /><AppLogo :size="24" /><AppLogo :size="32" />
      <AppLogo :size="24" wordmark />
    </div>
  </GalleryCard>

  <GalleryCard title="UiIcon">
    <div class="row muted">
      <UiIcon :icon="House" :size="12" /><UiIcon :icon="Search" :size="14" /><UiIcon :icon="Calendar" />
      <UiIcon :icon="Flag" :size="20" filled /><UiIcon :icon="Tag" :size="24" />
      <UiIcon :icon="SquareKanban" label="Board" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiButton" wide>
    <div class="row">
      <UiButton variant="primary" :icon="Plus" kbd="N">New task</UiButton>
      <UiButton>Secondary</UiButton>
      <UiButton variant="ghost" :icon="ListFilter">Filter</UiButton>
      <UiButton variant="danger" :icon="Trash2">Delete</UiButton>
      <UiButton variant="danger-ghost">Remove</UiButton>
      <UiButton variant="link">Clear filters</UiButton>
    </div>
    <div class="row">
      <UiButton size="xs">Extra small</UiButton>
      <UiButton size="sm">Small</UiButton>
      <UiButton size="lg" variant="primary">Large</UiButton>
      <UiButton variant="primary" loading>Saving</UiButton>
      <UiButton :icon="Calendar" loading>Loading</UiButton>
      <UiButton disabled>Disabled</UiButton>
    </div>
  </GalleryCard>

  <GalleryCard title="UiIconButton">
    <div class="row">
      <UiIconButton :icon="SquarePen" label="New task" kbd="N" />
      <UiIconButton :icon="Ellipsis" label="More actions" variant="secondary" />
      <UiIconButton :icon="Plus" label="Add" variant="primary" />
      <UiIconButton :icon="Trash2" label="Delete" variant="danger-ghost" />
      <UiIconButton :icon="ListFilter" label="Filter" :pressed="pressed" @click="pressed = !pressed" />
      <UiIconButton :icon="Link" label="Copy link" size="xs" />
      <UiIconButton :icon="Search" label="Search" size="lg" />
      <UiIconButton :icon="Calendar" label="Due date" disabled />
    </div>
  </GalleryCard>

  <GalleryCard title="UiKbd">
    <div class="row">
      <UiKbd keys="Mod+K" /><UiKbd keys="G H" /><UiKbd keys="Shift+F10" /><UiKbd keys="Alt+ArrowUp" />
      <UiKbd keys="Escape" /><UiKbd keys="?" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiAvatar">
    <div class="row">
      <UiAvatar :user="PEOPLE[0]" :size="16" /><UiAvatar :user="PEOPLE[1]" :size="20" />
      <UiAvatar :user="PEOPLE[2]" :size="24" /><UiAvatar :user="PEOPLE[3]" :size="28" />
      <UiAvatar :user="PEOPLE[4]" :size="32" /><UiAvatar :user="PEOPLE[0]" :size="40" />
      <UiAvatar :user="PEOPLE[1]" pending /><UiAvatar :user="null" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiAvatarStack">
    <div class="row">
      <UiAvatarStack :users="PEOPLE.slice(0, 1)" />
      <UiAvatarStack :users="PEOPLE.slice(0, 3)" />
      <UiAvatarStack :users="PEOPLE" />
      <UiAvatarStack :users="[]" />
    </div>
  </GalleryCard>

  <GalleryCard title="UiBadge" wide>
    <div class="row">
      <UiBadge>Neutral</UiBadge><UiBadge tone="accent">Accent</UiBadge><UiBadge tone="success">Done</UiBadge>
      <UiBadge tone="warning">Dev only</UiBadge><UiBadge tone="danger">Overdue</UiBadge><UiBadge tone="info">Invited</UiBadge>
    </div>
    <div class="row">
      <UiBadge variant="solid" tone="accent">2</UiBadge><UiBadge variant="solid" tone="danger">9</UiBadge>
      <UiBadge variant="outline">Guest</UiBadge><UiBadge tone="success" dot :size="20">Online</UiBadge>
      <UiBadge tone="info" :icon="UserRoundPlus" :size="20">Pending</UiBadge>
    </div>
  </GalleryCard>

  <GalleryCard title="UiChip">
    <div class="row">
      <UiChip label="Assigned to me" :selected="chipOn" @click="chipOn = !chipOn" />
      <UiChip label="Status · 2" removable remove-label="Remove Status filter" />
      <UiChip :icon="Tag" label="design" size="md" />
      <UiChip as="span" label="Static" />
    </div>
  </GalleryCard>
</template>

<style scoped src="./gallery.css"></style>

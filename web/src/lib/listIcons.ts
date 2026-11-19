// The icons a list can have (Look 2), by the name the API stores (Taskloom.Domain ListIcons.All
// holds the same names in the same order: keep both in step). "list" is the plain icon every list
// without one shows. Picker order: seven to a row.
import type { Component } from 'vue'
import {
  BookOpen, Bookmark, Briefcase, Bug, Building, Calendar, Camera, ChartColumn, ClipboardList, Code, Coffee,
  Dumbbell, FileText, Flag, Gift, Globe, GraduationCap, Headphones, Heart, House, Inbox, Leaf, Lightbulb,
  List, ListChecks, Mail, Megaphone, MessageCircle, Package, Palette, Plane, Rocket, Shield, ShoppingCart,
  Sparkles, Star, Target, Trophy, Users, Wallet, Wrench, Zap,
} from '@lucide/vue'

export interface ListIcon {
  name: string
  /** What a screen reader calls it in the picker. */
  label: string
  icon: Component
}

export const DEFAULT_LIST_ICON = 'list'

export const LIST_ICONS: readonly ListIcon[] = [
  { name: 'list', label: 'List', icon: List },
  { name: 'list-checks', label: 'Checklist', icon: ListChecks },
  { name: 'star', label: 'Star', icon: Star },
  { name: 'flag', label: 'Flag', icon: Flag },
  { name: 'target', label: 'Target', icon: Target },
  { name: 'rocket', label: 'Rocket', icon: Rocket },
  { name: 'zap', label: 'Lightning', icon: Zap },
  { name: 'lightbulb', label: 'Light bulb', icon: Lightbulb },
  { name: 'bookmark', label: 'Bookmark', icon: Bookmark },
  { name: 'book-open', label: 'Book', icon: BookOpen },
  { name: 'briefcase', label: 'Briefcase', icon: Briefcase },
  { name: 'calendar', label: 'Calendar', icon: Calendar },
  { name: 'clipboard-list', label: 'Clipboard', icon: ClipboardList },
  { name: 'file-text', label: 'Document', icon: FileText },
  { name: 'code', label: 'Code', icon: Code },
  { name: 'bug', label: 'Bug', icon: Bug },
  { name: 'wrench', label: 'Wrench', icon: Wrench },
  { name: 'palette', label: 'Palette', icon: Palette },
  { name: 'megaphone', label: 'Megaphone', icon: Megaphone },
  { name: 'message-circle', label: 'Chat', icon: MessageCircle },
  { name: 'mail', label: 'Mail', icon: Mail },
  { name: 'users', label: 'People', icon: Users },
  { name: 'heart', label: 'Heart', icon: Heart },
  { name: 'house', label: 'House', icon: House },
  { name: 'shopping-cart', label: 'Shopping cart', icon: ShoppingCart },
  { name: 'wallet', label: 'Wallet', icon: Wallet },
  { name: 'chart-column', label: 'Chart', icon: ChartColumn },
  { name: 'graduation-cap', label: 'Graduation cap', icon: GraduationCap },
  { name: 'plane', label: 'Plane', icon: Plane },
  { name: 'coffee', label: 'Coffee', icon: Coffee },
  { name: 'headphones', label: 'Headphones', icon: Headphones },
  { name: 'camera', label: 'Camera', icon: Camera },
  { name: 'globe', label: 'Globe', icon: Globe },
  { name: 'leaf', label: 'Leaf', icon: Leaf },
  { name: 'dumbbell', label: 'Dumbbell', icon: Dumbbell },
  { name: 'gift', label: 'Gift', icon: Gift },
  { name: 'trophy', label: 'Trophy', icon: Trophy },
  { name: 'package', label: 'Package', icon: Package },
  { name: 'inbox', label: 'Inbox', icon: Inbox },
  { name: 'shield', label: 'Shield', icon: Shield },
  { name: 'building', label: 'Building', icon: Building },
  { name: 'sparkles', label: 'Sparkles', icon: Sparkles },
]

const BY_NAME = new Map(LIST_ICONS.map((i) => [i.name, i]))

/** The icon to draw for a list: its own, else the plain list icon (also for a name this build doesn't know). */
export function listIconOf(name: string | null | undefined): ListIcon {
  return (name ? BY_NAME.get(name) : undefined) ?? LIST_ICONS[0]
}

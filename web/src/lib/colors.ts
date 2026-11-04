// Data colours (7.2): the 13 swatches that statuses, spaces and tags store, and the avatar colours.
// One of the few files allowed to hold hex values (S1 AC1).

export interface Swatch {
  name: string
  hex: string
}

/** Picker order. The C# `Palette` class holds the same values. */
export const SWATCHES: Swatch[] = [
  { name: 'Slate', hex: '#7A868C' },
  { name: 'Blue', hex: '#3D7DF5' },
  { name: 'Indigo', hex: '#6E6CF0' },
  { name: 'Violet', hex: '#9B5DE5' },
  { name: 'Pink', hex: '#DB4C8B' },
  { name: 'Red', hex: '#E0484E' },
  { name: 'Orange', hex: '#D2661B' },
  { name: 'Amber', hex: '#B07A0A' },
  { name: 'Lime', hex: '#6F9A1B' },
  { name: 'Green', hex: '#2E9F5E' },
  { name: 'Teal', hex: '#14998A' },
  { name: 'Cyan', hex: '#1C93B8' },
  { name: 'Brown', hex: '#A87750' },
]

/** Named defaults from 7.2. */
export const DEFAULT_COLORS = {
  todo: '#7A868C',
  inProgress: '#3D7DF5',
  review: '#9B5DE5',
  waiting: '#B07A0A',
  done: '#2E9F5E',
  space: '#14998A',
  tag: '#3D7DF5',
} as const

export const AVATAR_COLORS = ['#0D7469', '#2F5FD0', '#6A47C9', '#B4366F', '#A44A12', '#2D7A3E', '#4F5B61', '#8A5A2B']

/** The avatar colour comes from the user id (FNV-1a, 32-bit), so renaming doesn't recolour anyone. */
export function avatarColor(userId: string): string {
  let h = 0x811c9dc5
  for (let i = 0; i < userId.length; i++) {
    h ^= userId.charCodeAt(i)
    h = Math.imul(h, 0x01000193) >>> 0
  }
  return AVATAR_COLORS[h % AVATAR_COLORS.length]
}

function indexOfSwatch(hex: string | null | undefined): number {
  if (!hex) return -1
  const h = hex.trim().toUpperCase()
  return SWATCHES.findIndex((s) => s.hex === h)
}

export function isSwatch(hex: string | null | undefined): boolean {
  return indexOfSwatch(hex) >= 0
}

/** The swatch's name ("Teal"), or "Custom" for any other colour. */
export function swatchName(hex: string | null | undefined): string {
  const i = indexOfSwatch(hex)
  return i >= 0 ? SWATCHES[i].name : 'Custom'
}

/**
 * The next swatch in picker order after `after` (wrapping from Brown to Slate).
 * Without a swatch to follow (none given, or a custom colour) it is Blue, the tags default of 7.2.
 */
export function nextSwatch(after?: string | null): string {
  const i = indexOfSwatch(after)
  if (i < 0) return DEFAULT_COLORS.tag
  return SWATCHES[(i + 1) % SWATCHES.length].hex
}

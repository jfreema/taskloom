// Attachment limits and helpers (5.1, 8.10).

/** 25 MiB, the server's limit. */
export const MAX_ATTACHMENT_BYTES = 26_214_400
/** Raster images up to 5 MiB show as thumbnails; larger ones show as file tiles. */
export const MAX_THUMBNAIL_BYTES = 5_242_880

const RASTER_TYPES = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp'])

/** "512 B", "12 KB", "1.2 MB", "3.4 GB" (1024-based, one decimal below 10). */
export function formatBytes(n: number): string {
  if (!Number.isFinite(n) || n < 0) return '0 B'
  if (n < 1024) return `${Math.round(n)} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let value = n / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  const rounded = value < 10 ? Math.round(value * 10) / 10 : Math.round(value)
  return `${rounded} ${units[unit]}`
}

/** PNG, JPEG, GIF and WebP (never SVG, which the server always sends as a download). */
export function isRasterImage(contentType: string | null | undefined): boolean {
  if (!contentType) return false
  return RASTER_TYPES.has(contentType.split(';')[0].trim().toLowerCase())
}

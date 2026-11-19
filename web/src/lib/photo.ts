// Profile photos (Look 2): the picture someone chooses becomes a 256-pixel square JPEG, cropped
// from its middle, before it is uploaded. The server keeps photos of at most 512 KB and 2048 pixels
// a side; a photo made here is a few dozen KB, so any picture the browser can open will do.

export const PHOTO_SIZE = 256
export const PHOTO_TYPES = 'image/jpeg,image/png,image/webp,image/gif'
export const UNREADABLE_PHOTO = "That file isn't a picture Taskloom can read. Try a JPEG or PNG."

/** Thrown when the browser can't open the chosen file as a picture; its message is for the person. */
export class UnreadablePhotoError extends Error {
  constructor() {
    super(UNREADABLE_PHOTO)
    this.name = 'UnreadablePhotoError'
  }
}

async function decode(file: Blob): Promise<{ source: CanvasImageSource; width: number; height: number; close(): void }> {
  if (typeof createImageBitmap === 'function') {
    try {
      const bitmap = await createImageBitmap(file)
      return { source: bitmap, width: bitmap.width, height: bitmap.height, close: () => bitmap.close() }
    } catch {
      // Fall through to an <img>, which some browsers can open more formats with.
    }
  }
  const url = URL.createObjectURL(file)
  try {
    const img = new Image()
    img.decoding = 'async'
    img.src = url
    await img.decode()
    return { source: img, width: img.naturalWidth, height: img.naturalHeight, close: () => URL.revokeObjectURL(url) }
  } catch {
    URL.revokeObjectURL(url)
    throw new UnreadablePhotoError()
  }
}

/** The middle square of the picture, scaled to `size` pixels, as a JPEG on white (for transparent PNGs). */
export async function squarePhoto(file: Blob, size = PHOTO_SIZE): Promise<Blob> {
  const image = await decode(file)
  try {
    if (!image.width || !image.height) throw new UnreadablePhotoError()
    const side = Math.min(image.width, image.height)
    const canvas = document.createElement('canvas')
    canvas.width = size
    canvas.height = size
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new UnreadablePhotoError()
    ctx.fillStyle = '#FFFFFF'
    ctx.fillRect(0, 0, size, size)
    ctx.imageSmoothingQuality = 'high'
    ctx.drawImage(image.source, (image.width - side) / 2, (image.height - side) / 2, side, side, 0, 0, size, size)
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.88))
    if (!blob) throw new UnreadablePhotoError()
    return blob
  } finally {
    image.close()
  }
}

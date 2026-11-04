// Mirrors a ui-store request (ui.newSpace, ui.newList, ...) into a dialog's own open flag: a request
// opens the dialog after `onOpen` has copied what the dialog needs, closing the dialog clears the
// request, and a request cleared elsewhere (sign-out, ui.reset) closes the dialog. The copy keeps
// the title steady while the dialog fades out.
import { ref, watch, type Ref } from 'vue'

export function useRequestedDialog<T>(
  request: () => T | null | false | undefined,
  clear: () => void,
  onOpen: (r: T) => void,
): Ref<boolean> {
  const open = ref(false)
  watch(
    request,
    (r) => {
      if (r) {
        onOpen(r)
        open.value = true
      } else {
        open.value = false
      }
    },
    { immediate: true },
  )
  watch(open, (on) => {
    if (!on && request()) clear()
  })
  return open
}

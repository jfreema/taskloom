// Focus for sidebar rows that go away while focused (8.3, 9.6). Deleting a space or list from its
// menu returns focus to the row's "..." button, and the delete then removes the row. Rather than
// letting focus fall to the page body, it moves to the next sidebar row, else the previous one.
import { nextTick, onBeforeUnmount, type Ref } from 'vue'

const ITEM = '[data-sidebar-item]'

export function useSidebarFocusRescue(el: Ref<HTMLElement | null>): void {
  onBeforeUnmount(() => {
    const root = el.value
    const active = document.activeElement
    if (!root || !active || !root.contains(active)) return
    const items = Array.from(root.closest('nav')?.querySelectorAll<HTMLElement>(ITEM) ?? []).filter(
      (item) => !root.contains(item) && item.getClientRects().length > 0,
    )
    const after = items.find((item) => !!(root.compareDocumentPosition(item) & Node.DOCUMENT_POSITION_FOLLOWING))
    const before = items.filter((item) => !!(root.compareDocumentPosition(item) & Node.DOCUMENT_POSITION_PRECEDING)).pop()
    const target = after ?? before
    if (!target) return
    void nextTick(() => {
      const now = document.activeElement
      if (now && now !== document.body && now.isConnected) return
      if (target.isConnected && !target.closest('[inert]')) target.focus({ preventScroll: true })
    })
  })
}

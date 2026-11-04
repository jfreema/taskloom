// The text typed into a picker's search field, read from the DOM by a footer action ("Invite
// people" passes it on when it looks like an email). UiListbox keeps its query to itself, and the
// footer slot sits inside the same listbox, so the nearest combobox input above the footer is it.
export function queryNear(from: EventTarget | null): string {
  let node = from instanceof Element ? from.parentElement : null
  while (node) {
    const input = node.querySelector<HTMLInputElement>('input[role="combobox"]')
    if (input) return input.value.trim()
    node = node.parentElement
  }
  return ''
}

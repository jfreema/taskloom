// Menu item icons for the bulk bar's menus (8.7). UiMenu draws an item's icon through UiIcon, which
// hands the icon a class and some SVG attributes; these keep the class and draw something else in
// its 16px box: a status's own icon, a person's avatar or a tag's colour.
import { defineComponent, type Component, type VNode } from 'vue'

/** Item icons by key, made once each; `draw` gets the key and the class UiMenu gives the icon. */
export function menuGlyphs<K>(draw: (key: K, cls: unknown) => VNode | null): (key: K) => Component {
  const made = new Map<K, Component>()
  return (key) => {
    let glyph = made.get(key)
    if (!glyph) {
      glyph = defineComponent({
        name: 'BulkMenuGlyph',
        inheritAttrs: false,
        setup(_, { attrs }) {
          return () => draw(key, attrs.class)
        },
      })
      made.set(key, glyph)
    }
    return glyph
  }
}

// Fractional ordering positions. Mirrors `Positions.Between` in Taskloom.Application (step 1024),
// so optimistic placement matches what the server will store.

export const STEP = 1024

/** A position between two neighbours: the midpoint, one step past the only neighbour, or STEP. */
export function between(before?: number | null, after?: number | null): number {
  const hasBefore = typeof before === 'number' && Number.isFinite(before)
  const hasAfter = typeof after === 'number' && Number.isFinite(after)
  if (hasBefore && hasAfter) return ((before as number) + (after as number)) / 2
  if (hasBefore) return (before as number) + STEP
  if (hasAfter) return (after as number) - STEP
  return STEP
}

/** The top of a sibling group: minimum − 1024, or 1024 when empty (create placement 'Top'). */
export function topOf(positions: number[]): number {
  return between(null, positions.length ? Math.min(...positions) : null)
}

/** The bottom of a sibling group: maximum + 1024, or 1024 when empty (create placement 'Bottom'). */
export function bottomOf(positions: number[]): number {
  return between(positions.length ? Math.max(...positions) : null, null)
}

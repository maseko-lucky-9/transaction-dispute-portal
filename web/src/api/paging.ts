/** The shape every keyset-paged list endpoint returns. */
export interface Page<T> {
  items: T[];
  /** Opaque and protected: only ever echoed back as `cursor`, never parsed or shown. */
  nextCursor: string | null;
}

/** Flatten pages into one list. First occurrence wins, so a repeated row is never rendered twice. */
export function flattenPages<T extends { id: string }>(pages: readonly Page<T>[]): T[] {
  const seen = new Set<string>();
  const items: T[] = [];
  for (const page of pages) {
    for (const item of page.items) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      items.push(item);
    }
  }
  return items;
}

/**
 * The search for a status filter link: the current search (limit, from, to ...) with only the
 * status swapped. `undefined` is "All" (or a default such as the agent queue's Open): the key
 * is removed, so the URL carries no `status`, exactly as choosing it in the form does.
 */
export function searchWithStatus<T extends { status?: unknown }>(
  search: T,
  status: T["status"] | undefined,
): Omit<T, "status"> & { status?: T["status"] } {
  const next = { ...search };
  delete next.status;
  return status === undefined ? next : { ...next, status };
}

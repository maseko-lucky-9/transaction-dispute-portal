/**
 * The search for a status filter link: the current search (limit, from, to ...) with only the
 * status swapped. `undefined` is "All": the key is removed, so the URL carries no `status`,
 * exactly as choosing "All statuses" in the form does.
 */
export function searchWithStatus<T extends { status?: unknown }>(
  search: T,
  status: T["status"],
): T {
  const next = { ...search };
  delete next.status;
  return status === undefined ? next : { ...next, status };
}

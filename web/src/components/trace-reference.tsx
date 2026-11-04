import { isApiError } from "@/api/problem";

/** "Reference: <traceId>" for a failed request, so a user can quote it and an engineer find it. */
export function TraceReference({ error }: { error: unknown }) {
  const traceId = isApiError(error) ? error.traceId : undefined;
  return traceId ? (
    <p className="text-sm text-muted-foreground">
      Reference: <code className="font-mono text-foreground">{traceId}</code>
    </p>
  ) : null;
}

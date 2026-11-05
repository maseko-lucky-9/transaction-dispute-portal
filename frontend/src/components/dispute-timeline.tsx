import type { DisputeStatus, TimelineEntry } from "@/api/disputes";
import { DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";
import { formatDateTime, shortId } from "@/lib/format";

const label = (status: DisputeStatus) => DISPUTE_STATUS_LABELS[status];

/** An entry as either viewer gets it; only the agent API sends `actorId`. */
export type TimelineEntryView = TimelineEntry & { actorId?: string | null | undefined };

/** Who is looking: it decides how much of an actor is shown. */
export type TimelineViewer = "customer" | "agent";

/**
 * A customer only ever sees the KIND of actor: "You" or "Back-office". An agent's identity is
 * not in the customer API's response and must never be shown or guessed. An agent sees
 * "Customer" for the customer and the acting agent's id, shortened, for agents.
 */
function actorName(entry: TimelineEntryView, viewer: TimelineViewer): string {
  if (viewer === "customer") return entry.actorType === "Customer" ? "You" : "Back-office";
  if (entry.actorType === "Customer") return "Customer";
  return entry.actorId ? `Agent ${shortId(entry.actorId)}` : "An agent";
}

function describe(entry: TimelineEntryView, viewer: TimelineViewer): string {
  const isCustomer = entry.actorType === "Customer";
  const actor = actorName(entry, viewer);
  const verb = (past: string) =>
    viewer === "customer" && isCustomer ? `You ${past}` : `${actor} ${past}`;
  if (entry.from === null) {
    return isCustomer
      ? `${verb("submitted")} this dispute`
      : `${actor} set the status to ${label(entry.to)}`;
  }
  if (isCustomer && entry.to === "Withdrawn") return `${verb("withdrew")} this dispute`;
  return `${actor} changed the status from ${label(entry.from)} to ${label(entry.to)}`;
}

/** Every status change, oldest first, with when it happened and the kind of actor. */
export function DisputeTimeline({
  entries,
  viewer = "customer",
}: {
  entries: readonly TimelineEntryView[];
  viewer?: TimelineViewer;
}) {
  return (
    <ol aria-label="Timeline" className="flex flex-col gap-4 border-l pl-6">
      {entries.map((entry, index) => (
        // The API gives entries no id (and none must be invented); order and time identify them.
        <li key={`${index}-${entry.occurredAt}`} className="relative flex flex-col gap-1">
          <span
            aria-hidden="true"
            className="absolute top-1.5 -left-[1.9rem] size-2.5 rounded-full border-2 border-foreground bg-background"
          />
          <time dateTime={entry.occurredAt} className="text-xs text-muted-foreground">
            {formatDateTime(entry.occurredAt)}
          </time>
          <p className="font-medium">{describe(entry, viewer)}</p>
          {entry.note ? <p className="text-sm">Note: {entry.note}</p> : null}
        </li>
      ))}
    </ol>
  );
}

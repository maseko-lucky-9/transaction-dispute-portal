import type { DisputeStatus, TimelineEntry } from "@/api/disputes";
import { DISPUTE_STATUS_LABELS } from "@/lib/dispute-filters";
import { formatDateTime } from "@/lib/format";

const label = (status: DisputeStatus) => DISPUTE_STATUS_LABELS[status];

/**
 * Only the KIND of actor is ever known to a customer: "You" or "Back-office". An agent's
 * identity is not in the API response and must never be shown or guessed.
 */
function describe(entry: TimelineEntry): string {
  const isCustomer = entry.actorType === "Customer";
  const actor = isCustomer ? "You" : "Back-office";
  if (entry.from === null) {
    return isCustomer
      ? "You submitted this dispute"
      : `${actor} set the status to ${label(entry.to)}`;
  }
  if (isCustomer && entry.to === "Withdrawn") return "You withdrew this dispute";
  return `${actor} changed the status from ${label(entry.from)} to ${label(entry.to)}`;
}

/** Every status change, oldest first, with when it happened and the kind of actor. */
export function DisputeTimeline({ entries }: { entries: readonly TimelineEntry[] }) {
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
          <p className="font-medium">{describe(entry)}</p>
          {entry.note ? <p className="text-sm">Note: {entry.note}</p> : null}
        </li>
      ))}
    </ol>
  );
}

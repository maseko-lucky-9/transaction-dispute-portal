import { useMemo, useState } from "react";
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "@tanstack/react-router";

import { agentKeys, agentQueueInfiniteOptions, takeNextDispute } from "@/api/agent";
import { disputeReasonsOptions } from "@/api/disputes";
import { isApiError } from "@/api/problem";
import { ErrorState } from "@/components/error-state";
import { PagedResults } from "@/components/paged-results";
import { QueueTable } from "@/components/queue-table";
import { Button } from "@/components/ui/button";
import { useUser } from "@/components/user-context";
import type { AssignmentFilters, QueueAssignment } from "@/lib/agent-filters";
import { sendsAssignment } from "@/lib/assignment";
import { homeGreeting } from "@/lib/greeting";
import { CARD, LINK } from "@/lib/table-styles";

/** Each section's own short page; Load more gets the next. */
const SECTION_LIMIT = 10;

interface SectionCopy {
  heading: string;
  description: string;
  empty: string;
  firstLoadFailed: string;
  nextPageFailed: string;
  /** Accessible names: unique on the page, which holds three of each. */
  list: string;
  /** What the list holds, for its summary: "Unclaimed disputes: all loaded". */
  noun: { one: string; many: string };
  table: string;
  caption: string;
  action: "review" | "read";
  showClaimed: boolean;
}

/** The three sections, in the order an agent works: their own, then the pool, then colleagues'. */
const SECTIONS: readonly (SectionCopy & { assignment: QueueAssignment })[] = [
  {
    assignment: "mine",
    heading: "In review by you",
    description:
      "Disputes you have claimed. Only you can decide them or release them to the queue.",
    empty: "You are not reviewing a dispute. Take next, or open one from Unclaimed.",
    firstLoadFailed: "We could not load the disputes you are reviewing",
    nextPageFailed: "We could not load more of your disputes",
    list: "Your disputes list",
    noun: { one: "your dispute", many: "your disputes" },
    table: "Your disputes table",
    caption: "Disputes you are reviewing, oldest first",
    action: "review",
    showClaimed: true,
  },
  {
    assignment: "unclaimed",
    heading: "Unclaimed",
    description: "Waiting for an agent, oldest first. Start a review to claim one.",
    empty: "Nothing is waiting. New disputes appear here the moment a customer submits one.",
    firstLoadFailed: "We could not load the unclaimed disputes",
    nextPageFailed: "We could not load more unclaimed disputes",
    list: "Unclaimed disputes list",
    noun: { one: "unclaimed dispute", many: "unclaimed disputes" },
    table: "Unclaimed disputes table",
    caption: "Disputes nobody is reviewing yet, oldest first",
    action: "review",
    showClaimed: false,
  },
  {
    assignment: "others",
    heading: "Claimed by other agents",
    description: "Colleagues are reviewing these. You can read them, but not decide them.",
    empty: "No other agent is reviewing a dispute right now.",
    firstLoadFailed: "We could not load the disputes other agents are reviewing",
    nextPageFailed: "We could not load more of their disputes",
    list: "Other agents' disputes list",
    noun: { one: "other agents' dispute", many: "other agents' disputes" },
    table: "Other agents' disputes table",
    caption: "Disputes other agents are reviewing, oldest first",
    action: "read",
    showClaimed: true,
  },
];

/**
 * An agent's Home: take the next dispute in one press, then what they hold, what is waiting and
 * what colleagues hold. No counts or totals anywhere: the API returns none (.impeccable.md).
 * Take next is the page's only crimson control.
 */
export function AgentWorkbench() {
  const user = useUser();
  return (
    <div className="flex flex-col gap-6">
      <section
        aria-labelledby="home-heading"
        className="flex flex-wrap items-end justify-between gap-4"
      >
        <div className="flex flex-col gap-1.5">
          <h1 id="home-heading" className="text-[clamp(28px,4vw,40px)] font-bold tracking-tight">
            {homeGreeting(new Date().getHours(), user.name)}
          </h1>
          <p className="text-muted-foreground">
            Signed in as <strong className="text-foreground">{user.name}</strong>
          </p>
        </div>
        <Link to="/agent" className={`${LINK} text-sm`}>
          Open the full review queue
        </Link>
      </section>

      <TakeNext />

      {SECTIONS.map((section) => (
        <WorkbenchSection key={section.assignment} {...section} />
      ))}
    </div>
  );
}

const TAKE_NEXT_KEY = ["agent", "take-next"] as const;

/** Not an error to show as one: nothing waiting, or an older api with no take next. */
type Outcome = "nothing-waiting" | "unavailable" | null;

function TakeNext() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [outcome, setOutcome] = useState<Outcome>(null);

  const mutation = useMutation({
    mutationKey: TAKE_NEXT_KEY,
    mutationFn: takeNextDispute,
    onSuccess: async (taken) => {
      // A claim (or an empty pool) changes what every section shows.
      void queryClient.invalidateQueries({ queryKey: ["agent", "queue"] });
      if (taken === null) {
        setOutcome("nothing-waiting");
        return;
      }
      // The answer IS the claimed dispute with its ETag: the dispute page opens on it at once.
      queryClient.setQueryData(agentKeys.detail(taken.dispute.id), taken);
      await navigate({
        to: "/agent/disputes/$disputeId",
        params: { disputeId: taken.dispute.id },
      });
    },
    onError: (error) => {
      // 404 or 405: an older api with no take next, or a claim that could not be read back. Either
      // way, picking a dispute by hand still works: say that, not a failure.
      if (isApiError(error) && (error.status === 404 || error.status === 405)) {
        setOutcome("unavailable");
      }
    },
  });

  // Ask the cache, not `mutation.isPending`: that only changes after a render, so a double click
  // would see "not pending" twice and claim two disputes.
  function take() {
    if (queryClient.isMutating({ mutationKey: TAKE_NEXT_KEY }) > 0) return;
    setOutcome(null);
    mutation.reset();
    mutation.mutate();
  }

  const failed = mutation.isError && outcome !== "unavailable";

  return (
    <section aria-labelledby="take-next-heading" className={CARD}>
      <div className="flex flex-col gap-1">
        <h2 id="take-next-heading" className="text-lg font-semibold">
          Next dispute
        </h2>
        <p className="text-sm text-muted-foreground">
          Claims the oldest dispute nobody is reviewing and opens it for you.
        </p>
      </div>
      <Button
        type="button"
        className="self-start"
        aria-disabled={mutation.isPending ? true : undefined}
        onClick={take}
      >
        {mutation.isPending ? "Taking..." : "Take next"}
      </Button>
      <p role="status" className="text-sm font-medium">
        {outcome === "nothing-waiting"
          ? "Nothing is waiting for review right now."
          : outcome === "unavailable"
            ? "Take next isn't available right now. Pick a dispute from Unclaimed instead."
            : ""}
      </p>
      {failed ? (
        <ErrorState
          error={mutation.error}
          title="We could not take the next dispute"
          onRetry={take}
        />
      ) : null}
    </section>
  );
}

function WorkbenchSection(props: SectionCopy & { assignment: QueueAssignment }) {
  const { assignment } = props;
  const filters = useMemo<AssignmentFilters>(
    () => ({ assignment, limit: SECTION_LIMIT }),
    [assignment],
  );
  const options = useMemo(() => agentQueueInfiniteOptions(filters), [filters]);
  const query = useInfiniteQuery(options);
  const reasons = useQuery(disputeReasonsOptions);
  const headingId = `section-${assignment}-heading`;

  // An older api ignores the filter and sends the whole open queue with no assignment: only
  // "unclaimed" can still be told apart (it is the Submitted rows); the others cannot.
  const legacy =
    query.data?.pages.some((page) => !page.items.every((item) => sendsAssignment(item))) ?? false;

  return (
    <section aria-labelledby={headingId} className={CARD}>
      <div className="flex flex-col gap-1">
        <h2 id={headingId} className="text-lg font-semibold">
          {props.heading}
        </h2>
        <p className="text-sm text-muted-foreground">{props.description}</p>
      </div>
      {legacy && assignment !== "unclaimed" ? (
        <p className="text-sm">
          We cannot show who is reviewing what yet.{" "}
          <Link to="/agent" className={LINK}>
            The review queue
          </Link>{" "}
          lists every open dispute.
        </p>
      ) : (
        <PagedResults
          query={query}
          queryKey={options.queryKey}
          filtersKey={assignment}
          label={props.list}
          noun={props.noun}
          titles={{
            firstLoadFailed: props.firstLoadFailed,
            nextPageFailed: props.nextPageFailed,
          }}
          countless
          empty={<SectionEmpty text={props.empty} />}
          renderItems={(items) => {
            const rows = legacy ? items.filter((item) => item.status === "Submitted") : items;
            if (rows.length === 0) {
              // Filtered here, so an empty page is not an empty list while the cursor runs on.
              return query.hasNextPage ? (
                <p className="text-sm text-muted-foreground">
                  None of these are waiting. Load more to look further back.
                </p>
              ) : (
                <SectionEmpty text={props.empty} />
              );
            }
            return (
              <QueueTable
                items={rows}
                reasons={reasons.data}
                label={props.table}
                caption={props.caption}
                action={props.action}
                showClaimed={props.showClaimed}
              />
            );
          }}
        />
      )}
    </section>
  );
}

function SectionEmpty({ text }: { text: string }) {
  return (
    <div className="rounded-xl bg-accent p-6 text-center">
      <p className="font-medium">{text}</p>
    </div>
  );
}

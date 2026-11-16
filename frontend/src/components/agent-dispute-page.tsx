import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getRouteApi, Link } from "@tanstack/react-router";

import {
  agentDisputeOptions,
  agentKeys,
  transitionDispute,
  type AgentDisputeWithEtag,
} from "@/api/agent";
import { disputeReasonsOptions } from "@/api/disputes";
import { isApiError } from "@/api/problem";
import { AgentCustomerTransactions } from "@/components/agent-customer-transactions";
import { DisputeOverview } from "@/components/dispute-overview";
import { LifecycleStepper } from "@/components/lifecycle-stepper";
import { DisputeStatusPill } from "@/components/status-pill";
import { DisputeTimeline } from "@/components/dispute-timeline";
import { ErrorState } from "@/components/error-state";
import { Fact } from "@/components/fact";
import { FormField } from "@/components/form-field";
import { TraceReference } from "@/components/trace-reference";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { isUnderReview } from "@/lib/agent-filters";
import { assignmentLine, assignmentOf, reviewedByAnother } from "@/lib/assignment";
import { DESCRIPTION_MAX } from "@/lib/dispute-form";
import { formatDateTime } from "@/lib/format";
import { freeTextField } from "@/lib/free-text";
import { isGuid } from "@/lib/guid";
import {
  noteIsRequired,
  optionsFor,
  type TransitionOption,
  type TransitionTarget,
} from "@/lib/transitions";
import { BACK_LINK, CARD, LINK, PAGE_TITLE } from "@/lib/table-styles";

// getRouteApi avoids importing the route here (the route imports this component).
const route = getRouteApi("/agent/disputes/$disputeId");

export function AgentDisputePage() {
  const { disputeId } = route.useParams();
  const validId = isGuid(disputeId);
  const query = useQuery({ ...agentDisputeOptions(disputeId), enabled: validId });
  const reasons = useQuery(disputeReasonsOptions);
  // Lives here, not in the form: a decision remounts the form (new status), the message stays.
  const [message, setMessage] = useState<string | null>(null);
  // Also here: a reload that changes the status remounts the form, and the note typed must survive.
  const [note, setNote] = useState("");

  if (!validId || (isApiError(query.error) && query.error.status === 404)) {
    return (
      <div className="flex flex-col items-start gap-3">
        <h1 className={PAGE_TITLE}>Dispute not found</h1>
        <p className="text-muted-foreground">We could not find that dispute.</p>
        <Link to="/agent" className={LINK}>
          Back to the review queue
        </Link>
      </div>
    );
  }
  if (query.isPending) {
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-4">
        <span className="sr-only">Loading dispute...</span>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }
  if (!query.data) {
    return (
      <ErrorState
        error={query.error}
        title="We could not load this dispute"
        headingLevel="h1"
        onRetry={() => void query.refetch()}
      />
    );
  }

  const { dispute } = query.data;
  const assignment = assignmentOf(dispute);
  const reviewer = assignmentLine(assignment);
  return (
    <div className="flex flex-col gap-6">
      <title>Dispute review - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-2">
        <Link to="/agent" className={BACK_LINK}>
          Back to the review queue
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className={PAGE_TITLE}>Dispute: {dispute.transaction.merchantName}</h1>
          <DisputeStatusPill status={dispute.status} />
          <LifecycleStepper
            status={dispute.status}
            reviewStarted={dispute.timeline.some((entry) => entry.to === "UnderReview")}
          />
        </div>
        <p className="text-sm text-muted-foreground">Raised {formatDateTime(dispute.createdAt)}</p>
        {/* Absent from an older api, and once decided: then nothing is said rather than guessed. */}
        {reviewer ? <p className="text-sm font-semibold">{reviewer}</p> : null}
      </div>

      <DisputeOverview
        dispute={dispute}
        reasons={reasons.data}
        summaryHeading="The dispute"
        descriptionHeading="What the customer wrote"
        extraFacts={
          <>
            <Fact term="Customer" value={dispute.customerDisplayName} />
            <Fact term="Account" value={dispute.maskedAccountNumber} />
          </>
        }
        timeline={<DisputeTimeline entries={dispute.timeline} viewer="agent" />}
      />

      {/* Only while under review (the API refuses a closed dispute). Keyed by status (and not like the form below: siblings must not share a key): Start review changes this dispute's own row, so the list is read afresh; a decision that closes the dispute unmounts it, with no read. */}
      {isUnderReview(dispute.status) ? (
        <AgentCustomerTransactions
          key={`transactions-${dispute.status}`}
          disputeId={dispute.id}
          transactionId={dispute.transactionId}
        />
      ) : null}

      {/* Only the reviewing agent may decide (ADR-019): a colleague's review is read only. Otherwise keyed by status and reviewer: a new version of the dispute starts a fresh form. */}
      {dispute.status === "UnderReview" && reviewedByAnother(assignment) ? (
        <ReadOnlyDecision message={message} />
      ) : (
        <TransitionForm
          key={`${dispute.status}-${assignment.kind}`}
          id={dispute.id}
          current={query.data}
          options={optionsFor(dispute.status, assignment)}
          message={message}
          setMessage={setMessage}
          note={note}
          setNote={setNote}
        />
      )}
    </div>
  );
}

const NOTE_ID = "transition-note";

/** Another agent holds the review: this one reads, and is told why there is nothing to press. */
function ReadOnlyDecision({ message }: { message: string | null }) {
  return (
    <section aria-labelledby="transition-heading" className={CARD}>
      <h2 id="transition-heading" className="text-lg font-semibold">
        Decision
      </h2>
      <p role="status" className="text-sm font-medium">
        {message ?? ""}
      </p>
      <p className="text-sm text-muted-foreground">
        Another agent is reviewing this dispute. You can read it but not decide it.
      </p>
    </section>
  );
}

/** A 409 not_assignee, in plain words: someone else holds the review now. */
const NOT_ASSIGNEE =
  "Another agent is reviewing this dispute, so you cannot decide or release it. We reloaded it.";

/** A decision that asks for confirmation, with the ETag of the version it was chosen on. */
interface PendingDecision {
  option: TransitionOption;
  etag: string | null;
}

interface TransitionFormProps {
  id: string;
  current: AgentDisputeWithEtag;
  /** What this agent may do now (status and reviewer). */
  options: readonly TransitionOption[];
  message: string | null;
  setMessage: (message: string | null) => void;
  note: string;
  setNote: (note: string) => void;
}

function TransitionForm({
  id,
  current,
  options,
  message,
  setMessage,
  note,
  setNote,
}: TransitionFormProps) {
  const queryClient = useQueryClient();
  const status = current.dispute.status;
  const required = noteIsRequired(status);
  const offersRelease = options.some((option) => !option.takesNote);
  const [noteError, setNoteError] = useState<string | undefined>();
  // A decision waiting for its confirmation keeps the version the agent chose it on.
  const [pending, setPending] = useState<PendingDecision | null>(null);

  const detailKey = agentKeys.detail(id);
  const mutation = useMutation({
    mutationKey: ["agent", "transition", id],
    // Strict If-Match: send the version the agent was SHOWN when they decided, passed in by the
    // caller. Never re-read the cache here: a refetch that landed since would send a newer ETag
    // and silently overwrite a change the agent never saw. A stale one is a 412 and a reload.
    mutationFn: (variables: { to: TransitionTarget; note: string; etag: string | null }) =>
      transitionDispute(id, variables.etag, variables.to, variables.note),
    onSuccess: async (result, variables) => {
      queryClient.setQueryData(detailKey, result);
      setPending(null);
      // The draft is spent either way: after a release it would otherwise go out with the next
      // Start review, as the claim's note to the customer.
      setNote("");
      setMessage(
        variables.to === "Submitted"
          ? "Released. The dispute is back in the queue for any agent."
          : "Status updated.",
      );
      await queryClient.invalidateQueries({ queryKey: ["agent", "queue"] });
    },
    onError: async (error) => {
      setPending(null);
      if (!isApiError(error)) return;
      if (error.status === 412) {
        await Promise.all([
          queryClient.refetchQueries({ queryKey: detailKey }),
          queryClient.invalidateQueries({ queryKey: ["agent", "queue"] }),
        ]);
        setMessage("This dispute changed while you were reviewing it. We reloaded it.");
      } else if (error.status === 409) {
        // Set first: when another agent holds the review, the reload swaps this form for the
        // read-only card, which shows the same message.
        setMessage(
          error.code === "not_assignee"
            ? NOT_ASSIGNEE
            : "That move is no longer possible. We reloaded the dispute.",
        );
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: detailKey }),
          queryClient.invalidateQueries({ queryKey: ["agent", "queue"] }),
        ]);
      }
    },
  });

  if (options.length === 0) {
    return (
      <section aria-labelledby="transition-heading" className={CARD}>
        <h2 id="transition-heading" className="text-lg font-semibold">
          Decision
        </h2>
        <p role="status" className="text-sm text-muted-foreground">
          {message ? `${message} ` : ""}
          This dispute is {status === "Withdrawn" ? "withdrawn" : "decided"}, so there is nothing
          more to do.
        </p>
      </section>
    );
  }

  const noteSchema = freeTextField({
    min: required ? 1 : 0,
    max: DESCRIPTION_MAX,
    minMessage: "Enter a note: the customer will see it",
    maxMessage: `Use at most ${DESCRIPTION_MAX} characters`,
  });

  function choose(option: TransitionOption) {
    // A move that takes no note (a release) neither checks nor sends what is in the field.
    const parsed = option.takesNote ? noteSchema.safeParse(note) : null;
    if (parsed && !parsed.success) {
      setNoteError(parsed.error.issues[0]?.message ?? "Check the note");
      document.getElementById(NOTE_ID)?.focus();
      return;
    }
    setNoteError(undefined);
    setMessage(null);
    mutation.reset();
    if (option.confirm) setPending({ option, etag: current.etag });
    else send(option, current.etag);
  }

  function send(option: TransitionOption, etag: string | null) {
    if (queryClient.isMutating({ mutationKey: ["agent", "transition", id] }) > 0) return;
    mutation.mutate({ to: option.to, note: option.takesNote ? note.trim() : "", etag });
  }

  const error = mutation.error;
  const apiError = isApiError(error) ? error : null;
  // 400 problem documents name the field: `errors: { note: [...] }`.
  const serverNote = apiError?.status === 400 ? firstNoteError(apiError.problem["errors"]) : null;
  const handled = apiError?.status === 412 || apiError?.status === 409;
  const notAssignee = apiError?.code === "not_assignee";

  return (
    <section aria-labelledby="transition-heading" className={CARD}>
      <h2 id="transition-heading" className="text-lg font-semibold">
        Decision
      </h2>
      <p role="status" className="text-sm font-medium">
        {message ?? ""}
      </p>
      {handled ? (
        <div role="alert" className="flex flex-col gap-1 rounded-xl bg-accent p-4">
          <p className="font-medium">{apiError?.status === 412 ? "Reloaded" : "Not possible"}</p>
          <p className="text-sm">
            {notAssignee
              ? "Another agent is reviewing this dispute."
              : (apiError?.problem.detail ?? "Check the status and try again.")}
          </p>
          <TraceReference error={error} />
        </div>
      ) : error && !serverNote ? (
        <ErrorState error={error} title="We could not update this dispute" />
      ) : null}

      <form
        noValidate
        onSubmit={(event: FormEvent) => event.preventDefault()}
        className="flex max-w-xl flex-col gap-2"
      >
        <FormField
          id={NOTE_ID}
          label="Note to the customer"
          optional={!required}
          error={noteError ?? serverNote ?? undefined}
          hint={
            required
              ? `Required to accept or reject. The customer sees it. Do not include card or ID numbers.${offersRelease ? " Not sent when you release." : ""}`
              : "Optional. The customer sees it."
          }
        >
          {(described) => (
            <Textarea
              id={NOTE_ID}
              rows={4}
              value={note}
              aria-required={required ? "true" : undefined}
              aria-invalid={noteError || serverNote ? true : undefined}
              aria-describedby={described}
              onChange={(event) => setNote(event.target.value)}
            />
          )}
        </FormField>

        {pending ? (
          <div role="group" aria-label="Confirm decision" className="flex flex-col gap-3">
            <p className="font-medium">Do you want to {pending.option.verb} this dispute?</p>
            {pending.option.takesNote ? null : (
              <p className="text-sm text-muted-foreground">
                It goes back to Unclaimed, where any agent can claim it. No note is sent.
              </p>
            )}
            <div className="flex gap-2">
              {/* A ghost move (release) confirms outlined, so the answer still outranks Cancel. */}
              <Button
                type="button"
                variant={pending.option.variant === "ghost" ? "outline" : pending.option.variant}
                aria-disabled={mutation.isPending ? true : undefined}
                onClick={() => send(pending.option, pending.etag)}
              >
                {mutation.isPending ? "Saving..." : `Yes, ${pending.option.label.toLowerCase()}`}
              </Button>
              {/* Not while the move is in flight: the answer would land on a form that no longer asks. */}
              <Button
                type="button"
                variant="ghost"
                aria-disabled={mutation.isPending ? true : undefined}
                onClick={() => {
                  if (queryClient.isMutating({ mutationKey: ["agent", "transition", id] }) > 0) {
                    return;
                  }
                  setPending(null);
                }}
              >
                Cancel
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap gap-2">
            {options.map((option) => (
              <Button
                key={option.to}
                type="button"
                variant={option.variant}
                aria-disabled={mutation.isPending ? true : undefined}
                onClick={() => choose(option)}
              >
                {option.label}
              </Button>
            ))}
          </div>
        )}
      </form>
    </section>
  );
}

function firstNoteError(errors: unknown): string | null {
  if (typeof errors !== "object" || errors === null) return null;
  for (const [name, messages] of Object.entries(errors)) {
    if (name.toLowerCase() !== "note" || !Array.isArray(messages)) continue;
    const first = messages.find((m): m is string => typeof m === "string" && m.trim() !== "");
    if (first) return first;
  }
  return null;
}

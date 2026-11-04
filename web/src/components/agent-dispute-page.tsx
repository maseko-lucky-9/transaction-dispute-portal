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
import { DisputeOverview } from "@/components/dispute-overview";
import { DisputeStatusBadge } from "@/components/dispute-status-badge";
import { DisputeTimeline } from "@/components/dispute-timeline";
import { ErrorState } from "@/components/error-state";
import { Fact } from "@/components/fact";
import { FormField } from "@/components/form-field";
import { TraceReference } from "@/components/trace-reference";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { DESCRIPTION_MAX } from "@/lib/dispute-form";
import { formatDateTime } from "@/lib/format";
import { freeTextField } from "@/lib/free-text";
import { isGuid } from "@/lib/guid";
import {
  NEXT_TRANSITIONS,
  noteIsRequired,
  type TransitionOption,
  type TransitionTarget,
} from "@/lib/transitions";

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
        <h1 className="text-2xl font-semibold">Dispute not found</h1>
        <p className="text-muted-foreground">We could not find that dispute.</p>
        <Link to="/agent" className="underline underline-offset-4">
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
  return (
    <div className="flex flex-col gap-8">
      <title>Dispute review - Transactions Dispute Portal</title>
      <div className="flex flex-col gap-2">
        <Link to="/agent" className="text-sm underline underline-offset-4">
          Back to the review queue
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold">Dispute: {dispute.transaction.merchantName}</h1>
          <DisputeStatusBadge status={dispute.status} />
        </div>
        <p className="text-sm text-muted-foreground">Raised {formatDateTime(dispute.createdAt)}</p>
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

      {/* Keyed by status: a new version of the dispute starts a fresh form. */}
      <TransitionForm
        key={dispute.status}
        id={dispute.id}
        current={query.data}
        message={message}
        setMessage={setMessage}
        note={note}
        setNote={setNote}
      />
    </div>
  );
}

const NOTE_ID = "transition-note";

interface TransitionFormProps {
  id: string;
  current: AgentDisputeWithEtag;
  message: string | null;
  setMessage: (message: string | null) => void;
  note: string;
  setNote: (note: string) => void;
}

function TransitionForm({ id, current, message, setMessage, note, setNote }: TransitionFormProps) {
  const queryClient = useQueryClient();
  const status = current.dispute.status;
  const options = NEXT_TRANSITIONS[status];
  const required = noteIsRequired(status);
  const [noteError, setNoteError] = useState<string | undefined>();
  const [pending, setPending] = useState<TransitionOption | null>(null);

  const detailKey = agentKeys.detail(id);
  const mutation = useMutation({
    mutationKey: ["agent", "transition", id],
    // Read the ETag at call time: the latest one the cache holds.
    mutationFn: (variables: { to: TransitionTarget; note: string }) => {
      const cached = queryClient.getQueryData<AgentDisputeWithEtag>(detailKey) ?? current;
      return transitionDispute(id, cached.etag, variables.to, variables.note);
    },
    onSuccess: async (result) => {
      queryClient.setQueryData(detailKey, result);
      setPending(null);
      setNote("");
      setMessage("Status updated.");
      await queryClient.invalidateQueries({ queryKey: ["agent", "queue"] });
    },
    onError: async (error) => {
      setPending(null);
      if (!isApiError(error)) return;
      if (error.status === 412) {
        await queryClient.refetchQueries({ queryKey: detailKey });
        setMessage("This dispute changed while you were reviewing it. We reloaded it.");
      } else if (error.status === 409) {
        await queryClient.refetchQueries({ queryKey: detailKey });
        setMessage("That move is no longer possible. We reloaded the dispute.");
      }
    },
  });

  if (options.length === 0) {
    return (
      <section aria-labelledby="transition-heading" className="flex flex-col gap-2">
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
    const parsed = noteSchema.safeParse(note);
    if (!parsed.success) {
      setNoteError(parsed.error.issues[0]?.message ?? "Check the note");
      document.getElementById(NOTE_ID)?.focus();
      return;
    }
    setNoteError(undefined);
    setMessage(null);
    mutation.reset();
    if (option.confirm) setPending(option);
    else send(option);
  }

  function send(option: TransitionOption) {
    if (queryClient.isMutating({ mutationKey: ["agent", "transition", id] }) > 0) return;
    mutation.mutate({ to: option.to, note: note.trim() });
  }

  const error = mutation.error;
  const apiError = isApiError(error) ? error : null;
  // 400 problem documents name the field: `errors: { note: [...] }`.
  const serverNote = apiError?.status === 400 ? firstNoteError(apiError.problem["errors"]) : null;
  const handled = apiError?.status === 412 || apiError?.status === 409;

  return (
    <section aria-labelledby="transition-heading" className="flex flex-col gap-3">
      <h2 id="transition-heading" className="text-lg font-semibold">
        Decision
      </h2>
      <p role="status" className="text-sm font-medium">
        {message ?? ""}
      </p>
      {handled ? (
        <div role="alert" className="flex flex-col gap-1 rounded-xl border p-4">
          <p className="font-medium">{apiError?.status === 412 ? "Reloaded" : "Not possible"}</p>
          <p className="text-sm">{apiError?.problem.detail ?? "Check the status and try again."}</p>
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
              ? "Required to accept or reject. The customer sees it. Do not include card or ID numbers."
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
            <p className="font-medium">Do you want to {pending.verb} this dispute?</p>
            <div className="flex gap-2">
              <Button
                type="button"
                aria-disabled={mutation.isPending ? true : undefined}
                onClick={() => send(pending)}
              >
                {mutation.isPending ? "Saving..." : `Yes, ${pending.label.toLowerCase()}`}
              </Button>
              <Button type="button" variant="outline" onClick={() => setPending(null)}>
                Cancel
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex gap-2">
            {options.map((option) => (
              <Button
                key={option.to}
                type="button"
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

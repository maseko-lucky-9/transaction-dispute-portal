import { useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";

import { isApiError } from "@/api/problem";
import { disputeKeys, withdrawDispute, type DisputeWithEtag } from "@/api/disputes";
import { ErrorState } from "@/components/error-state";
import { TraceReference } from "@/components/trace-reference";
import { Button } from "@/components/ui/button";
import { WITHDRAWABLE_STATUSES } from "@/lib/dispute-filters";

type Outcome = "withdrawn" | "reloaded" | "already-closed" | null;

const OUTCOME_TEXT: Record<Exclude<Outcome, null>, string> = {
  withdrawn: "Dispute withdrawn.",
  reloaded: "Showing the latest version of this dispute.",
  "already-closed": "This dispute was already closed.",
};

/**
 * Withdraw a Submitted or Under-review dispute, with an inline confirmation (a real, focusable
 * part of the page: `window.confirm` is neither stylable nor reliably announced).
 *
 * The request carries `If-Match` with the ETag the page was read with, so a dispute an agent
 * has since moved on is refused (412) instead of silently overwritten.
 */
export function WithdrawDispute({ id, current }: { id: string; current: DisputeWithEtag }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const [outcome, setOutcome] = useState<Outcome>(null);
  const withdrawButtonRef = useRef<HTMLButtonElement>(null);
  const outcomeRef = useRef<HTMLParagraphElement>(null);

  const detailKey = disputeKeys.detail(id);
  const refreshOthers = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["disputes", "list"] }),
      // The transactions list shows each row's dispute status.
      queryClient.invalidateQueries({ queryKey: ["transactions"] }),
    ]);

  const withdrawKey = ["disputes", "withdraw", id] as const;
  // Ask the cache, not `mutation.isPending`: that only changes after a render, so two fast
  // activations (a double click, Enter held down) both still see "not pending" and both send.
  const startWithdraw = () => {
    if (queryClient.isMutating({ mutationKey: withdrawKey }) > 0) return;
    mutation.mutate();
  };

  const mutation = useMutation({
    mutationKey: withdrawKey,
    // Read the ETag at call time, not render time: it is the latest one the cache holds.
    mutationFn: () => {
      const cached = queryClient.getQueryData<DisputeWithEtag>(detailKey) ?? current;
      return withdrawDispute(id, cached.etag);
    },
    onSuccess: async (result) => {
      queryClient.setQueryData(detailKey, result);
      setConfirming(false);
      setOutcome("withdrawn");
      await refreshOthers();
      outcomeRef.current?.focus();
    },
    onError: async (error) => {
      // Already closed (409) means someone else got there first: show what is true now.
      if (isApiError(error) && error.code === "invalid_transition") {
        setConfirming(false);
        setOutcome("already-closed");
        await queryClient.invalidateQueries({ queryKey: detailKey });
        outcomeRef.current?.focus();
      }
    },
  });

  async function reload() {
    await queryClient.refetchQueries({ queryKey: detailKey });
    mutation.reset();
    setConfirming(false);
    setOutcome("reloaded");
    outcomeRef.current?.focus();
  }

  const error = mutation.error;
  const isStale = isApiError(error) && error.status === 412;
  const isClosed = isApiError(error) && error.code === "invalid_transition";
  const canWithdraw = WITHDRAWABLE_STATUSES.includes(current.dispute.status);

  return (
    <section aria-labelledby="withdraw-heading" className="flex flex-col gap-3">
      <h2 id="withdraw-heading" className="text-lg font-semibold">
        Withdraw
      </h2>

      {/* One live region for the outcome; focus moves here so the result is heard. */}
      <p ref={outcomeRef} role="status" tabIndex={-1} className="text-sm font-medium outline-none">
        {outcome ? OUTCOME_TEXT[outcome] : ""}
      </p>

      {isStale ? (
        <div
          role="alert"
          className="flex flex-col items-start gap-3 rounded-xl border border-destructive/50 p-4"
        >
          <p className="font-medium">This dispute changed. Reload to see the latest.</p>
          <TraceReference error={error} />
          <Button variant="outline" size="sm" onClick={() => void reload()}>
            Reload
          </Button>
        </div>
      ) : error && !isClosed ? (
        // 428 (a missing If-Match) cannot happen from this page: if it does it is a bug, and
        // the generic message with the reference is the honest thing to show.
        <ErrorState
          error={error}
          title="We could not withdraw this dispute"
          onRetry={startWithdraw}
        />
      ) : null}

      {canWithdraw && !confirming && !isStale ? (
        <div>
          <Button
            ref={withdrawButtonRef}
            variant="outline"
            onClick={() => {
              setOutcome(null);
              mutation.reset();
              setConfirming(true);
            }}
          >
            Withdraw dispute
          </Button>
        </div>
      ) : null}

      {canWithdraw && confirming && !isStale ? (
        <ConfirmWithdraw
          pending={mutation.isPending}
          onConfirm={startWithdraw}
          onCancel={() => {
            setConfirming(false);
            // Put focus back where the user came from.
            queueMicrotask(() => withdrawButtonRef.current?.focus());
          }}
        />
      ) : null}

      {!canWithdraw && !outcome ? (
        <p className="text-sm text-muted-foreground">
          This dispute can no longer be withdrawn because it has been decided or withdrawn.
        </p>
      ) : null}
    </section>
  );
}

function ConfirmWithdraw(props: { pending: boolean; onConfirm: () => void; onCancel: () => void }) {
  return (
    <div
      role="group"
      aria-labelledby="withdraw-confirm-heading"
      onKeyDown={(event) => {
        if (event.key === "Escape") props.onCancel();
      }}
      className="flex flex-col items-start gap-3 rounded-xl border p-4"
    >
      <p
        id="withdraw-confirm-heading"
        tabIndex={-1}
        // Land on the question so it is read before the choices.
        ref={(element) => element?.focus()}
        className="font-medium outline-none"
      >
        Withdraw this dispute?
      </p>
      <p className="text-sm text-muted-foreground">
        The bank will stop reviewing it and you cannot undo this. You can raise a new dispute for
        the same transaction afterwards.
      </p>
      <div className="flex gap-2">
        <Button
          variant="destructive"
          aria-disabled={props.pending ? true : undefined}
          onClick={props.onConfirm}
        >
          {props.pending ? "Withdrawing..." : "Yes, withdraw"}
        </Button>
        <Button
          variant="outline"
          aria-disabled={props.pending ? true : undefined}
          onClick={() => !props.pending && props.onCancel()}
        >
          Keep dispute
        </Button>
      </div>
    </div>
  );
}

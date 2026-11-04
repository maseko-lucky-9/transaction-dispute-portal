import { useMemo, useState, type ReactNode } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate } from "@tanstack/react-router";
import { useForm, useWatch, type Resolver } from "react-hook-form";

import { createDispute, disputeKeys, type DisputeReason } from "@/api/disputes";
import { isApiError } from "@/api/problem";
import type { TransactionItem } from "@/api/transactions";
import { ErrorState } from "@/components/error-state";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { normalizeAmount } from "@/lib/decimal";
import {
  DESCRIPTION_MAX,
  DESCRIPTION_MIN,
  describeRemaining,
  disputeFormSchema,
  type DisputeFormValues,
} from "@/lib/dispute-form";
import { classifyCreateError, type CreateFailure, type DisputeField } from "@/lib/dispute-errors";
import { formatMoney } from "@/lib/format";
import { isGuid } from "@/lib/guid";
import { newUuid } from "@/lib/uuid";

interface DisputeFormProps {
  transaction: TransactionItem;
  reasons: readonly DisputeReason[];
}

interface Failed {
  failure: CreateFailure;
  error: unknown;
}

const CREATE_KEY = ["disputes", "create"] as const;
const FIELD_ORDER: readonly DisputeField[] = ["reasonCode", "description", "amount"];
const LINK = "underline underline-offset-4";

function focusOnMount(element: HTMLElement | null) {
  element?.focus();
}

/**
 * Validates on blur, then on every change. Submitting while invalid sends nothing and focuses
 * the first invalid field.
 *
 * The dispute id is made ONCE, when the form is opened, and every attempt carries it. If a
 * response is lost and the customer presses submit again, the server sees the same id and
 * answers with the dispute it already made (200) instead of making a second one.
 */
export function DisputeForm({ transaction, reasons }: DisputeFormProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [disputeId] = useState(newUuid);
  const [failed, setFailed] = useState<Failed | null>(null);

  const schema = useMemo(
    () =>
      disputeFormSchema({
        reasonCodes: reasons.map((reason) => reason.code),
        maxAmount: transaction.amount,
        currency: transaction.currency,
      }),
    [reasons, transaction.amount, transaction.currency],
  );
  const resolver = useMemo<Resolver<DisputeFormValues>>(
    () => (values, context, options) => zodResolver(schema)(values, context, options),
    [schema],
  );

  const {
    register,
    handleSubmit,
    setError,
    control,
    formState: { errors },
  } = useForm<DisputeFormValues>({
    resolver,
    mode: "onTouched",
    defaultValues: { reasonCode: "", description: "", amount: "" },
  });

  const mutation = useMutation({
    mutationKey: CREATE_KEY,
    mutationFn: createDispute,
    onSuccess: async ({ dispute, etag }) => {
      // The detail page opens on this (it revalidates in the background) and keeps the ETag
      // it will need to withdraw.
      queryClient.setQueryData(disputeKeys.detail(dispute.id), { dispute, etag });
      void queryClient.invalidateQueries({ queryKey: ["disputes", "list"] });
      void queryClient.invalidateQueries({ queryKey: ["transactions"] });
      await navigate({
        to: "/disputes/$disputeId",
        params: { disputeId: dispute.id },
        state: { disputeSubmitted: true },
      });
    },
    onError: (error) => {
      const failure = classifyCreateError(error);
      if (failure.kind === "fields") {
        let focused = false;
        for (const name of FIELD_ORDER) {
          const message = failure.fields[name];
          if (message === undefined) continue;
          setError(name, { type: "server", message }, { shouldFocus: !focused });
          focused = true;
        }
      }
      setFailed({ failure, error });
    },
  });
  const pending = mutation.isPending;

  const submit = handleSubmit((values) => {
    // aria-disabled still lets Enter submit, so the guard lives here, not on the button. It asks
    // the cache, which knows the instant a request starts; `pending` only follows a render.
    if (queryClient.isMutating({ mutationKey: CREATE_KEY }) > 0) return;
    setFailed(null);
    const amount = values.amount === "" ? null : normalizeAmount(values.amount);
    mutation.mutate({
      id: disputeId,
      transactionId: transaction.id,
      reasonCode: values.reasonCode,
      description: values.description, // already trimmed by the schema
      ...(amount === null ? {} : { amount }),
    });
  });

  const reasonCode = useWatch({ control, name: "reasonCode" });
  const description = useWatch({ control, name: "description" });
  const selectedReason = reasons.find((reason) => reason.code === reasonCode);
  // Counted the way the server counts it: on the trimmed text.
  const length = description.trim().length;
  const overLimit = length > DESCRIPTION_MAX;

  return (
    <form
      aria-label="Dispute this transaction"
      noValidate
      onSubmit={(event) => void submit(event)}
      className="flex flex-col gap-5"
    >
      <Field
        id="dispute-reason"
        label="Reason"
        error={errors.reasonCode?.message}
        hint={selectedReason?.description}
      >
        {(described) => (
          <Select
            id="dispute-reason"
            aria-required="true"
            aria-invalid={errors.reasonCode ? true : undefined}
            aria-describedby={described}
            {...register("reasonCode")}
          >
            <option value="">Choose a reason</option>
            {reasons.map((reason) => (
              <option key={reason.code} value={reason.code}>
                {reason.label}
              </option>
            ))}
          </Select>
        )}
      </Field>

      <Field
        id="dispute-description"
        label="What happened?"
        error={errors.description?.message}
        hint={`In your own words, ${DESCRIPTION_MIN} to ${DESCRIPTION_MAX} characters. Do not include card or ID numbers.`}
        footer={
          <>
            <span className={overLimit ? "text-destructive" : "text-muted-foreground"}>
              {length} / {DESCRIPTION_MAX}
            </span>
            <span
              role="status"
              aria-label="Description length"
              aria-live="polite"
              aria-atomic="true"
              className="sr-only"
            >
              {describeRemaining(length)}
            </span>
          </>
        }
      >
        {(described) => (
          <Textarea
            id="dispute-description"
            rows={6}
            aria-required="true"
            aria-invalid={errors.description ? true : undefined}
            aria-describedby={described}
            {...register("description")}
          />
        )}
      </Field>

      <Field
        id="dispute-amount"
        label="Amount to dispute"
        optional
        error={errors.amount?.message}
        hint={`Leave empty to dispute all ${formatMoney(transaction.amount, transaction.currency)}, the most you can dispute. Use at most 2 decimals, for example 49.99.`}
      >
        {(described) => (
          <Input
            id="dispute-amount"
            inputMode="decimal"
            autoComplete="off"
            className="sm:max-w-48"
            aria-invalid={errors.amount ? true : undefined}
            aria-describedby={described}
            {...register("amount")}
          />
        )}
      </Field>

      {failed ? (
        <FailureNotice key={mutation.submittedAt} failed={failed} retry={() => void submit()} />
      ) : null}

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" aria-disabled={pending ? "true" : undefined}>
          {pending ? "Submitting..." : "Submit dispute"}
        </Button>
        <Button asChild variant="outline">
          <Link to="/transactions">Cancel</Link>
        </Button>
        <p role="status" aria-label="Submission status" className="text-sm text-muted-foreground">
          {pending ? "Submitting your dispute..." : ""}
        </p>
      </div>
      {failed?.failure.kind === "fields" ? <Reference error={failed.error} /> : null}
    </form>
  );
}

interface FieldProps {
  id: string;
  label: string;
  optional?: boolean;
  error: string | undefined;
  hint?: string | undefined;
  footer?: ReactNode;
  /** Receives the `aria-describedby` value: the hint, then the error when there is one. */
  children: (described: string) => ReactNode;
}

function Field({ id, label, optional, error, hint, footer, children }: FieldProps) {
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const described = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(" ");
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>
        {label}{" "}
        <span className="font-normal text-muted-foreground">
          {optional ? "(optional)" : "(required)"}
        </span>
      </Label>
      {children(described)}
      {hint ? (
        <p id={hintId} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {footer ? <div className="flex justify-end gap-2 text-sm">{footer}</div> : null}
      {/*
        A fixed-height slot: an error appearing on blur must not move the Submit button, or the
        mouse-down that blurs the field would shift it away before the mouse-up and the click
        would be dropped.
      */}
      <div className="min-h-10">
        {error ? (
          <p id={errorId} role="alert" className="text-sm leading-5 text-destructive">
            {error}
          </p>
        ) : null}
      </div>
    </div>
  );
}

function Reference({ error }: { error: unknown }) {
  const traceId = isApiError(error) ? error.traceId : undefined;
  return traceId ? (
    <p className="text-sm text-muted-foreground">
      Reference: <code className="font-mono text-foreground">{traceId}</code>
    </p>
  ) : null;
}

function plural(count: number, noun: string) {
  return `${count} ${noun}${count === 1 ? "" : "s"}`;
}

/** Every failure that is not a field message: what happened, what to do, and the Reference. */
function FailureNotice({ failed, retry }: { failed: Failed; retry: () => void }) {
  const { failure, error } = failed;
  const offline = isApiError(error) && error.status === 0;
  // Field errors are announced beside their fields (and focused); only unmapped messages land here.
  if (failure.kind === "fields" && failure.other.length === 0) return null;

  const box = (title: string, message: string, extra?: ReactNode, onRetry?: () => void) => (
    <div
      tabIndex={-1}
      ref={failure.kind === "fields" ? undefined : focusOnMount}
      className="outline-none"
    >
      <ErrorState error={error} title={title} message={message} {...(onRetry ? { onRetry } : {})}>
        {extra}
      </ErrorState>
    </div>
  );

  switch (failure.kind) {
    case "fields":
      return box(
        "Please check your dispute",
        "The server could not accept it:",
        <ul className="list-disc pl-5 text-sm">
          {failure.other.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>,
      );
    case "violations":
      return box(
        "This dispute can't be submitted",
        "It breaks these rules:",
        <ul className="list-disc pl-5 text-sm">
          {failure.messages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>,
      );
    case "exists":
      return box(
        "This transaction already has a dispute",
        "You can follow it, or withdraw it from its page.",
        failure.existingDisputeId && isGuid(failure.existingDisputeId) ? (
          <Link
            to="/disputes/$disputeId"
            params={{ disputeId: failure.existingDisputeId }}
            className={LINK}
          >
            View the existing dispute
          </Link>
        ) : (
          <Link to="/disputes" className={LINK}>
            View my disputes
          </Link>
        ),
      );
    case "idConflict":
      return box(
        "We may already have this request",
        "An earlier attempt may have gone through. Check your disputes. If it is not there, reload this page and try again.",
        <Link to="/disputes" className={LINK}>
          View my disputes
        </Link>,
      );
    case "notFound":
      return box(
        "Transaction not found",
        "We could not find this transaction. It may belong to someone else or no longer exist.",
        <Link to="/transactions" className={LINK}>
          Back to my transactions
        </Link>,
      );
    case "rateLimited":
      return box(
        "Too many attempts",
        failure.retryAfterSeconds === null
          ? "Too many attempts, try again in a moment."
          : `Too many attempts, try again in ${plural(failure.retryAfterSeconds, "second")}.`,
      );
    case "failed":
      return box(
        "We could not submit your dispute",
        `${offline ? "We could not reach the server. " : ""}Your details are kept. Trying again will not create a second dispute.`,
        undefined,
        retry,
      );
  }
}

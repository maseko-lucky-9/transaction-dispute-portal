import { useEffect, type ChangeEvent, type FocusEvent, type ReactNode } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm, type FieldErrors, type Resolver } from "react-hook-form";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import {
  filterFormSchema,
  TRANSACTION_STATUSES,
  type FilterFormValues,
  type TransactionStatus,
} from "@/lib/transaction-filters";

/** The filters as they live in the URL: undefined means "not set". */
export interface AppliedFilters {
  from?: string | undefined;
  to?: string | undefined;
  status?: TransactionStatus | undefined;
}

interface TransactionFiltersFormProps {
  applied: AppliedFilters;
  onApply: (filters: AppliedFilters) => void;
  onClear: () => void;
}

const INCOMPLETE_DATE = "Enter a complete date";
const DATE_FIELDS = ["from", "to"] as const;

/**
 * zod covers the rules; this adds what zod cannot see. A half-typed native date reports an
 * empty `value`, so only the element's own `validity.badInput` says it is incomplete.
 */
const resolveFilters: Resolver<FilterFormValues> = async (values, context, options) => {
  const result = await zodResolver(filterFormSchema)(values, context, options);
  const incomplete = DATE_FIELDS.filter((name) => {
    const element = options.fields[name]?.ref;
    return element instanceof HTMLInputElement && element.validity.badInput;
  });
  if (incomplete.length === 0) return result;

  const errors: FieldErrors<FilterFormValues> = { ...result.errors };
  for (const name of incomplete) errors[name] = { type: "badInput", message: INCOMPLETE_DATE };
  return { values: {}, errors };
};

function toFormValues(applied: AppliedFilters): FilterFormValues {
  return { from: applied.from ?? "", to: applied.to ?? "", status: applied.status ?? "" };
}

/**
 * Validates on blur, then on every change. Submitting while invalid sends nothing and
 * focuses the first invalid field; a valid submit hands the values to `onApply`, which writes
 * them to the URL (the list only ever reacts to the URL).
 */
export function TransactionFiltersForm({ applied, onApply, onClear }: TransactionFiltersFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    trigger,
    getValues,
    formState: { errors },
  } = useForm<FilterFormValues>({
    resolver: resolveFilters,
    mode: "onTouched",
    defaultValues: toFormValues(applied),
  });

  // The URL is the source of truth: Back/Forward and "Clear" must reach the inputs.
  useEffect(() => {
    reset({ from: applied.from ?? "", to: applied.to ?? "", status: applied.status ?? "" });
  }, [applied.from, applied.to, applied.status, reset]);

  /**
   * The range rule spans both dates, so an edit here can fix or break the error shown on the
   * other field. RHF's `deps` only fires once the edited field is itself validating, which
   * leaves a stale error behind until blur; this does it explicitly.
   */
  function dateField(name: "from" | "to", partner: "from" | "to") {
    const field = register(name);
    return {
      ...field,
      onChange: (event: ChangeEvent<HTMLInputElement>) => {
        void field.onChange(event);
        if (errors[partner]) void trigger(partner); // clears (or updates) a shown error
      },
      onBlur: (event: FocusEvent<HTMLInputElement>) => {
        void field.onBlur(event);
        if (getValues(partner)) void trigger(partner); // surfaces a conflict as soon as both are set
      },
    };
  }

  const submit = handleSubmit((values) =>
    onApply({
      from: values.from || undefined,
      to: values.to || undefined,
      status: values.status || undefined,
    }),
  );

  return (
    <form
      aria-label="Filter transactions"
      noValidate
      onSubmit={(event) => void submit(event)}
      className="flex flex-wrap items-start gap-4 rounded-xl border p-4"
    >
      <Field id="filter-from" label="From" error={errors.from?.message}>
        <Input
          id="filter-from"
          type="date"
          aria-invalid={errors.from ? true : undefined}
          aria-describedby={errors.from ? "filter-from-error" : undefined}
          {...dateField("from", "to")}
        />
      </Field>
      <Field id="filter-to" label="To" error={errors.to?.message}>
        <Input
          id="filter-to"
          type="date"
          aria-invalid={errors.to ? true : undefined}
          aria-describedby={errors.to ? "filter-to-error" : undefined}
          {...dateField("to", "from")}
        />
      </Field>
      <Field id="filter-status" label="Status" error={errors.status?.message}>
        <Select
          id="filter-status"
          aria-invalid={errors.status ? true : undefined}
          aria-describedby={errors.status ? "filter-status-error" : undefined}
          {...register("status")}
        >
          <option value="">All statuses</option>
          {TRANSACTION_STATUSES.map((status) => (
            <option key={status} value={status}>
              {status}
            </option>
          ))}
        </Select>
      </Field>
      {/* Aligned with the inputs (not the bottom of the error slots) via a label-height spacer. */}
      <div className="flex flex-col gap-1.5">
        <span aria-hidden="true" className="text-sm leading-none">
          &nbsp;
        </span>
        <div className="flex items-center gap-2">
          <Button type="submit">Apply filters</Button>
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              reset({ from: "", to: "", status: "" });
              onClear();
            }}
          >
            Clear filters
          </Button>
        </div>
      </div>
    </form>
  );
}

function Field(props: {
  id: string;
  label: string;
  error: string | undefined;
  children: ReactNode;
}) {
  return (
    <div className="flex w-52 flex-col gap-1.5">
      <Label htmlFor={props.id}>{props.label}</Label>
      {props.children}
      {/*
        A fixed-height slot: an error appearing on blur must not move anything. If it did, the
        mouse-down that blurs the field would shift the Apply button away before the
        mouse-up, and the browser would drop the click.
      */}
      <div className="min-h-10">
        {props.error ? (
          <p id={`${props.id}-error`} role="alert" className="text-sm leading-5 text-destructive">
            {props.error}
          </p>
        ) : null}
      </div>
    </div>
  );
}

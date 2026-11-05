import type { ReactNode } from "react";

import { Label } from "@/components/ui/label";
import { DESCRIPTION_MAX, describeRemaining } from "@/lib/dispute-form";

interface FormFieldProps {
  id: string;
  label: string;
  optional?: boolean;
  error: string | undefined;
  hint?: string | undefined;
  footer?: ReactNode;
  /** Receives the `aria-describedby` value: the hint, then the error when there is one. */
  children: (described: string) => ReactNode;
}

/**
 * A labelled field: "(required)" or "(optional)" in the label, a hint, a footer (the counter),
 * and a fixed-height error slot. Shared by every form so they behave and read the same.
 */
export function FormField({ id, label, optional, error, hint, footer, children }: FormFieldProps) {
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

/**
 * "n / 2000", plus a polite live region that speaks only when the remaining length crosses a
 * threshold (see describeRemaining), so typing is never narrated. `length` is the TRIMMED length.
 */
export function CharacterCounter({ length, label }: { length: number; label: string }) {
  return (
    <>
      <span className={length > DESCRIPTION_MAX ? "text-destructive" : "text-muted-foreground"}>
        {length} / {DESCRIPTION_MAX}
      </span>
      <span
        role="status"
        aria-label={label}
        aria-live="polite"
        aria-atomic="true"
        className="sr-only"
      >
        {describeRemaining(length)}
      </span>
    </>
  );
}

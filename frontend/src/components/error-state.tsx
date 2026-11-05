import type { ReactNode } from "react";
import { AlertTriangle } from "lucide-react";

import { isApiError } from "@/api/problem";
import { Button } from "@/components/ui/button";

interface ErrorStateProps {
  error: unknown;
  /** Heading; defaults to the problem title, then a generic line. */
  title?: string;
  /** Body text; defaults to the problem detail, then a generic line. */
  message?: string;
  /** Extra content under the message (a list of rules, a link to follow). */
  children?: ReactNode;
  /** Use "h1" when the error replaces the whole page, "h2" (default) when it sits inside one. */
  headingLevel?: "h1" | "h2";
  onRetry?: () => void;
}

const GENERIC_MESSAGE = "Something went wrong. Please try again.";

/**
 * Renders an API failure. The server's `traceId` is shown as "Reference: <id>" so a user can
 * quote it to support and an engineer can find the exact trace and log lines.
 */
export function ErrorState({
  error,
  title,
  message: messageOverride,
  children,
  headingLevel: Heading = "h2",
  onRetry,
}: ErrorStateProps) {
  const apiError = isApiError(error) ? error : null;
  const heading = title ?? apiError?.problem.title ?? "Something went wrong";
  // Only ApiError text is meant for users; any other Error message is an internal detail.
  const message = messageOverride ?? apiError?.problem.detail ?? GENERIC_MESSAGE;
  const traceId = apiError?.traceId;

  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-xl border border-destructive/50 bg-card p-6 text-card-foreground"
    >
      <div className="flex items-center gap-2 font-semibold">
        <AlertTriangle className="size-5 text-destructive" aria-hidden="true" />
        <Heading>{heading}</Heading>
      </div>
      <p className="text-sm">{message}</p>
      {children}
      {traceId ? (
        <p className="text-sm text-muted-foreground">
          Reference: <code className="font-mono text-foreground">{traceId}</code>
        </p>
      ) : null}
      {onRetry ? (
        <Button type="button" variant="outline" size="sm" onClick={onRetry}>
          Try again
        </Button>
      ) : null}
    </div>
  );
}

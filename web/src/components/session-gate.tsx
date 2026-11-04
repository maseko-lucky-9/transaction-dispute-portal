import type { ReactNode } from "react";

import { useSession } from "@/api/session";
import { ErrorState } from "@/components/error-state";
import { SignIn } from "@/components/sign-in";
import { Skeleton } from "@/components/ui/skeleton";
import { UserContext } from "@/components/user-context";

/**
 * Renders `children` only for a signed-in user.
 *
 * pending -> skeleton | session check failed -> error with Retry | 401 -> Sign in | 200 -> app
 * A background refetch that fails keeps showing the app; only a first-load failure blocks it.
 */
export function SessionGate({ children }: { children: ReactNode }) {
  const { data: user, error, isPending, refetch } = useSession();

  if (isPending) {
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-4 py-6">
        <span className="sr-only">Checking your session...</span>
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-4 w-72" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (error && user === undefined) {
    return (
      <ErrorState
        error={error}
        title="We could not check your session"
        headingLevel="h1"
        onRetry={() => void refetch()}
      />
    );
  }

  if (!user) return <SignIn />;

  return <UserContext.Provider value={user}>{children}</UserContext.Provider>;
}

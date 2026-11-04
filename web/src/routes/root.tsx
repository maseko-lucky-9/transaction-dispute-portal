import { createRootRoute } from "@tanstack/react-router";

import { ErrorState } from "@/components/error-state";
import { NotFound } from "@/components/not-found";
import { RootLayout } from "@/components/root-layout";

export const rootRoute = createRootRoute({
  component: RootLayout,
  notFoundComponent: NotFound,
  errorComponent: ({ error, reset }) => (
    <ErrorState error={error} headingLevel="h1" onRetry={reset} />
  ),
});

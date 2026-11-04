import type { ReactNode } from "react";
import { useMatches } from "@tanstack/react-router";

import { useUser } from "@/components/user-context";
import { WrongArea } from "@/components/wrong-area";
import { accessFor } from "@/lib/roles";

/**
 * Renders the matched page only if the session's role may use its area (`staticData.area` on
 * the route). A refused page is replaced by an explanation and never mounts, so it cannot fire
 * a request. Routes with no area (Home, not-found) are open to every signed-in user.
 */
export function RoleGuard({ children }: { children: ReactNode }) {
  const user = useUser();
  const required = useMatches({
    select: (matches) => matches.map((match) => match.staticData.area).find(Boolean),
  });
  const access = accessFor(user);

  if (required === undefined || access === required) return <>{children}</>;
  return <WrongArea required={required} access={access} />;
}

import type { ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { LogOut } from "lucide-react";

import { logout, useSession } from "@/api/session";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";
import { accessFor, type Access } from "@/lib/roles";

const NAV_LINK =
  "text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:underline";
const NAV_ACTIVE = "font-medium text-foreground underline";

/** Header (name, section links, theme, user, logout) around the page content. */
export function AppShell({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const { data: user } = useSession();

  return (
    <div className="flex min-h-screen flex-col">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-10 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-primary-foreground"
      >
        Skip to content
      </a>
      <header className="border-b">
        <div className="mx-auto flex h-14 max-w-5xl items-center justify-between gap-4 px-4">
          <div className="flex items-center gap-6">
            <Link to="/" className="font-semibold outline-none focus-visible:underline">
              Transactions Dispute Portal
            </Link>
            {user ? <PrimaryNav access={accessFor(user)} /> : null}
          </div>
          <div className="flex items-center gap-2">
            <ThemeToggle />
            {user ? (
              <>
                <span className="text-sm" data-testid="header-user">
                  {user.name}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => logout(queryClient, user.logoutUrl)}
                >
                  <LogOut aria-hidden="true" />
                  Logout
                </Button>
              </>
            ) : null}
          </div>
        </div>
      </header>
      <main id="main" className="mx-auto w-full max-w-5xl flex-1 px-4 py-8">
        {children}
      </main>
    </div>
  );
}

/**
 * The section links for the session's role: customer pages for a customer, ONLY the queue for an
 * agent (customer pages would be refused), nothing for a refused both-roles session. The router
 * marks the current section's link aria-current="page" by itself.
 */
function PrimaryNav({ access }: { access: Access }) {
  if (access === "both") return null;
  return (
    <nav aria-label="Primary" className="flex items-center gap-4 text-sm">
      {access === "agent" ? (
        <Link to="/agent" className={NAV_LINK} activeProps={{ className: NAV_ACTIVE }}>
          Queue
        </Link>
      ) : (
        <>
          <Link to="/transactions" className={NAV_LINK} activeProps={{ className: NAV_ACTIVE }}>
            Transactions
          </Link>
          <Link to="/disputes" className={NAV_LINK} activeProps={{ className: NAV_ACTIVE }}>
            Disputes
          </Link>
        </>
      )}
    </nav>
  );
}

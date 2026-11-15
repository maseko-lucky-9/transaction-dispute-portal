import type { ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { LogOut } from "lucide-react";

import { logout, useSession } from "@/api/session";
import { BrandMark } from "@/components/brand-mark";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";
import { accessFor, type Access } from "@/lib/roles";

// Pill links: a light fill, and the solid active pill keyed off the router's aria-current.
const NAV_LINK =
  "rounded-full bg-background px-4 py-2 text-sm font-semibold text-foreground outline-none hover:bg-accent hover:ring-1 hover:ring-border focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-[current=page]:bg-nav-active aria-[current=page]:text-nav-active-foreground aria-[current=page]:hover:bg-nav-active aria-[current=page]:hover:ring-0";

/** Header (name, section links, theme, user, logout) around the page content. */
export function AppShell({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const { data: user } = useSession();

  return (
    <div className="mx-auto flex min-h-screen w-full max-w-6xl flex-col gap-7 px-4 pt-5 pb-8">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-10 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-primary-foreground"
      >
        Skip to content
      </a>
      <header className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-3xl bg-card p-2 shadow-card sm:rounded-full">
        <Link
          to="/"
          className="mr-auto inline-flex min-w-0 items-center gap-2.5 rounded-full font-semibold outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
        >
          <BrandMark />
          <span>Transactions Dispute Portal</span>
        </Link>
        {user ? <PrimaryNav access={accessFor(user)} /> : null}
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
      </header>
      <main id="main" className="w-full min-w-0 flex-1">
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
    <nav aria-label="Primary" className="flex flex-wrap items-center gap-1.5">
      {access === "agent" ? (
        <Link to="/agent" className={NAV_LINK}>
          Queue
        </Link>
      ) : (
        <>
          <Link to="/transactions" className={NAV_LINK}>
            Transactions
          </Link>
          <Link to="/disputes" className={NAV_LINK}>
            Disputes
          </Link>
        </>
      )}
    </nav>
  );
}

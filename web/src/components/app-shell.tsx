import type { ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { LogOut } from "lucide-react";

import { logout, useSession } from "@/api/session";
import { ThemeToggle } from "@/components/theme-toggle";
import { Button } from "@/components/ui/button";

/** Header (name, theme, user, logout) around the page content. */
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
          <span className="font-semibold">Transactions Dispute Portal</span>
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

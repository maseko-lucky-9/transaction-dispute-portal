import { Outlet } from "@tanstack/react-router";

import { AppShell } from "@/components/app-shell";
import { SessionGate } from "@/components/session-gate";

export function RootLayout() {
  return (
    <AppShell>
      <SessionGate>
        <Outlet />
      </SessionGate>
    </AppShell>
  );
}

import { Outlet } from "@tanstack/react-router";

import { AppShell } from "@/components/app-shell";
import { RoleGuard } from "@/components/role-guard";
import { SessionGate } from "@/components/session-gate";

export function RootLayout() {
  return (
    <AppShell>
      <SessionGate>
        <RoleGuard>
          <Outlet />
        </RoleGuard>
      </SessionGate>
    </AppShell>
  );
}

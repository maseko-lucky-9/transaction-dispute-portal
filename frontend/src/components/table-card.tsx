import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

interface TableCardProps {
  /** Accessible name of the scrollable region, e.g. "Transactions table". */
  label: string;
  /** Inside a card that already provides the surface (a filter bar sits above the table). */
  flat?: boolean;
  children: ReactNode;
}

/**
 * The scrollable region around a table. As a card it is white, rounded and softly shadowed;
 * `flat` drops that chrome for a table that lives inside a larger card.
 */
export function TableCard({ label, flat = false, children }: TableCardProps) {
  return (
    // A scrollable region must be reachable by keyboard (axe: scrollable-region-focusable).
    <div
      role="region"
      aria-label={label}
      tabIndex={0}
      className={cn("overflow-x-auto", !flat && "rounded-card bg-card shadow-card")}
    >
      {children}
    </div>
  );
}

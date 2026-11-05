import { Link } from "@tanstack/react-router";

import { buttonVariants } from "@/components/ui/button";
import type { Access, Area } from "@/lib/roles";

/**
 * Shown INSTEAD of a page the session's role may not use. The page component is never rendered,
 * so it makes no request: the UI does not attempt what the server would refuse (403).
 */
export function WrongArea({ required, access }: { required: Area; access: Access }) {
  const copy = COPY[access === "both" ? "both" : required];
  return (
    <div className="flex max-w-xl flex-col items-start gap-3">
      <title>{`${copy.heading} - Transactions Dispute Portal`}</title>
      <h1 className="text-2xl font-semibold">{copy.heading}</h1>
      <p className="text-muted-foreground">{copy.body}</p>
      {access === "agent" ? (
        <Link to="/agent" className={buttonVariants({ variant: "outline" })}>
          Go to the review queue
        </Link>
      ) : (
        <Link to="/" className={buttonVariants({ variant: "outline" })}>
          Back to home
        </Link>
      )}
    </div>
  );
}

const COPY: Record<Area | "both", { heading: string; body: string }> = {
  agent: {
    heading: "This area is for back-office agents",
    body: "Your account is a customer account, so the review queue is not available to you.",
  },
  customer: {
    heading: "This area is for customers",
    body: "Your account is a back-office account. Customer pages show a customer's own transactions and disputes, which agents do not act on.",
  },
  both: {
    heading: "Your account has both a customer and a back-office role",
    body: "That combination is not allowed, because an agent must not act as a customer. Ask an administrator to give the account one role.",
  },
};

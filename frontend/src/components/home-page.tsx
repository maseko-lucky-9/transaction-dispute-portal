import { Link } from "@tanstack/react-router";
import { ArrowRight } from "lucide-react";

import type { DisputeStatus } from "@/api/disputes";
import { DisputeStatusPill } from "@/components/status-pill";
import { TransactionResults } from "@/components/transaction-results";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { useUser } from "@/components/user-context";
import { homeGreeting } from "@/lib/greeting";
import { accessFor, CUSTOMER_ROLE, type Access } from "@/lib/roles";
import { CARD, LINK } from "@/lib/table-styles";
import type { TransactionFilters } from "@/lib/transaction-filters";
import { cn } from "@/lib/utils";

const LIFECYCLE: readonly { status: DisputeStatus; meaning: string }[] = [
  { status: "Submitted", meaning: "You raised it" },
  { status: "UnderReview", meaning: "Being checked" },
  { status: "Accepted", meaning: "The dispute was upheld" },
  { status: "Rejected", meaning: "The charge stands" },
  { status: "Withdrawn", meaning: "You cancelled it" },
];

/**
 * Home's own short list. A limit other than /transactions' default gives it a query key of its own,
 * so Home never replays the pages /transactions has loaded, and Load more here does not grow that
 * view. A constant, not an inline literal: the list memoizes on identity.
 */
const RECENT_TRANSACTIONS: TransactionFilters = { limit: 10 };

export function HomePage() {
  const user = useUser();
  const access = accessFor(user);
  // A session with no roles is treated as a customer, but the API refuses it the list (403): it
  // gets the "none assigned" hint instead of a list that cannot load.
  const showRecent = access === "customer" && user.roles.includes(CUSTOMER_ROLE);
  const showRoles = access !== "customer" || user.roles.length === 0;

  return (
    <div className="grid items-start gap-5 min-[960px]:grid-cols-[minmax(0,1fr)_320px]">
      <div className="grid min-w-0 gap-6">
        <section
          aria-labelledby="home-heading"
          className="flex flex-wrap items-end justify-between gap-4"
        >
          <div className="flex flex-col gap-1.5">
            <h1 id="home-heading" className="text-[clamp(28px,4vw,40px)] font-bold tracking-tight">
              {homeGreeting(new Date().getHours(), user.name)}
            </h1>
            <p className="text-muted-foreground">
              Signed in as <strong className="text-foreground">{user.name}</strong>
            </p>
          </div>
          {/* A customer has no use for their role names; the back-office, a refused session and one with no role do. */}
          {showRoles && (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm text-muted-foreground">Roles</span>
              {user.roles.length > 0 ? (
                <ul className="flex flex-wrap gap-2" aria-label="Roles">
                  {user.roles.map((role) => (
                    <li key={role}>
                      <Badge variant="secondary">{role}</Badge>
                    </li>
                  ))}
                </ul>
              ) : (
                <span className="text-sm text-muted-foreground">none assigned</span>
              )}
            </div>
          )}
        </section>

        <HomeLinks access={access} />
      </div>

      {access === "customer" && <LifecyclePanel />}
      {showRecent && <RecentTransactions />}
    </div>
  );
}

/** Full width under the cards: the table needs more room than the column beside the legend. */
function RecentTransactions() {
  return (
    <section aria-labelledby="recent-heading" className="grid min-w-0 gap-3 min-[960px]:col-span-2">
      <h2 id="recent-heading" className="text-lg font-semibold">
        Recent transactions
      </h2>
      <div className={CARD}>
        <TransactionResults filters={RECENT_TRANSACTIONS} />
        <Link to="/transactions" className={`${LINK} self-start text-sm`}>
          See all transactions and filters
        </Link>
      </div>
    </section>
  );
}

/** What each dispute status means, in the customer's words. No outcomes or timings promised. */
function LifecyclePanel() {
  return (
    <aside aria-labelledby="lifecycle-heading">
      <Card className="gap-4 p-5">
        <h2 id="lifecycle-heading" className="text-base font-semibold">
          How a dispute moves
        </h2>
        <ul className="grid gap-2.5">
          {LIFECYCLE.map(({ status, meaning }) => (
            <li key={status} className="flex items-center justify-between gap-2 text-sm">
              <DisputeStatusPill status={status} />
              <span className="text-muted-foreground">{meaning}</span>
            </li>
          ))}
        </ul>
      </Card>
    </aside>
  );
}

/** The cards for what this session may open: customer pages, or the queue, never both. */
function HomeLinks({ access }: { access: Access }) {
  if (access === "both") {
    return (
      <p role="alert" className="text-sm">
        This account has both a customer and a back-office role, which is not allowed. Ask an
        administrator to give it one.
      </p>
    );
  }
  return (
    <nav aria-label="Main">
      <ul className="grid gap-5 min-[700px]:grid-cols-2">
        {access === "agent" ? (
          <HomeCard
            primary
            to="/agent"
            title="Review queue"
            description="Open disputes across customers, oldest first. Start a review, accept or reject."
          />
        ) : (
          <>
            <HomeCard
              primary
              to="/transactions"
              title="My transactions"
              description="Browse your transactions and dispute a charge."
            />
            <HomeCard
              to="/disputes"
              title="My disputes"
              description="Follow your disputes and withdraw one if you need to."
            />
          </>
        )}
      </ul>
    </nav>
  );
}

function HomeCard(props: {
  to: "/agent" | "/transactions" | "/disputes";
  title: string;
  description: string;
  primary?: boolean;
}) {
  return (
    <li>
      <Card
        className={cn(
          "relative h-full min-h-40 justify-between p-6 transition-colors focus-within:ring-[3px] focus-within:ring-ring/50",
          props.primary ? "bg-primary text-primary-foreground" : "hover:bg-accent/50",
        )}
      >
        <h2 className="text-[22px] font-semibold">
          <Link
            to={props.to}
            className="flex items-center gap-2 outline-none after:absolute after:inset-0 after:content-['']"
          >
            {props.title}
            <ArrowRight aria-hidden="true" className="size-5" />
          </Link>
        </h2>
        <p
          className={cn(
            "text-sm",
            props.primary ? "text-primary-foreground" : "text-muted-foreground",
          )}
        >
          {props.description}
        </p>
      </Card>
    </li>
  );
}

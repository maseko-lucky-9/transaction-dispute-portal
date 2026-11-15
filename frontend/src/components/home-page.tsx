import { Link } from "@tanstack/react-router";
import { ArrowRight } from "lucide-react";

import type { DisputeStatus } from "@/api/disputes";
import { DisputeStatusPill } from "@/components/status-pill";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { useUser } from "@/components/user-context";
import { homeGreeting } from "@/lib/greeting";
import { accessFor, type Access } from "@/lib/roles";
import { cn } from "@/lib/utils";

const LIFECYCLE: readonly { status: DisputeStatus; meaning: string }[] = [
  { status: "Submitted", meaning: "You raised it" },
  { status: "UnderReview", meaning: "Being checked" },
  { status: "Accepted", meaning: "The dispute was upheld" },
  { status: "Rejected", meaning: "The charge stands" },
  { status: "Withdrawn", meaning: "You cancelled it" },
];

export function HomePage() {
  const user = useUser();
  const access = accessFor(user);

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
        </section>

        <HomeLinks access={access} />
      </div>

      {access === "customer" && <LifecyclePanel />}
    </div>
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

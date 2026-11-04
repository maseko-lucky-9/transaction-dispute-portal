import { Link } from "@tanstack/react-router";

import { Badge } from "@/components/ui/badge";
import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useUser } from "@/components/user-context";
import { accessFor, type Access } from "@/lib/roles";

export function HomePage() {
  const user = useUser();

  return (
    <div className="flex flex-col gap-8">
      <section aria-labelledby="home-heading" className="flex flex-col gap-3">
        <h1 id="home-heading" className="text-2xl font-semibold">
          Home
        </h1>
        <p>
          Signed in as <strong>{user.name}</strong>
        </p>
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

      <HomeLinks access={accessFor(user)} />
    </div>
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
      <ul className="grid gap-4 sm:grid-cols-2">
        {access === "agent" ? (
          <HomeCard
            to="/agent"
            title="Review queue"
            description="Open disputes across customers, oldest first. Start a review, accept or reject."
          />
        ) : (
          <>
            <HomeCard
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
}) {
  return (
    <li>
      <Card className="h-full transition-colors focus-within:ring-[3px] focus-within:ring-ring/50 hover:bg-accent/50">
        <CardHeader>
          <CardTitle as="h2" className="text-base">
            <Link to={props.to} className="underline-offset-4 outline-none hover:underline">
              {props.title}
            </Link>
          </CardTitle>
          <CardDescription>{props.description}</CardDescription>
        </CardHeader>
      </Card>
    </li>
  );
}

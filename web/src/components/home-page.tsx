import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useUser } from "@/components/user-context";

/**
 * Placeholders for pages that arrive in later PRs. They are not links yet: a role="link"
 * without an href, marked aria-disabled, is the ARIA pattern for a disabled link.
 */
const PLACEHOLDERS = [
  { label: "My transactions", description: "Browse your transactions and dispute a charge." },
  { label: "Agent queue", description: "Review and resolve customer disputes." },
] as const;

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

      <nav aria-label="Main">
        <ul className="grid gap-4 sm:grid-cols-2">
          {PLACEHOLDERS.map(({ label, description }) => (
            <li key={label}>
              <Card className="h-full">
                <CardHeader>
                  <CardTitle as="h2" className="text-base">
                    <a
                      role="link"
                      aria-disabled="true"
                      className="cursor-not-allowed text-muted-foreground"
                    >
                      {label}
                    </a>
                  </CardTitle>
                  <CardDescription>{description}</CardDescription>
                </CardHeader>
                <CardContent>
                  <Badge variant="outline">Coming soon</Badge>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </nav>
    </div>
  );
}

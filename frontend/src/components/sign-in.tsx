import { LogIn } from "lucide-react";

import { DisputeStatusPill } from "@/components/status-pill";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { currentReturnUrl, loginUrl, navigate } from "@/lib/navigation";

const FEATURES = [
  {
    title: "See every transaction",
    body: "Filter by date and status. Pending, posted and reversed are marked in colour and in words.",
  },
  {
    title: "Raise a dispute",
    body: "Pick a reason, describe what happened, and submit. Posted debits from the last 120 days qualify.",
  },
  {
    title: "Track it to the end",
    body: "Each step is logged with the date, so you always know whether it's submitted, under review or decided.",
  },
] as const;

/**
 * Shown when /bff/user answers 401. Signing in is a top-level navigation to the BFF, which
 * redirects on to Keycloak. It is never a fetch: a fetch cannot follow a cross-origin
 * redirect to a login page, and the SPA must never handle credentials.
 */
export function SignIn() {
  return (
    <div className="flex flex-col gap-5">
      <div className="grid gap-5 min-[860px]:grid-cols-2">
        <Card className="justify-center gap-5 p-6 sm:p-10 lg:p-12">
          <h1 className="text-[15px] font-bold tracking-[0.08em] text-brand-crimson uppercase">
            Sign in
          </h1>
          <p className="text-[clamp(30px,4.4vw,44px)] leading-[1.08] font-bold tracking-tight">
            Question a card transaction in a few minutes.
          </p>
          <p className="text-muted-foreground">
            See every transaction on your accounts, dispute the ones you don&apos;t recognise, and
            follow each dispute until it&apos;s resolved.
          </p>
          <Button
            size="lg"
            className="min-h-12 justify-self-start px-7 text-[15px]"
            onClick={() => navigate(loginUrl(currentReturnUrl()))}
          >
            <LogIn aria-hidden="true" />
            Sign in
          </Button>
          <p className="text-[13px] text-muted-foreground">
            You&apos;ll continue to the secure Capitec login page.
          </p>
        </Card>

        <aside
          aria-label="Example dispute"
          className="relative grid min-h-80 content-end gap-4 overflow-hidden rounded-card bg-brand-royal p-6 text-white sm:p-10"
        >
          <span
            aria-hidden="true"
            className="pointer-events-none absolute -top-36 -right-30 size-90 rounded-full border-[56px] border-white/10"
          />
          {/* Large text only: white on Royal Blue is 4.48:1. */}
          <p className="relative text-[26px] leading-tight font-bold">
            Every dispute shows where it is, from submitted to resolved.
          </p>
          <div className="relative grid max-w-85 gap-1 rounded-lg bg-card p-4 text-card-foreground">
            <div className="flex justify-between gap-2">
              <strong>Takealot.com</strong>
              <span className="tabular-nums">R 2 349.00</span>
            </div>
            <span className="text-[13px] text-muted-foreground">
              Goods or services not received
            </span>
            <span className="mt-1">
              <DisputeStatusPill status="UnderReview" />
            </span>
          </div>
        </aside>
      </div>

      <ul aria-label="What you can do" className="grid gap-5 min-[760px]:grid-cols-3">
        {FEATURES.map((feature, index) => (
          <li key={feature.title}>
            <Card className="h-full gap-2 p-6">
              <span
                aria-hidden="true"
                className="mb-1 grid size-9 place-items-center rounded-[10px] bg-background font-bold text-brand-crimson"
              >
                {index + 1}
              </span>
              <h2 className="text-[17px] font-semibold">{feature.title}</h2>
              <p className="text-muted-foreground">{feature.body}</p>
            </Card>
          </li>
        ))}
      </ul>
    </div>
  );
}

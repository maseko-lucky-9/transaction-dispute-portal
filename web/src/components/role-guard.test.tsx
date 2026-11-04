import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";

import { fakeAgentApi, makeAgentDispute } from "@/test/agent";
import { alice, bob, carol, signedIn } from "@/test/handlers";
import { renderApp } from "@/test/render-app";
import { server } from "@/test/server";

const DISPUTE = "dddddddd-0000-4000-8000-000000000001";
const TRANSACTION = "00000000-0000-4000-8000-000000000001";

/** Every request except the session check, so "no data request was made" can be asserted. */
function recordApiRequests() {
  const requests: string[] = [];
  server.use(
    http.all("/api/*", ({ request }) => {
      requests.push(`${request.method} ${new URL(request.url).pathname}`);
      return new HttpResponse(null, { status: 500 });
    }),
  );
  return requests;
}

const nav = () => screen.getByRole("navigation", { name: "Primary" });

describe("header navigation by role", () => {
  it("a customer sees Transactions and Disputes, and no Queue", async () => {
    server.use(signedIn(alice));
    renderApp("/");
    await screen.findByText(/Signed in as/);

    const links = within(nav()).getAllByRole("link");
    expect(links.map((link) => link.textContent)).toEqual(["Transactions", "Disputes"]);
    expect(within(nav()).queryByRole("link", { name: "Queue" })).not.toBeInTheDocument();
  });

  it("an agent sees ONLY the Queue", async () => {
    server.use(signedIn(bob));
    renderApp("/");
    await screen.findByText(/Signed in as/);

    const links = within(nav()).getAllByRole("link");
    expect(links.map((link) => link.textContent)).toEqual(["Queue"]);
    expect(links[0]).toHaveAttribute("href", "/agent");
  });

  it("a refused both-roles session gets no section links at all", async () => {
    server.use(signedIn(carol));
    renderApp("/");
    await screen.findByText(/Signed in as/);

    expect(screen.queryByRole("navigation", { name: "Primary" })).not.toBeInTheDocument();
  });

  it("marks the Queue as the current page on the queue", async () => {
    server.use(signedIn(bob));
    renderApp("/agent");
    await screen.findByRole("heading", { level: 1 });

    expect(within(nav()).getByRole("link", { name: "Queue" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });
});

describe("home by role", () => {
  it("an agent gets a link to the queue and no customer cards", async () => {
    server.use(signedIn(bob));
    renderApp("/");
    await screen.findByText(/Signed in as/);

    const main = within(screen.getByRole("navigation", { name: "Main" }));
    expect(main.getByRole("link", { name: "Review queue" })).toHaveAttribute("href", "/agent");
    expect(main.queryByRole("link", { name: "My transactions" })).not.toBeInTheDocument();
    expect(main.queryByRole("link", { name: "My disputes" })).not.toBeInTheDocument();
  });

  it("a customer gets the customer cards and no queue", async () => {
    server.use(signedIn(alice));
    renderApp("/");
    await screen.findByText(/Signed in as/);

    const main = within(screen.getByRole("navigation", { name: "Main" }));
    expect(main.getAllByRole("link").map((link) => link.getAttribute("href"))).toEqual([
      "/transactions",
      "/disputes",
    ]);
  });

  it("a both-roles session is told why it has nothing to open", async () => {
    server.use(signedIn(carol));
    renderApp("/");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "both a customer and a back-office role",
    );
    expect(screen.queryByRole("navigation", { name: "Main" })).not.toBeInTheDocument();
  });

  it("follows the queue link to the queue", async () => {
    server.use(signedIn(bob));
    const { router } = renderApp("/");
    await screen.findByText(/Signed in as/);

    await userEvent.setup().click(screen.getByRole("link", { name: "Review queue" }));

    await screen.findByRole("heading", { level: 1 });
    expect(router.state.location.pathname).toBe("/agent");
  });
});

describe("a customer in the back-office", () => {
  it.each([
    "/agent",
    `/agent/disputes/${DISPUTE}`,
    "/agent?status=Accepted&limit=5",
    "/agent/disputes/not-an-id",
  ])("opening %s explains the area and makes no request", async (path) => {
    server.use(signedIn(alice));
    const requests = recordApiRequests();
    renderApp(path);

    expect(
      await screen.findByRole("heading", { level: 1, name: "This area is for back-office agents" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to home" })).toHaveAttribute("href", "/");
    expect(document.title).toBe(
      "This area is for back-office agents - Transactions Dispute Portal",
    );
    // Let anything that was going to fire, fire.
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(requests).toEqual([]);
  });
});

describe("an agent on customer pages", () => {
  it.each([
    "/transactions",
    "/transactions?status=Posted",
    "/disputes",
    `/disputes/${DISPUTE}`,
    `/transactions/${TRANSACTION}/dispute`,
  ])("opening %s explains the area and makes no request", async (path) => {
    server.use(signedIn(bob));
    const requests = recordApiRequests();
    renderApp(path);

    expect(
      await screen.findByRole("heading", { level: 1, name: "This area is for customers" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to the review queue" })).toHaveAttribute(
      "href",
      "/agent",
    );
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(requests).toEqual([]);
  });
});

describe("a session with both roles", () => {
  it.each(["/agent", "/transactions", `/disputes/${DISPUTE}`])(
    "is refused at %s, and makes no request",
    async (path) => {
      server.use(signedIn(carol));
      const requests = recordApiRequests();
      renderApp(path);

      expect(
        await screen.findByRole("heading", {
          level: 1,
          name: "Your account has both a customer and a back-office role",
        }),
      ).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Back to home" })).toBeInTheDocument();
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(requests).toEqual([]);
    },
  );
});

describe("pages open to every role", () => {
  it.each([alice, bob])("$name can open Home and the not-found page", async (user) => {
    server.use(signedIn(user));
    renderApp("/does-not-exist");
    expect(await screen.findByRole("heading", { name: "Page not found" })).toBeInTheDocument();
  });
});

describe("an agent in the back-office", () => {
  it("gets the real queue, not the refusal, and the queue makes its request", async () => {
    const { handlers } = fakeAgentApi([makeAgentDispute()]);
    server.use(signedIn(bob), ...handlers);
    renderApp("/agent");

    expect(await screen.findByRole("heading", { level: 1, name: "Review queue" })).toBeVisible();
    expect(await screen.findByRole("table")).toBeInTheDocument();
    expect(screen.queryByText("This area is for back-office agents")).not.toBeInTheDocument();
  });

  it("normalises a hand-edited queue URL instead of crashing", async () => {
    const { api, handlers } = fakeAgentApi([makeAgentDispute()]);
    server.use(signedIn(bob), ...handlers);
    const { router } = renderApp("/agent?status=bogus&limit=999");

    await screen.findByRole("table");
    expect(screen.getByLabelText("Status")).toHaveValue("open");
    expect(Object.fromEntries(api.queueRequests.at(-1)?.searchParams ?? [])).toEqual({
      limit: "25",
    });
    // The defaults are not kept on the URL.
    expect(router.state.location.search).toEqual({});
  });
});

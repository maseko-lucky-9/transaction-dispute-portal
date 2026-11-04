import type { User } from "@/api/types";

export const CUSTOMER_ROLE = "customer";
export const AGENT_ROLE = "dispute-agent";

/** The two halves of the app. Routes declare theirs in `staticData.area`. */
export type Area = "agent" | "customer";

/**
 * Which half a session may use. The server decides for real (403): this only keeps the UI from
 * offering, or even attempting, what it would refuse.
 *
 *  - "agent":    the back-office. A session with only the agent role.
 *  - "customer": the customer pages. Anyone else, including a session with no role yet.
 *  - "both":     refused everywhere (an agent must never act as a customer).
 */
export type Access = Area | "both";

export function accessFor(user: Pick<User, "roles">): Access {
  const agent = user.roles.includes(AGENT_ROLE);
  const customer = user.roles.includes(CUSTOMER_ROLE);
  if (agent && customer) return "both";
  return agent ? "agent" : "customer";
}

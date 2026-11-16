import type { DisputeStatus } from "@/api/disputes";

import { shortId } from "./format";

/**
 * Who is reviewing a dispute, as this agent sees it (ADR-019). The ONLY place that reads the
 * `assignment`, `assigneeId` and `claimedAt` fields of the agent API.
 *
 * The generated types mark those fields required, but the web can be live while an older api
 * (from before assignment) still serves it, and that api sends none of them. So every field is
 * read as possibly absent or malformed, and anything that cannot be trusted is "unknown": the
 * pages then fall back to what they did before assignment existed (the status alone decides).
 */
export type AssignmentView =
  /** Submitted: in the pool, anyone may claim it. */
  | { kind: "unclaimed" }
  /** Under review by this agent. */
  | { kind: "mine"; claimedAt: string | null }
  /** Under review by someone else, named by a short id. */
  | { kind: "other"; assignee: string; claimedAt: string | null }
  /**
   * Decided, withdrawn, an older api that sends no assignment, or a review with no assignee (a
   * row an older pod wrote mid-rollout, which the API lets any agent decide): derive nothing.
   */
  | { kind: "unknown" };

/** What the agent API may send. Typed loosely on purpose: an older api sends none of it. */
interface AssignmentFields {
  status: DisputeStatus;
  assignment?: unknown;
  assigneeId?: unknown;
  claimedAt?: unknown;
}

const UNKNOWN: AssignmentView = { kind: "unknown" };

const textOrNull = (value: unknown): string | null =>
  typeof value === "string" && value.trim() !== "" ? value : null;

function instantOrNull(value: unknown): string | null {
  const text = textOrNull(value);
  return text !== null && !Number.isNaN(new Date(text).getTime()) ? text : null;
}

/**
 * Normalise what the API says about a dispute's reviewer. The status must agree with the
 * assignment (only a Submitted dispute is unclaimed, only one under review has a reviewer);
 * anything else is unknown rather than guessed.
 */
export function assignmentOf(source: AssignmentFields): AssignmentView {
  const { status, assignment } = source;
  if (assignment === "Unclaimed") return status === "Submitted" ? { kind: "unclaimed" } : UNKNOWN;
  if (status !== "UnderReview") return UNKNOWN;
  if (assignment === "You") return { kind: "mine", claimedAt: instantOrNull(source.claimedAt) };
  if (assignment === "AnotherAgent") {
    // No assignee means nobody holds it: the API guards only a non-null assignee (ADR-019,
    // expand step), so the status alone decides, as it did before assignment.
    const assignee = textOrNull(source.assigneeId);
    if (assignee === null) return UNKNOWN;
    return {
      kind: "other",
      assignee: shortId(assignee),
      claimedAt: instantOrNull(source.claimedAt),
    };
  }
  return UNKNOWN;
}

/** The dispute page's assignment line, or null when there is nothing trustworthy to say. */
export function assignmentLine(view: AssignmentView): string | null {
  switch (view.kind) {
    case "unclaimed":
      return "Unclaimed";
    case "mine":
      return "In review by you";
    case "other":
      return `In review by Agent ${view.assignee}`;
    case "unknown":
      return null;
  }
}

/** Someone else holds the review, so this agent may read the dispute but not decide it. */
export const reviewedByAnother = (view: AssignmentView): boolean => view.kind === "other";

const DAY_MS = 86_400_000;

/**
 * How long ago a claim was made, in words: "Claimed today", "Claimed yesterday", "Claimed 3 days
 * ago". Whole UTC days, like the queue's age; a future instant (a skewed clock) is today. Null
 * when the claim time is unknown.
 */
export function claimAge(view: AssignmentView, now: number = Date.now()): string | null {
  if (view.kind !== "mine" && view.kind !== "other") return null;
  if (view.claimedAt === null) return null;
  const days = Math.floor((now - new Date(view.claimedAt).getTime()) / DAY_MS);
  if (days < 1) return "Claimed today";
  return days === 1 ? "Claimed yesterday" : `Claimed ${days} days ago`;
}

/**
 * Whether the API said anything about assignment at all. An older api sends no `assignment` (and
 * ignores the queue's `assignment` filter, answering with the whole open queue), so a list from
 * it cannot be split by reviewer. Null is an answer (decided or withdrawn); absent is not.
 */
export const sendsAssignment = (item: { assignment?: unknown }): boolean =>
  item.assignment !== undefined;

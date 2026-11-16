import type { DisputeStatus } from "@/api/disputes";

import type { AssignmentView } from "./assignment";

/**
 * A state an agent can move a dispute to. Withdrawn is the customer's and is not here; Submitted
 * is the release of a dispute under review back to the queue (ADR-019).
 */
export type TransitionTarget = "UnderReview" | "Accepted" | "Rejected" | "Submitted";

export interface TransitionOption {
  to: TransitionTarget;
  /** The button: what the agent does. */
  label: string;
  /** What the confirmation says when it is done, e.g. "accept". */
  verb: string;
  /** The note is required (and shown to the customer) for a decision. */
  noteRequired: boolean;
  /** A decision cannot be undone, so it is confirmed first. */
  confirm: boolean;
  /** False when the API refuses a note for the move (a release): none is checked or sent. */
  takesNote: boolean;
  /** Button look. Only one option on a page may be crimson ("default"); the others are quieter. */
  variant: "default" | "outline" | "ghost";
}

const START_REVIEW: TransitionOption = {
  to: "UnderReview",
  label: "Start review",
  verb: "start the review of",
  noteRequired: false,
  confirm: false,
  takesNote: true,
  variant: "default",
};
const ACCEPT: TransitionOption = {
  to: "Accepted",
  label: "Accept",
  verb: "accept",
  noteRequired: true,
  confirm: true,
  takesNote: true,
  variant: "default",
};
const REJECT: TransitionOption = {
  to: "Rejected",
  label: "Reject",
  verb: "reject",
  noteRequired: true,
  confirm: true,
  takesNote: true,
  // Not destructive: #b4121a sits beside crimson #e51718 and reads as a second red button.
  variant: "outline",
};

/**
 * Hand a dispute under review back to the queue. No note: notes are shown to the customer, and
 * the API refuses one. Confirmed, because another agent may claim it the moment it is back.
 */
export const RELEASE: TransitionOption = {
  to: "Submitted",
  label: "Release to queue",
  verb: "release",
  noteRequired: false,
  confirm: true,
  takesNote: false,
  variant: "ghost",
};

/**
 * What an agent may do from each status. Mirrors the Domain's DisputeLifecycle (the agent rows:
 * Submitted -> UnderReview, UnderReview -> Accepted or Rejected, and UnderReview -> Submitted,
 * the release, which is assignee-only and so is added by `optionsFor`, not listed here); the
 * server enforces it, this only offers what would be accepted. A terminal status offers nothing.
 */
export const NEXT_TRANSITIONS: Record<DisputeStatus, readonly TransitionOption[]> = {
  Submitted: [START_REVIEW],
  UnderReview: [ACCEPT, REJECT],
  Accepted: [],
  Rejected: [],
  Withdrawn: [],
};

/**
 * What THIS agent may do, given who is reviewing (ADR-019): only the reviewing agent may decide or
 * release, so a dispute another agent holds offers nothing. When the assignment is unknown (an
 * older api that sends none), the status alone decides, as before assignment existed, and there
 * is no release (that api has no such move).
 */
export function optionsFor(
  status: DisputeStatus,
  assignment: AssignmentView,
): readonly TransitionOption[] {
  if (status !== "UnderReview") return NEXT_TRANSITIONS[status];
  if (assignment.kind === "other") return [];
  if (assignment.kind === "mine") return [...NEXT_TRANSITIONS.UnderReview, RELEASE];
  return NEXT_TRANSITIONS.UnderReview;
}

/** Every option that takes a note from one status has the same rule, so the form can be validated before a choice. */
export const noteIsRequired = (status: DisputeStatus): boolean =>
  NEXT_TRANSITIONS[status].some((option) => option.noteRequired);

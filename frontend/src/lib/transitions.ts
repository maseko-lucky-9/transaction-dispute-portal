import type { DisputeStatus } from "@/api/disputes";

/** A state an agent can move a dispute to. Withdrawn is the customer's and is not here. */
export type TransitionTarget = "UnderReview" | "Accepted" | "Rejected";

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
}

const START_REVIEW: TransitionOption = {
  to: "UnderReview",
  label: "Start review",
  verb: "start the review of",
  noteRequired: false,
  confirm: false,
};
const ACCEPT: TransitionOption = {
  to: "Accepted",
  label: "Accept",
  verb: "accept",
  noteRequired: true,
  confirm: true,
};
const REJECT: TransitionOption = {
  to: "Rejected",
  label: "Reject",
  verb: "reject",
  noteRequired: true,
  confirm: true,
};

/**
 * What an agent may do from each status. Mirrors the Domain's DisputeLifecycle (the agent rows:
 * Submitted -> UnderReview, UnderReview -> Accepted or Rejected); the server enforces it, this
 * only offers what would be accepted. A terminal status offers nothing.
 */
export const NEXT_TRANSITIONS: Record<DisputeStatus, readonly TransitionOption[]> = {
  Submitted: [START_REVIEW],
  UnderReview: [ACCEPT, REJECT],
  Accepted: [],
  Rejected: [],
  Withdrawn: [],
};

/** Every option from one status has the same note rule, so the form can be validated before a choice. */
export const noteIsRequired = (status: DisputeStatus): boolean =>
  NEXT_TRANSITIONS[status].some((option) => option.noteRequired);

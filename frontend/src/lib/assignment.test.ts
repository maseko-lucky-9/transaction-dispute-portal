import { describe, expect, it } from "vitest";

import {
  assignmentLine,
  assignmentOf,
  claimAge,
  reviewedByAnother,
  sendsAssignment,
} from "./assignment";

const SUBJECT = "bbbbbbbb-0000-4000-8000-000000000002";
const CLAIMED = "2026-11-10T09:00:00Z";

describe("assignmentOf", () => {
  it("reads the three answers the API gives, relative to the agent asking", () => {
    expect(assignmentOf({ status: "Submitted", assignment: "Unclaimed" })).toEqual({
      kind: "unclaimed",
    });
    expect(assignmentOf({ status: "UnderReview", assignment: "You", claimedAt: CLAIMED })).toEqual({
      kind: "mine",
      claimedAt: CLAIMED,
    });
    expect(
      assignmentOf({
        status: "UnderReview",
        assignment: "AnotherAgent",
        assigneeId: SUBJECT,
        claimedAt: CLAIMED,
      }),
    ).toEqual({ kind: "other", assignee: "bbbbbbbb", claimedAt: CLAIMED });
  });

  it("derives nothing when an older api sends no assignment fields at all", () => {
    expect(assignmentOf({ status: "UnderReview" })).toEqual({ kind: "unknown" });
    expect(assignmentOf({ status: "Submitted" })).toEqual({ kind: "unknown" });
  });

  it("is unknown for a decided or withdrawn dispute (null) and for a value it does not know", () => {
    expect(assignmentOf({ status: "Accepted", assignment: null })).toEqual({ kind: "unknown" });
    expect(assignmentOf({ status: "UnderReview", assignment: "Supervisor" })).toEqual({
      kind: "unknown",
    });
    expect(assignmentOf({ status: "UnderReview", assignment: 1 })).toEqual({ kind: "unknown" });
  });

  it("refuses an assignment that contradicts the status rather than guess", () => {
    expect(assignmentOf({ status: "UnderReview", assignment: "Unclaimed" })).toEqual({
      kind: "unknown",
    });
    expect(assignmentOf({ status: "Submitted", assignment: "You" })).toEqual({ kind: "unknown" });
    expect(assignmentOf({ status: "Rejected", assignment: "AnotherAgent" })).toEqual({
      kind: "unknown",
    });
  });

  it("a review with no assignee (written by an older pod) is unknown: any agent may decide it", () => {
    expect(assignmentOf({ status: "UnderReview", assignment: "AnotherAgent" })).toEqual({
      kind: "unknown",
    });
    expect(
      assignmentOf({ status: "UnderReview", assignment: "AnotherAgent", assigneeId: null }),
    ).toEqual({ kind: "unknown" });
    expect(
      assignmentOf({ status: "UnderReview", assignment: "AnotherAgent", assigneeId: "  " }),
    ).toEqual({ kind: "unknown" });
  });

  it("tolerates a missing or malformed claim time", () => {
    expect(
      assignmentOf({
        status: "UnderReview",
        assignment: "AnotherAgent",
        assigneeId: SUBJECT,
        claimedAt: "not a date",
      }),
    ).toEqual({ kind: "other", assignee: "bbbbbbbb", claimedAt: null });
    expect(assignmentOf({ status: "UnderReview", assignment: "You", claimedAt: 42 })).toEqual({
      kind: "mine",
      claimedAt: null,
    });
  });
});

describe("assignmentLine", () => {
  it("names the reviewer by short id only, and says nothing when unknown", () => {
    expect(assignmentLine({ kind: "unclaimed" })).toBe("Unclaimed");
    expect(assignmentLine({ kind: "mine", claimedAt: null })).toBe("In review by you");
    expect(assignmentLine({ kind: "other", assignee: "bbbbbbbb", claimedAt: null })).toBe(
      "In review by Agent bbbbbbbb",
    );
    expect(assignmentLine({ kind: "unknown" })).toBeNull();
  });

  it("never carries the whole subject", () => {
    const line = assignmentLine(
      assignmentOf({ status: "UnderReview", assignment: "AnotherAgent", assigneeId: SUBJECT }),
    );
    expect(line).not.toContain(SUBJECT);
    expect(line).toBe("In review by Agent bbbbbbbb");
  });
});

describe("reviewedByAnother", () => {
  it("only when the API says another agent holds it; unknown falls back to the status", () => {
    expect(reviewedByAnother({ kind: "other", assignee: "bbbbbbbb", claimedAt: null })).toBe(true);
    expect(reviewedByAnother({ kind: "mine", claimedAt: null })).toBe(false);
    expect(reviewedByAnother({ kind: "unclaimed" })).toBe(false);
    expect(reviewedByAnother({ kind: "unknown" })).toBe(false);
  });
});

describe("claimAge", () => {
  const now = Date.parse("2026-11-16T12:00:00Z");
  const other = (claimedAt: string | null) => ({
    kind: "other" as const,
    assignee: "bbbbbbbb",
    claimedAt,
  });

  it("says how long ago in words, in whole UTC days", () => {
    expect(claimAge(other("2026-11-16T08:00:00Z"), now)).toBe("Claimed today");
    expect(claimAge(other("2026-11-15T08:00:00Z"), now)).toBe("Claimed yesterday");
    expect(claimAge(other("2026-11-13T08:00:00Z"), now)).toBe("Claimed 3 days ago");
    expect(claimAge({ kind: "mine", claimedAt: "2026-11-15T08:00:00Z" }, now)).toBe(
      "Claimed yesterday",
    );
  });

  it("is today for a future instant (a skewed clock) and nothing when the time is unknown", () => {
    expect(claimAge(other("2026-11-20T08:00:00Z"), now)).toBe("Claimed today");
    expect(claimAge(other(null), now)).toBeNull();
    expect(claimAge({ kind: "unclaimed" }, now)).toBeNull();
    expect(claimAge({ kind: "unknown" }, now)).toBeNull();
  });
});

describe("sendsAssignment", () => {
  it("tells an answer (even null) from an older api's silence", () => {
    expect(sendsAssignment({ assignment: "Unclaimed" })).toBe(true);
    expect(sendsAssignment({ assignment: null })).toBe(true);
    expect(sendsAssignment({})).toBe(false);
  });
});

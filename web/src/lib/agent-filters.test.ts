import { describe, expect, it } from "vitest";

import { agentQueueSearchSchema, QUEUE_STATUSES, statusesOf } from "./agent-filters";

describe("agentQueueSearchSchema", () => {
  it("defaults to the open queue and the default limit", () => {
    expect(agentQueueSearchSchema.parse({})).toEqual({ status: "open", limit: 25 });
  });

  it.each(QUEUE_STATUSES)("accepts %s", (status) => {
    expect(agentQueueSearchSchema.parse({ status }).status).toBe(status);
  });

  it.each(["", "OPEN", "closed", "submitted", "Submitted,Accepted", 7, null, ["open"], {}])(
    "falls back to open for the status %j",
    (status) => {
      expect(agentQueueSearchSchema.parse({ status }).status).toBe("open");
    },
  );

  it.each([0, -1, 101, 1.5, "5", "x", null, NaN])("falls back to 25 for the limit %j", (limit) => {
    expect(agentQueueSearchSchema.parse({ limit }).limit).toBe(25);
  });

  it("keeps a good limit even when the status is bad, and the reverse", () => {
    expect(agentQueueSearchSchema.parse({ status: "nope", limit: 10 })).toEqual({
      status: "open",
      limit: 10,
    });
    expect(agentQueueSearchSchema.parse({ status: "Accepted", limit: 1000 })).toEqual({
      status: "Accepted",
      limit: 25,
    });
  });
});

describe("statusesOf", () => {
  it("expands open to the two awaiting a decision and leaves a status alone", () => {
    expect(statusesOf("open")).toEqual(["Submitted", "UnderReview"]);
    expect(statusesOf("Withdrawn")).toEqual(["Withdrawn"]);
  });
});

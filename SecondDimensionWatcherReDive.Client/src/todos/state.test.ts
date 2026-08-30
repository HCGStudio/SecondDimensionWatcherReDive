import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { getTodoSnoozeAction } from "./state";

describe("getTodoSnoozeAction", () => {
  const now = Date.parse("2026-08-30T00:00:00Z");

  it("offers unsnooze while the wake time is still in the future", () => {
    assert.equal(getTodoSnoozeAction("2026-08-30T01:00:00Z", now), "unsnooze");
  });

  it("offers snooze for expired, absent, or invalid wake times", () => {
    assert.equal(getTodoSnoozeAction("2026-08-29T23:00:00Z", now), "snooze");
    assert.equal(getTodoSnoozeAction(null, now), "snooze");
    assert.equal(getTodoSnoozeAction("invalid", now), "snooze");
  });
});

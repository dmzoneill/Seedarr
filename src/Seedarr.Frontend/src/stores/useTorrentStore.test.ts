import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  isTelemetryStale,
  resolveTelemetryStatus,
  type TorrentTelemetry,
} from "./useTorrentStore";

describe("resolveTelemetryStatus", () => {
  it("uses REST status when telemetry is stale", () => {
    const telemetry: TorrentTelemetry = {
      status: "Paused",
      lastUpdated: Date.now() - 60_000,
    };
    assert.equal(resolveTelemetryStatus(telemetry, "Seeding"), "Seeding");
  });

  it("uses live telemetry status when fresh", () => {
    const telemetry: TorrentTelemetry = {
      status: "Seeding",
      lastUpdated: Date.now(),
    };
    assert.equal(resolveTelemetryStatus(telemetry, "Paused"), "Seeding");
  });
});

describe("isTelemetryStale", () => {
  it("treats missing lastUpdated as not stale", () => {
    assert.equal(isTelemetryStale({ status: "Paused" }), false);
  });
});

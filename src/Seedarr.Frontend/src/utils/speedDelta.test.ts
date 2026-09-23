import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { computeSpeedDelta, type SpeedSnapshot } from "./speedDelta";

describe("speedDelta: computeSpeedDelta", () => {
  it("initializes snapshot on initial setup (prev === null) without calculating speed", () => {
    const now = 1000000;
    const current = { totalUploaded: 1024, totalDownloaded: 2048 };
    const result = computeSpeedDelta(current, null, now, 1.0);

    assert.equal(result.thresholdReached, false);
    assert.equal(result.speed, undefined);
    assert.deepEqual(result.nextSnapshot, {
      totalUploaded: 1024,
      totalDownloaded: 2048,
      timestamp: now,
    });
  });

  it("does not reset baseline snapshot when updates arrive faster than threshold (timeDelta < threshold)", () => {
    const t0 = 1000000;
    const initial: SpeedSnapshot = {
      totalUploaded: 1000,
      totalDownloaded: 2000,
      timestamp: t0,
    };

    // Sub-second update after 200ms
    const t1 = t0 + 200;
    const current1 = { totalUploaded: 1500, totalDownloaded: 2500 };
    const result1 = computeSpeedDelta(current1, initial, t1, 1.0);

    assert.equal(result1.thresholdReached, false);
    assert.equal(result1.speed, undefined);
    // Crucial: baseline timestamp and counts MUST NOT be reset to t1!
    assert.equal(result1.nextSnapshot.timestamp, t0);
    assert.equal(result1.nextSnapshot.totalUploaded, 1000);
    assert.equal(result1.nextSnapshot.totalDownloaded, 2000);

    // Another sub-second update after 600ms total
    const t2 = t0 + 600;
    const current2 = { totalUploaded: 2000, totalDownloaded: 3000 };
    const result2 = computeSpeedDelta(current2, result1.nextSnapshot, t2, 1.0);

    assert.equal(result2.thresholdReached, false);
    assert.equal(result2.speed, undefined);
    assert.equal(result2.nextSnapshot.timestamp, t0);

    // Finally after 1200ms (threshold 1.0s reached from initial t0)
    const t3 = t0 + 1200;
    const current3 = { totalUploaded: 3400, totalDownloaded: 4400 };
    const result3 = computeSpeedDelta(current3, result2.nextSnapshot, t3, 1.0);

    assert.equal(result3.thresholdReached, true);
    assert.ok(result3.speed);
    // (3400 - 1000) / 1.2 = 2400 / 1.2 = 2000 B/s
    // (4400 - 2000) / 1.2 = 2400 / 1.2 = 2000 B/s
    assert.equal(Math.round(result3.speed.uploadSpeed), 2000);
    assert.equal(Math.round(result3.speed.downloadSpeed), 2000);
    // Next snapshot is now updated to t3
    assert.equal(result3.nextSnapshot.timestamp, t3);
    assert.equal(result3.nextSnapshot.totalUploaded, 3400);
    assert.equal(result3.nextSnapshot.totalDownloaded, 4400);
  });

  it("supports custom threshold (e.g. 0.5s for SpeedGraph)", () => {
    const t0 = 1000000;
    const initial: SpeedSnapshot = {
      totalUploaded: 0,
      totalDownloaded: 0,
      timestamp: t0,
    };

    // 400ms: below 0.5s
    const r1 = computeSpeedDelta(
      { totalUploaded: 400, totalDownloaded: 800 },
      initial,
      t0 + 400,
      0.5,
    );
    assert.equal(r1.thresholdReached, false);
    assert.equal(r1.nextSnapshot.timestamp, t0);

    // 550ms: reaches 0.5s threshold
    const r2 = computeSpeedDelta(
      { totalUploaded: 5500, totalDownloaded: 11000 },
      r1.nextSnapshot,
      t0 + 550,
      0.5,
    );
    assert.equal(r2.thresholdReached, true);
    assert.ok(r2.speed);
    assert.equal(Math.round(r2.speed.uploadSpeed), 10000);
    assert.equal(Math.round(r2.speed.downloadSpeed), 20000);
    assert.equal(r2.nextSnapshot.timestamp, t0 + 550);
  });

  it("clamps negative speeds to 0 if counters decrease (e.g. counter reset)", () => {
    const t0 = 1000000;
    const initial: SpeedSnapshot = {
      totalUploaded: 5000,
      totalDownloaded: 5000,
      timestamp: t0,
    };

    const result = computeSpeedDelta(
      { totalUploaded: 100, totalDownloaded: 200 },
      initial,
      t0 + 1000,
      1.0,
    );
    assert.equal(result.thresholdReached, true);
    assert.ok(result.speed);
    assert.equal(result.speed.uploadSpeed, 0);
    assert.equal(result.speed.downloadSpeed, 0);
  });
});

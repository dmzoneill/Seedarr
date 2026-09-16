import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { HourlyTrafficPoint } from "../api/types";
import { calculateHourlyActivityPoints } from "./TrackerMetrics";

function createTrafficPoint(overrides: Partial<HourlyTrafficPoint> = {}): HourlyTrafficPoint {
  return {
    timeLabel: "12:00",
    timestamp: "2026-09-16T12:00:00Z",
    uploaded: 1000,
    downloaded: 500,
    announces: 10,
    peersDiscovered: 50,
    avgLatencyMs: 45,
    ...overrides,
  };
}

describe("TrackerMetrics: calculateHourlyActivityPoints", () => {
  it("generates points for upload, download, and announces sharing correct scales", () => {
    const data: HourlyTrafficPoint[] = [
      createTrafficPoint({ uploaded: 100, downloaded: 200, announces: 5 }),
      createTrafficPoint({ uploaded: 400, downloaded: 100, announces: 15 }),
      createTrafficPoint({ uploaded: 200, downloaded: 500, announces: 10 }),
    ];

    const width = 600;
    const height = 160;
    const padding = 20;

    const result = calculateHourlyActivityPoints(data, width, height, padding);

    // maxTraffic should be max(400, 500) = 500
    assert.equal(result.maxTraffic, 500);
    // maxAnnounce should be 15
    assert.equal(result.maxAnnounce, 15);

    // Points should exist and contain 3 coordinate pairs
    const uploadCoords = result.pointsUpload.split(" ");
    const downloadCoords = result.pointsDownload.split(" ");
    const announceCoords = result.pointsAnnounce.split(" ");

    assert.equal(uploadCoords.length, 3);
    assert.equal(downloadCoords.length, 3);
    assert.equal(announceCoords.length, 3);

    // First point x should be padding (20)
    assert.match(uploadCoords[0], /^20,/);
    assert.match(downloadCoords[0], /^20,/);
    assert.match(announceCoords[0], /^20,/);

    // Last point x should be width - padding (580)
    assert.match(uploadCoords[2], /^580,/);
    assert.match(downloadCoords[2], /^580,/);
    assert.match(announceCoords[2], /^580,/);

    // Peak download is at index 2 (500), so its y should be padding (20)
    const lastDownloadY = Number(downloadCoords[2].split(",")[1]);
    assert.equal(lastDownloadY, padding);

    // Peak announce is at index 1 (15), so its y should be padding (20)
    const peakAnnounceY = Number(announceCoords[1].split(",")[1]);
    assert.equal(peakAnnounceY, padding);
  });

  it("sanitizes coordinates against NaN, null, and undefined values", () => {
    const badData = [
      createTrafficPoint({ uploaded: NaN, downloaded: undefined as unknown as number, announces: null as unknown as number }),
      createTrafficPoint({ uploaded: -50, downloaded: NaN, announces: undefined as unknown as number }),
    ];

    const result = calculateHourlyActivityPoints(badData, 600, 160, 20);

    // maxTraffic and maxAnnounce should default safely to 1
    assert.equal(result.maxTraffic, 1);
    assert.equal(result.maxAnnounce, 1);

    // None of the points should contain NaN or Infinity
    assert.ok(!result.pointsUpload.includes("NaN"), "Upload points contained NaN");
    assert.ok(!result.pointsDownload.includes("NaN"), "Download points contained NaN");
    assert.ok(!result.pointsAnnounce.includes("NaN"), "Announce points contained NaN");

    assert.ok(!result.pointsUpload.includes("Infinity"), "Upload points contained Infinity");
    assert.ok(!result.pointsDownload.includes("Infinity"), "Download points contained Infinity");
    assert.ok(!result.pointsAnnounce.includes("Infinity"), "Announce points contained Infinity");

    // When values are 0 or invalid, y coordinate falls back to height - padding (140)
    for (const point of result.pointsUpload.split(" ")) {
      const [, y] = point.split(",").map(Number);
      assert.equal(y, 140);
    }
  });

  it("handles a single data point without division by zero", () => {
    const single = [createTrafficPoint({ uploaded: 1000, downloaded: 2000, announces: 10 })];
    const result = calculateHourlyActivityPoints(single, 600, 160, 20);

    assert.equal(result.maxTraffic, 2000);
    assert.equal(result.maxAnnounce, 10);

    const [upX, upY] = result.pointsUpload.split(",").map(Number);
    const [downX, downY] = result.pointsDownload.split(",").map(Number);

    assert.equal(upX, 20);
    assert.equal(downX, 20);
    assert.ok(Number.isFinite(upY));
    assert.ok(Number.isFinite(downY));
  });

  it("handles empty data gracefully", () => {
    const result = calculateHourlyActivityPoints([], 600, 160, 20);
    assert.equal(result.maxTraffic, 1);
    assert.equal(result.maxAnnounce, 1);
    assert.equal(result.pointsUpload, "");
    assert.equal(result.pointsDownload, "");
    assert.equal(result.pointsAnnounce, "");
  });

  it("computes summary max upload and swarm bar percentages correctly", () => {
    const topUploadTrackers = [
      { totalUploaded: 500_000_000 },
      { totalUploaded: 250_000_000 },
      { totalUploaded: 0 },
    ];

    const summaryMaxUpload = Math.max(
      ...topUploadTrackers.map((t) => t.totalUploaded || 0),
      1,
    );
    assert.equal(summaryMaxUpload, 500_000_000);

    const pcts = topUploadTrackers.map((t) =>
      summaryMaxUpload > 0 ? (t.totalUploaded / summaryMaxUpload) * 100 : 0,
    );

    assert.equal(pcts[0], 100);
    assert.equal(pcts[1], 50);
    assert.equal(pcts[2], 0);
  });

  it("handles empty top upload trackers without exploding percentages", () => {
    const emptyTrackers: { totalUploaded: number }[] = [];
    const summaryMaxUpload = Math.max(
      ...emptyTrackers.map((t) => t.totalUploaded || 0),
      1,
    );
    assert.equal(summaryMaxUpload, 1);

    const pcts = emptyTrackers.map((t) =>
      summaryMaxUpload > 0 ? (t.totalUploaded / summaryMaxUpload) * 100 : 0,
    );
    assert.deepEqual(pcts, []);
  });
});

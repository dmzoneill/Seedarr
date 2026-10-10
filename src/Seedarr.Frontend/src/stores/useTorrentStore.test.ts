import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import {
  applyTelemetry,
  isTelemetryStale,
  resolveTelemetryStatus,
  TELEMETRY_STATUS_STALE_MS,
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

  it("treats telemetry older than the stale window as stale", () => {
    const telemetry: TorrentTelemetry = {
      status: "Paused",
      lastUpdated: Date.now() - TELEMETRY_STATUS_STALE_MS - 1,
    };
    assert.equal(isTelemetryStale(telemetry), true);
  });

  it("treats telemetry within the stale window as fresh", () => {
    const telemetry: TorrentTelemetry = {
      status: "Paused",
      lastUpdated: Date.now() - TELEMETRY_STATUS_STALE_MS + 500,
    };
    assert.equal(isTelemetryStale(telemetry), false);
  });
});

const baseTorrent = (): Torrent => ({
  id: 1,
  name: "example",
  status: "Seeding",
  progress: 1,
  downloadSpeed: 1200,
  uploadSpeed: 3400,
  eta: 0,
  seeders: 3,
  leechers: 1,
  uploaded: 100,
  downloaded: 100,
  ratio: 1,
});

describe("applyTelemetry", () => {
  it("ignores stale Paused telemetry so REST seeding speeds show after resume", () => {
    const torrent = baseTorrent();
    const stalePaused: TorrentTelemetry = {
      status: "Paused",
      uploadSpeed: 0,
      downloadSpeed: 0,
      lastUpdated: Date.now() - 60_000,
    };

    const merged = applyTelemetry(torrent, stalePaused);
    assert.equal(merged.status, torrent.status);
    assert.equal(merged.uploadSpeed, torrent.uploadSpeed);
    assert.equal(merged.downloadSpeed, torrent.downloadSpeed);
  });

  it("zeros speeds when fresh telemetry reports Paused", () => {
    const torrent = baseTorrent();
    const freshPaused: TorrentTelemetry = {
      status: "Paused",
      uploadSpeed: 9000,
      downloadSpeed: 9000,
      lastUpdated: Date.now(),
    };

    const merged = applyTelemetry(torrent, freshPaused);
    assert.equal(merged.status, "Paused");
    assert.equal(merged.uploadSpeed, 0);
    assert.equal(merged.downloadSpeed, 0);
  });

  it("uses fresh telemetry speeds when status is active", () => {
    const torrent = baseTorrent();
    torrent.status = "Downloading";
    const fresh: TorrentTelemetry = {
      status: "Downloading",
      uploadSpeed: 10,
      downloadSpeed: 5000,
      lastUpdated: Date.now(),
    };

    const merged = applyTelemetry(torrent, fresh);
    assert.equal(merged.downloadSpeed, 5000);
    assert.equal(merged.uploadSpeed, 10);
  });
});

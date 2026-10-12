import { beforeEach, describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import {
  applyTelemetry,
  isTelemetryStale,
  resolveTelemetryStatus,
  safeGetStorage,
  TELEMETRY_STATUS_STALE_MS,
  useTorrentStore,
  type TorrentTelemetry,
} from "./useTorrentStore";

function resetTorrentStore(): void {
  const state = useTorrentStore.getState();
  state.clearTelemetry();
  state.clearPieceMaps();
  state.clearSelection();
  state.clearOptimisticUpdates();
}

beforeEach(() => resetTorrentStore());

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

  it("zeros speeds for stale inactive torrents", () => {
    const torrent = baseTorrent();
    torrent.status = "Queued";
    const stale: TorrentTelemetry = {
      status: "Queued",
      uploadSpeed: 100,
      downloadSpeed: 200,
      lastUpdated: Date.now() - 60_000,
    };
    const merged = applyTelemetry(torrent, stale);
    assert.equal(merged.uploadSpeed, 0);
    assert.equal(merged.downloadSpeed, 0);
  });
});

describe("useTorrentStore actions", () => {
  it("updateTelemetry merges speed aliases and purge removes stale entries", () => {
    const store = useTorrentStore.getState();
    store.updateTelemetry([
      { id: 1, upSpeed: 11, downSpeed: 22, status: "Downloading" },
    ]);
    assert.equal(useTorrentStore.getState().telemetry[1]?.uploadSpeed, 11);
    assert.equal(useTorrentStore.getState().telemetry[1]?.downloadSpeed, 22);

    useTorrentStore.setState({
      telemetry: {
        1: { status: "Paused", lastUpdated: Date.now() - 10_000 },
        2: { status: "Seeding", lastUpdated: Date.now() },
      },
    });
    store.purgeStaleTelemetry(1000);
    assert.equal(useTorrentStore.getState().telemetry[1], undefined);
    assert.equal(useTorrentStore.getState().telemetry[2]?.status, "Seeding");
  });

  it("updatePieceMap applies ranges, corruption, and cleared resets", () => {
    const store = useTorrentStore.getState();
    store.updatePieceMap(9, { ranges: [[0, 2]] });
    let map = useTorrentStore.getState().pieceMaps[9];
    assert.ok(map);
    assert.equal(map.version, 1);

    store.updatePieceMap(9, { pieceIndex: 4, pieceState: 3 });
    map = useTorrentStore.getState().pieceMaps[9]!;
    assert.deepEqual(map.corruptedIndices, [4]);

    store.updatePieceMap(9, { cleared: true });
    map = useTorrentStore.getState().pieceMaps[9]!;
    assert.equal(map.bitfield.length, 0);
    assert.deepEqual(map.corruptedIndices, []);
    assert.equal(map.version, 3);
  });

  it("selection, optimistic updates, and removeTorrent clean related state", () => {
    const store = useTorrentStore.getState();
    store.setSelectedIds([1, 2]);
    store.toggleSelectedId(2);
    assert.deepEqual([...useTorrentStore.getState().selectedIds], [1]);

    store.setSelectedTorrentId(1);
    store.updateTorrentOptimistic(1, { status: "Paused" });
    store.updateTelemetry([{ id: 1, status: "Seeding" }]);
    store.updatePieceMap(1, { pieceIndex: 0, pieceState: 1 });

    store.removeTorrent(1);
    const state = useTorrentStore.getState();
    assert.equal(state.selectedTorrentId, null);
    assert.equal(state.telemetry[1], undefined);
    assert.equal(state.pieceMaps[1], undefined);
    assert.equal(state.optimisticUpdates[1], undefined);
  });

  it("updatePieceMap merges base64 and Uint8Array bitfields and PascalCase aliases", () => {
    const store = useTorrentStore.getState();
    const singleByteB64 = btoa(String.fromCharCode(0xff));
    store.updatePieceMap(3, { bitfield: singleByteB64 });
    store.updatePieceMap(3, {
      bitfield: new Uint8Array([0x01]),
      PieceIndexes: [10],
      PieceIndex: 11,
      PieceState: 1,
    });
    const map = useTorrentStore.getState().pieceMaps[3]!;
    assert.ok(map.bitfield.length >= 2);
    assert.equal(map.corruptedIndices?.length ?? 0, 0);
  });

  it("clearTorrentTelemetry and clearOptimisticUpdates no-op when missing", () => {
    const store = useTorrentStore.getState();
    const before = useTorrentStore.getState();
    store.clearTorrentTelemetry(999);
    assert.deepEqual(useTorrentStore.getState().telemetry, before.telemetry);
    store.clearOptimisticUpdates(404);
    assert.deepEqual(
      useTorrentStore.getState().optimisticUpdates,
      before.optimisticUpdates,
    );
    store.clearOptimisticUpdates();
    assert.deepEqual(useTorrentStore.getState().optimisticUpdates, {});
  });

  it("updateTelemetry ignores invalid rows and selectAllIds replaces selection", () => {
    const store = useTorrentStore.getState();
    store.updateTelemetry([{ id: 1, status: "Seeding" }]);
    const snap = useTorrentStore.getState().telemetry;
    store.updateTelemetry([
      null as unknown as { id: number },
      {} as { id: number },
      { id: "bad" as unknown as number },
    ]);
    assert.deepEqual(useTorrentStore.getState().telemetry, snap);
    store.selectAllIds([5, 6]);
    assert.deepEqual([...useTorrentStore.getState().selectedIds], [5, 6]);
    store.setSelectedIds(new Set([7]));
    assert.deepEqual([...useTorrentStore.getState().selectedIds], [7]);
  });
});

describe("applyTelemetry extended", () => {
  it("prefers optimistic status over fresh telemetry status", () => {
    const torrent = baseTorrent();
    const fresh: TorrentTelemetry = {
      status: "Error",
      uploadSpeed: 50,
      downloadSpeed: 50,
      lastUpdated: Date.now(),
    };
    const merged = applyTelemetry(torrent, fresh, { status: "Downloading" });
    assert.equal(merged.status, "Downloading");
    assert.equal(merged.uploadSpeed, 50);
    assert.equal(merged.downloadSpeed, 50);
  });

  it("parses string eta from fresh telemetry", () => {
    const torrent = baseTorrent();
    torrent.status = "Downloading";
    const fresh: TorrentTelemetry = {
      status: "Downloading",
      eta: "120" as unknown as number,
      lastUpdated: Date.now(),
    };
    const merged = applyTelemetry(torrent, fresh);
    assert.equal(merged.eta, 120);
  });
});

describe("safeGetStorage", () => {
  it("returns default when localStorage is unavailable (node test runner)", () => {
    assert.deepEqual(safeGetStorage("missing", ["alt"], { ok: true }), {
      ok: true,
    });
  });
});

describe("useTorrentStore edge cases", () => {
  it("clears telemetry and piece maps wholesale", () => {
    const store = useTorrentStore.getState();
    store.updateTelemetry([{ id: 1, status: "Seeding" }]);
    store.updatePieceMap(1, { pieceIndex: 0, pieceState: 1 });
    store.clearTelemetry();
    store.clearPieceMaps();
    assert.deepEqual(useTorrentStore.getState().telemetry, {});
    assert.deepEqual(useTorrentStore.getState().pieceMaps, {});
  });

  it("repairing a corrupted piece clears corruptedIndices", () => {
    const store = useTorrentStore.getState();
    store.updatePieceMap(5, { pieceIndex: 2, pieceState: 3 });
    store.updatePieceMap(5, { pieceIndex: 2, pieceState: 1 });
    assert.deepEqual(
      useTorrentStore.getState().pieceMaps[5]?.corruptedIndices,
      [],
    );
  });

  it("honors Cleared PascalCase flag on piece maps", () => {
    const store = useTorrentStore.getState();
    store.updatePieceMap(6, { ranges: [[0, 3]] });
    store.updatePieceMap(6, { Cleared: true });
    assert.equal(useTorrentStore.getState().pieceMaps[6]?.bitfield.length, 0);
  });
});

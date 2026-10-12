import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import {
  normalizePieceMapWirePayload,
  resolvePieceMapTorrentId,
} from "./pieceMapSignalR";

const torrents: Torrent[] = [
  {
    id: 42,
    name: "a",
    infoHash: "AbCdEf",
    status: "Seeding",
    progress: 1,
    downloadSpeed: 0,
    uploadSpeed: 0,
    eta: 0,
    seeders: 0,
    leechers: 0,
    uploaded: 0,
    downloaded: 0,
    ratio: 0,
  },
];

describe("resolvePieceMapTorrentId", () => {
  it("reads camelCase torrentId", () => {
    assert.equal(resolvePieceMapTorrentId({ torrentId: 7 }), 7);
  });

  it("reads PascalCase TorrentId and Id", () => {
    assert.equal(resolvePieceMapTorrentId({ TorrentId: 8 }), 8);
    assert.equal(resolvePieceMapTorrentId({ Id: 9 }), 9);
  });

  it("ignores non-positive ids", () => {
    assert.equal(resolvePieceMapTorrentId({ torrentId: 0 }), undefined);
    assert.equal(resolvePieceMapTorrentId({ torrentId: -1 }), undefined);
  });

  it("resolves by infoHash when torrent list provided", () => {
    const id = resolvePieceMapTorrentId({ infoHash: "abcdef" }, torrents);
    assert.equal(id, 42);
  });

  it("returns undefined for blank infoHash or missing list", () => {
    assert.equal(resolvePieceMapTorrentId({ infoHash: "  " }, torrents), undefined);
    assert.equal(resolvePieceMapTorrentId({ InfoHash: "abcdef" }), undefined);
  });
});

describe("normalizePieceMapWirePayload", () => {
  it("maps pieceIndexes and pieceState aliases", () => {
    const normalized = normalizePieceMapWirePayload({
      pieceIndexes: [1, 2],
      PieceState: 2,
      Cleared: false,
    });
    assert.deepEqual(normalized.pieceIndices, [1, 2]);
    assert.equal(normalized.pieceState, 2);
    assert.equal(normalized.cleared, false);
  });

  it("sets cleared from camelCase or PascalCase", () => {
    assert.equal(normalizePieceMapWirePayload({ cleared: true }).cleared, true);
    assert.equal(normalizePieceMapWirePayload({ Cleared: true }).cleared, true);
  });

  it("drops negative pieceIndex and pieceState", () => {
    const normalized = normalizePieceMapWirePayload({
      pieceIndex: -1,
      state: -2,
    });
    assert.equal(normalized.pieceIndex, undefined);
    assert.equal(normalized.pieceState, undefined);
  });
});

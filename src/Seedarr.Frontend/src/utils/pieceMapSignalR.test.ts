import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  normalizePieceMapWirePayload,
  resolvePieceMapTorrentId,
} from "./pieceMapSignalR";
import type { Torrent } from "../api/types";

describe("pieceMapSignalR", () => {
  const torrents: Torrent[] = [
    {
      id: 42,
      infoHash: "abcdef1234567890abcdef1234567890abcdef12",
      name: "test",
      status: "downloading",
    } as Torrent,
  ];

  it("resolvePieceMapTorrentId reads torrentId, TorrentId, and id", () => {
    assert.equal(resolvePieceMapTorrentId({ torrentId: 5 }), 5);
    assert.equal(resolvePieceMapTorrentId({ TorrentId: 6 }), 6);
    assert.equal(resolvePieceMapTorrentId({ id: 7 }), 7);
  });

  it("resolvePieceMapTorrentId maps infoHash via cached torrent list", () => {
    const tid = resolvePieceMapTorrentId(
      { infoHash: "ABCDEF1234567890ABCDEF1234567890ABCDEF12" },
      torrents,
    );
    assert.equal(tid, 42);
  });

  it("normalizePieceMapWirePayload maps pieceIndexes and PieceIndex", () => {
    const normalized = normalizePieceMapWirePayload({
      pieceIndexes: [1, 2],
      PieceIndex: 3,
      Cleared: true,
    });
    assert.deepEqual(normalized.pieceIndices, [1, 2]);
    assert.equal(normalized.pieceIndex, 3);
    assert.equal(normalized.cleared, true);
  });

  it("normalizePieceMapWirePayload maps PieceState corruption", () => {
    const normalized = normalizePieceMapWirePayload({
      PieceIndex: 9,
      PieceState: 3,
    });
    assert.equal(normalized.pieceIndex, 9);
    assert.equal(normalized.pieceState, 3);
  });
});

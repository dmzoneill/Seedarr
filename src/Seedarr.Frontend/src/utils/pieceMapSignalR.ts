import type { Torrent } from "../api/types";
import type { PieceMapUpdatePayload } from "../stores/useTorrentStore";

export type PieceMapWirePayload = Record<string, unknown>;

function readNumber(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isFinite(value) && value > 0) {
    return value;
  }
  return undefined;
}

function readInfoHash(body: PieceMapWirePayload): string | undefined {
  const raw = body.infoHash ?? body.InfoHash;
  if (typeof raw !== "string" || raw.trim() === "") {
    return undefined;
  }
  return raw.toLowerCase();
}

/**
 * Resolves torrent id from pieceMapUpdated wire payload (camelCase, PascalCase, or infoHash lookup).
 */
export function resolvePieceMapTorrentId(
  body: PieceMapWirePayload,
  torrents?: Torrent[] | null,
): number | undefined {
  const direct =
    readNumber(body.torrentId) ??
    readNumber(body.TorrentId) ??
    readNumber(body.id) ??
    readNumber(body.Id);
  if (direct) {
    return direct;
  }

  const infoHash = readInfoHash(body);
  if (!infoHash || !torrents?.length) {
    return undefined;
  }

  const match = torrents.find(
    (t) =>
      typeof t.infoHash === "string" &&
      t.infoHash.toLowerCase() === infoHash,
  );
  return match?.id;
}

/**
 * Normalizes backend piece map event fields to the shape updatePieceMap expects.
 */
export function normalizePieceMapWirePayload(
  body: PieceMapWirePayload,
): PieceMapUpdatePayload {
  const pieceIndices =
    (Array.isArray(body.pieceIndices) ? body.pieceIndices : undefined) ??
    (Array.isArray(body.pieceIndexes) ? body.pieceIndexes : undefined) ??
    (Array.isArray(body.PieceIndexes) ? body.PieceIndexes : undefined);

  const pieceIndexRaw = body.pieceIndex ?? body.PieceIndex;
  const pieceIndex =
    typeof pieceIndexRaw === "number" && pieceIndexRaw >= 0
      ? pieceIndexRaw
      : undefined;

  const cleared = body.cleared === true || body.Cleared === true;

  const pieceStateRaw =
    body.pieceState ?? body.PieceState ?? body.state ?? body.State;
  const pieceState =
    typeof pieceStateRaw === "number" && pieceStateRaw >= 0
      ? pieceStateRaw
      : undefined;

  return {
    ...body,
    pieceIndices,
    pieceIndex,
    cleared,
    pieceState,
  };
}

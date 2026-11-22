import type { QueryClient } from "@tanstack/react-query";
import type { Torrent } from "../api/types";

/**
 * Named SignalR events emitted for a torrent resource update.
 * The broadcaster fans one update out under all three names.
 */
export const TORRENT_UPDATED_EVENTS = [
  "TorrentUpdated",
  "torrent_updated",
  "torrentUpdated",
] as const;

const TORRENT_STATUS_BY_CODE = [
  "Stopped",
  "Seeding",
  "Paused",
  "Error",
  "Queued",
  "Downloading",
  "Checking",
  "QueuedForChecking",
  "Moving",
  "StalledNoSeeds",
] as const;

const NUMERIC_KEYS = new Set<keyof Torrent>([
  "totalSize",
  "pieceCount",
  "pieceLength",
  "uploaded",
  "downloaded",
  "ratio",
  "seeders",
  "leechers",
  "priority",
  "uploadLimit",
  "downloadLimit",
  "progress",
  "announceInterval",
  "nextUpdate",
  "sessionUploaded",
  "sessionDownloaded",
  "smallTorrentLimit",
  "threshold",
  "uploadSpeed",
  "downloadSpeed",
  "availability",
  "eta",
  "sortOrder",
  "seedingTime",
]);

const NULLABLE_NUMERIC_KEYS = new Set<keyof Torrent>(["year", "rating"]);

const BOOLEAN_KEYS = new Set<keyof Torrent>([
  "isPrivate",
  "superSeeding",
  "forceStart",
  "sequentialDownload",
  "firstLastPiecePrio",
  "active",
  "forceCompleted",
  "isVpnPaused",
]);

const REQUIRED_STRING_KEYS = new Set<keyof Torrent>([
  "name",
  "infoHash",
  "dateAdded",
]);

const NULLABLE_STRING_KEYS = new Set<keyof Torrent>([
  "comment",
  "createdBy",
  "creationDate",
  "trackerUrl",
  "sourcePath",
  "lastActive",
  "label",
  "category",
  "savePath",
  "downloadPath",
  "posterUrl",
  "fanartUrl",
  "bannerUrl",
  "mediaTitle",
  "overview",
  "source",
  "errorMessage",
  "magnetLink",
  "resolution",
  "hdrFormat",
  "videoCodec",
  "audioCodec",
  "audioChannels",
]);

const STRING_ARRAY_KEYS = new Set<keyof Torrent>(["genres", "trackers"]);

export type TorrentSignalPatch = Partial<Torrent> & { id: number };

export function isTorrentUpdatedEvent(eventName: string): boolean {
  const lower = eventName.toLowerCase();
  return (
    lower === "torrentupdated" ||
    lower === "torrent_updated"
  );
}

function asRecord(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  return value as Record<string, unknown>;
}

function readTorrentId(record: Record<string, unknown>): number | null {
  const candidates = [record.id, record.Id, record.torrentId, record.TorrentId];
  for (const candidate of candidates) {
    if (
      typeof candidate === "number" &&
      Number.isInteger(candidate) &&
      candidate > 0
    ) {
      return candidate;
    }
  }
  return null;
}

function camelKey(key: string): string {
  if (key.length === 0) return key;
  return key.charAt(0).toLowerCase() + key.slice(1);
}

function coerceStatus(value: unknown): string | null {
  if (typeof value === "string" && value.length > 0) return value;
  if (typeof value === "number" && Number.isInteger(value)) {
    return TORRENT_STATUS_BY_CODE[value] ?? null;
  }
  return null;
}

function assignTorrentField(
  target: Record<string, unknown>,
  key: string,
  value: unknown,
): void {
  if (value === undefined) return;
  const field = key as keyof Torrent;

  if (NUMERIC_KEYS.has(field)) {
    if (typeof value === "number" && Number.isFinite(value)) {
      target[field] = value;
    }
    return;
  }

  if (NULLABLE_NUMERIC_KEYS.has(field)) {
    if (value === null || (typeof value === "number" && Number.isFinite(value))) {
      target[field] = value;
    }
    return;
  }

  if (BOOLEAN_KEYS.has(field)) {
    if (typeof value === "boolean") target[field] = value;
    return;
  }

  if (field === "status") {
    const status = coerceStatus(value);
    if (status) target.status = status;
    return;
  }

  if (STRING_ARRAY_KEYS.has(field)) {
    if (
      Array.isArray(value) &&
      value.every((item) => typeof item === "string")
    ) {
      target[field] = value;
    }
    return;
  }

  if (field === "tagIds") {
    if (
      Array.isArray(value) &&
      value.every((item) => typeof item === "number" && Number.isFinite(item))
    ) {
      target.tagIds = value;
    }
    return;
  }

  if (REQUIRED_STRING_KEYS.has(field)) {
    if (typeof value === "string") target[field] = value;
    return;
  }

  if (NULLABLE_STRING_KEYS.has(field)) {
    if (typeof value === "string" || value === null) target[field] = value;
  }
}

/**
 * Build a partial torrent from a SignalR TorrentUpdated body.
 * Accepts the camelCase REST resource and the PascalCase domain model.
 * Returns null when the payload has no positive torrent id.
 */
export function torrentPatchFromSignalRPayload(
  data: unknown,
): TorrentSignalPatch | null {
  const record = asRecord(data);
  if (!record) return null;

  const nested = asRecord(record.torrent) ?? asRecord(record.Torrent);
  const source = readTorrentId(record) == null && nested ? nested : record;
  const id = readTorrentId(source);
  if (id == null) return null;

  const target: Record<string, unknown> = { id };
  for (const [rawKey, value] of Object.entries(source)) {
    const key = camelKey(rawKey);
    if (key === "id" || key === "torrentId") continue;
    const alreadySet = Object.prototype.hasOwnProperty.call(target, key);
    const isPascalDuplicate = rawKey !== key && alreadySet;
    if (isPascalDuplicate) continue;
    assignTorrentField(target, key, value);
  }

  return target as TorrentSignalPatch;
}

export function applyPatchesToTorrentList(
  list: Torrent[] | undefined,
  patches: readonly TorrentSignalPatch[],
): Torrent[] | undefined {
  if (!list || patches.length === 0) return list;
  const byId = new Map(patches.map((patch) => [patch.id, patch]));
  let changed = false;
  const next = list.map((torrent) => {
    const patch = byId.get(torrent.id);
    if (!patch) return torrent;
    changed = true;
    return { ...torrent, ...patch, id: torrent.id };
  });
  return changed ? next : list;
}

function applyPatchToDetail(
  queryClient: QueryClient,
  patch: TorrentSignalPatch,
): void {
  queryClient.setQueryData<Torrent>(["torrents", patch.id], (current) => {
    if (!current) return current;
    return { ...current, ...patch, id: current.id };
  });
}

/**
 * Merge already-normalized patches into ["torrents"] and ["torrents", id].
 * Does not create missing rows and does not invalidate queries.
 */
export function flushTorrentPatches(
  queryClient: QueryClient,
  patches: readonly TorrentSignalPatch[],
): void {
  if (patches.length === 0) return;
  queryClient.setQueryData<Torrent[]>(["torrents"], (current) =>
    applyPatchesToTorrentList(current, patches),
  );
  for (const patch of patches) {
    applyPatchToDetail(queryClient, patch);
  }
}

interface PatchBatch {
  patches: Map<number, TorrentSignalPatch>;
  scheduled: boolean;
}

const batches = new WeakMap<QueryClient, PatchBatch>();

/**
 * Coalesce a burst of TorrentUpdated payloads (including the three aliased
 * event names) into one cache write. The list query stays valid, so React
 * Query does not refetch GET /torrent.
 */
export function enqueueTorrentUpdatedPatch(
  queryClient: QueryClient,
  patch: TorrentSignalPatch,
): void {
  let batch = batches.get(queryClient);
  if (!batch) {
    batch = { patches: new Map(), scheduled: false };
    batches.set(queryClient, batch);
  }

  const previous = batch.patches.get(patch.id);
  batch.patches.set(
    patch.id,
    previous ? { ...previous, ...patch, id: patch.id } : patch,
  );

  if (batch.scheduled) return;
  batch.scheduled = true;
  const scheduledBatch = batch;
  queueMicrotask(() => {
    scheduledBatch.scheduled = false;
    const pending = [...scheduledBatch.patches.values()];
    scheduledBatch.patches.clear();
    flushTorrentPatches(queryClient, pending);
  });
}

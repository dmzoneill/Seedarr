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

function finiteNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function writeNumber(value: unknown, write: (next: number) => void): void {
  const number = finiteNumber(value);
  if (number !== null) write(number);
}

function writeNullableNumber(
  value: unknown,
  write: (next: number | null) => void,
): void {
  if (value === null) {
    write(null);
    return;
  }
  const number = finiteNumber(value);
  if (number !== null) write(number);
}

function writeBoolean(value: unknown, write: (next: boolean) => void): void {
  if (typeof value === "boolean") write(value);
}

function writeString(value: unknown, write: (next: string) => void): void {
  if (typeof value === "string") write(value);
}

function writeNullableString(
  value: unknown,
  write: (next: string | null) => void,
): void {
  if (value === null || typeof value === "string") write(value);
}

function writeStringArray(value: unknown, write: (next: string[]) => void): void {
  if (Array.isArray(value) && value.every((item) => typeof item === "string")) {
    write(value);
  }
}

function statusFromCode(code: number): string | null {
  switch (code) {
    case 0:
      return "Stopped";
    case 1:
      return "Seeding";
    case 2:
      return "Paused";
    case 3:
      return "Error";
    case 4:
      return "Queued";
    case 5:
      return "Downloading";
    case 6:
      return "Checking";
    case 7:
      return "QueuedForChecking";
    case 8:
      return "Moving";
    case 9:
      return "StalledNoSeeds";
    default:
      return null;
  }
}

function coerceStatus(value: unknown): string | null {
  if (typeof value === "string" && value.length > 0) return value;
  if (typeof value === "number" && Number.isInteger(value)) {
    return statusFromCode(value);
  }
  return null;
}

function assignTorrentField(
  target: TorrentSignalPatch,
  key: string,
  value: unknown,
): void {
  if (value === undefined) return;

  switch (key) {
    case "totalSize":
      writeNumber(value, (next) => {
        target.totalSize = next;
      });
      return;
    case "pieceCount":
      writeNumber(value, (next) => {
        target.pieceCount = next;
      });
      return;
    case "pieceLength":
      writeNumber(value, (next) => {
        target.pieceLength = next;
      });
      return;
    case "uploaded":
      writeNumber(value, (next) => {
        target.uploaded = next;
      });
      return;
    case "downloaded":
      writeNumber(value, (next) => {
        target.downloaded = next;
      });
      return;
    case "ratio":
      writeNumber(value, (next) => {
        target.ratio = next;
      });
      return;
    case "seeders":
      writeNumber(value, (next) => {
        target.seeders = next;
      });
      return;
    case "leechers":
      writeNumber(value, (next) => {
        target.leechers = next;
      });
      return;
    case "priority":
      writeNumber(value, (next) => {
        target.priority = next;
      });
      return;
    case "uploadLimit":
      writeNumber(value, (next) => {
        target.uploadLimit = next;
      });
      return;
    case "downloadLimit":
      writeNumber(value, (next) => {
        target.downloadLimit = next;
      });
      return;
    case "progress":
      writeNumber(value, (next) => {
        target.progress = next;
      });
      return;
    case "announceInterval":
      writeNumber(value, (next) => {
        target.announceInterval = next;
      });
      return;
    case "nextUpdate":
      writeNumber(value, (next) => {
        target.nextUpdate = next;
      });
      return;
    case "sessionUploaded":
      writeNumber(value, (next) => {
        target.sessionUploaded = next;
      });
      return;
    case "sessionDownloaded":
      writeNumber(value, (next) => {
        target.sessionDownloaded = next;
      });
      return;
    case "smallTorrentLimit":
      writeNumber(value, (next) => {
        target.smallTorrentLimit = next;
      });
      return;
    case "threshold":
      writeNumber(value, (next) => {
        target.threshold = next;
      });
      return;
    case "uploadSpeed":
      writeNumber(value, (next) => {
        target.uploadSpeed = next;
      });
      return;
    case "downloadSpeed":
      writeNumber(value, (next) => {
        target.downloadSpeed = next;
      });
      return;
    case "availability":
      writeNumber(value, (next) => {
        target.availability = next;
      });
      return;
    case "eta":
      writeNumber(value, (next) => {
        target.eta = next;
      });
      return;
    case "sortOrder":
      writeNumber(value, (next) => {
        target.sortOrder = next;
      });
      return;
    case "seedingTime":
      writeNumber(value, (next) => {
        target.seedingTime = next;
      });
      return;
    case "year":
      writeNullableNumber(value, (next) => {
        target.year = next;
      });
      return;
    case "rating":
      writeNullableNumber(value, (next) => {
        target.rating = next;
      });
      return;
    case "isPrivate":
      writeBoolean(value, (next) => {
        target.isPrivate = next;
      });
      return;
    case "superSeeding":
      writeBoolean(value, (next) => {
        target.superSeeding = next;
      });
      return;
    case "forceStart":
      writeBoolean(value, (next) => {
        target.forceStart = next;
      });
      return;
    case "sequentialDownload":
      writeBoolean(value, (next) => {
        target.sequentialDownload = next;
      });
      return;
    case "firstLastPiecePrio":
      writeBoolean(value, (next) => {
        target.firstLastPiecePrio = next;
      });
      return;
    case "active":
      writeBoolean(value, (next) => {
        target.active = next;
      });
      return;
    case "forceCompleted":
      writeBoolean(value, (next) => {
        target.forceCompleted = next;
      });
      return;
    case "isVpnPaused":
      writeBoolean(value, (next) => {
        target.isVpnPaused = next;
      });
      return;
    case "status": {
      const status = coerceStatus(value);
      if (status) target.status = status;
      return;
    }
    case "name":
      writeString(value, (next) => {
        target.name = next;
      });
      return;
    case "infoHash":
      writeString(value, (next) => {
        target.infoHash = next;
      });
      return;
    case "dateAdded":
      writeString(value, (next) => {
        target.dateAdded = next;
      });
      return;
    case "comment":
      writeNullableString(value, (next) => {
        target.comment = next;
      });
      return;
    case "createdBy":
      writeNullableString(value, (next) => {
        target.createdBy = next;
      });
      return;
    case "creationDate":
      writeNullableString(value, (next) => {
        target.creationDate = next;
      });
      return;
    case "trackerUrl":
      writeNullableString(value, (next) => {
        target.trackerUrl = next;
      });
      return;
    case "sourcePath":
      writeNullableString(value, (next) => {
        target.sourcePath = next;
      });
      return;
    case "lastActive":
      writeNullableString(value, (next) => {
        target.lastActive = next;
      });
      return;
    case "label":
      writeNullableString(value, (next) => {
        target.label = next;
      });
      return;
    case "category":
      writeNullableString(value, (next) => {
        target.category = next;
      });
      return;
    case "savePath":
      writeNullableString(value, (next) => {
        target.savePath = next;
      });
      return;
    case "downloadPath":
      writeNullableString(value, (next) => {
        target.downloadPath = next;
      });
      return;
    case "posterUrl":
      writeNullableString(value, (next) => {
        target.posterUrl = next;
      });
      return;
    case "fanartUrl":
      writeNullableString(value, (next) => {
        target.fanartUrl = next;
      });
      return;
    case "bannerUrl":
      writeNullableString(value, (next) => {
        target.bannerUrl = next;
      });
      return;
    case "mediaTitle":
      writeNullableString(value, (next) => {
        target.mediaTitle = next;
      });
      return;
    case "overview":
      writeNullableString(value, (next) => {
        target.overview = next;
      });
      return;
    case "source":
      writeNullableString(value, (next) => {
        target.source = next;
      });
      return;
    case "errorMessage":
      writeNullableString(value, (next) => {
        target.errorMessage = next;
      });
      return;
    case "magnetLink":
      writeNullableString(value, (next) => {
        target.magnetLink = next;
      });
      return;
    case "resolution":
      writeNullableString(value, (next) => {
        target.resolution = next;
      });
      return;
    case "hdrFormat":
      writeNullableString(value, (next) => {
        target.hdrFormat = next;
      });
      return;
    case "videoCodec":
      writeNullableString(value, (next) => {
        target.videoCodec = next;
      });
      return;
    case "audioCodec":
      writeNullableString(value, (next) => {
        target.audioCodec = next;
      });
      return;
    case "audioChannels":
      writeNullableString(value, (next) => {
        target.audioChannels = next;
      });
      return;
    case "genres":
      writeStringArray(value, (next) => {
        target.genres = next;
      });
      return;
    case "trackers":
      writeStringArray(value, (next) => {
        target.trackers = next;
      });
      return;
    case "tagIds":
      if (
        Array.isArray(value) &&
        value.every((item) => typeof item === "number" && Number.isFinite(item))
      ) {
        target.tagIds = value;
      }
      return;
    default:
      return;
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

  const target: TorrentSignalPatch = { id };
  const seen = new Set<string>(["id"]);
  for (const [rawKey, value] of Object.entries(source)) {
    const key = camelKey(rawKey);
    if (key === "id" || key === "torrentId") continue;
    if (rawKey !== key && seen.has(key)) continue;
    seen.add(key);
    assignTorrentField(target, key, value);
  }

  return target;
}

export function applyPatchesToTorrentList(
  list: Torrent[] | undefined,
  patches: readonly TorrentSignalPatch[],
): Torrent[] | undefined {
  if (!list || patches.length === 0) return list;
  const byId = new Map(patches.map((patch) => [patch.id, patch]));
  if (!list.some((torrent) => byId.has(torrent.id))) return list;
  return list.map((torrent) => {
    const patch = byId.get(torrent.id);
    if (!patch) return torrent;
    return { ...torrent, ...patch, id: torrent.id };
  });
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

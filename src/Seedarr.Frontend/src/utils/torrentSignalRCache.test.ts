import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { torrentListCacheTiming, useTorrent, useTorrents } from "../api/hooks";
import type { Torrent } from "../api/types";
import {
  applyPatchesToTorrentList,
  enqueueTorrentUpdatedPatch,
  flushTorrentPatches,
  isTorrentUpdatedEvent,
  torrentPatchFromSignalRPayload,
} from "./torrentSignalRCache";

(globalThis as unknown as { React: typeof React }).React = React;

function torrent(overrides: Partial<Torrent> & { id: number }): Torrent {
  return {
    id: overrides.id,
    name: overrides.name ?? `Torrent ${overrides.id}`,
    infoHash: overrides.infoHash ?? "abc",
    totalSize: overrides.totalSize ?? 1000,
    pieceCount: overrides.pieceCount ?? 10,
    pieceLength: overrides.pieceLength ?? 100,
    comment: overrides.comment ?? null,
    createdBy: overrides.createdBy ?? null,
    creationDate: overrides.creationDate ?? null,
    isPrivate: overrides.isPrivate ?? false,
    status: overrides.status ?? "Seeding",
    uploaded: overrides.uploaded ?? 10,
    downloaded: overrides.downloaded ?? 20,
    ratio: overrides.ratio ?? 0.5,
    progress: overrides.progress ?? 0.25,
    seeders: overrides.seeders ?? 1,
    leechers: overrides.leechers ?? 2,
    trackerUrl: overrides.trackerUrl ?? null,
    sourcePath: overrides.sourcePath ?? null,
    dateAdded: overrides.dateAdded ?? "2026-01-01T00:00:00Z",
    lastActive: overrides.lastActive ?? null,
    priority: overrides.priority ?? 0,
    uploadLimit: overrides.uploadLimit ?? 0,
    downloadLimit: overrides.downloadLimit ?? 0,
    superSeeding: overrides.superSeeding ?? false,
    forceStart: overrides.forceStart ?? false,
    label: overrides.label ?? null,
    category: overrides.category ?? null,
    savePath: overrides.savePath ?? null,
    sequentialDownload: overrides.sequentialDownload ?? false,
    announceInterval: overrides.announceInterval ?? 1800,
    nextUpdate: overrides.nextUpdate ?? 0,
    sessionUploaded: overrides.sessionUploaded ?? 0,
    sessionDownloaded: overrides.sessionDownloaded ?? 0,
    smallTorrentLimit: overrides.smallTorrentLimit ?? 0,
    threshold: overrides.threshold ?? 0,
    uploadSpeed: overrides.uploadSpeed ?? 0,
    downloadSpeed: overrides.downloadSpeed ?? 0,
    active: overrides.active ?? true,
    availability: overrides.availability ?? 1,
    eta: overrides.eta ?? 0,
    sortOrder: overrides.sortOrder ?? 0,
    forceCompleted: overrides.forceCompleted ?? false,
    seedingTime: overrides.seedingTime ?? 0,
    posterUrl: overrides.posterUrl,
    mediaTitle: overrides.mediaTitle,
  };
}

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => {
    queueMicrotask(resolve);
  });
}

void describe("torrentSignalRCache", () => {
  void it("recognizes only the TorrentUpdated aliases", () => {
    assert.equal(isTorrentUpdatedEvent("TorrentUpdated"), true);
    assert.equal(isTorrentUpdatedEvent("torrent_updated"), true);
    assert.equal(isTorrentUpdatedEvent("torrentUpdated"), true);
    assert.equal(isTorrentUpdatedEvent("TorrentAdded"), false);
    assert.equal(isTorrentUpdatedEvent("TorrentDeleted"), false);
    assert.equal(isTorrentUpdatedEvent("speedPulse"), false);
  });

  void it("reads camelCase resource fields and ignores unknown keys", () => {
    const patch = torrentPatchFromSignalRPayload({
      id: 4,
      name: "Ubuntu",
      progress: 0.8,
      uploadSpeed: 1500,
      status: "Downloading",
      posterUrl: "https://img.example/poster.jpg",
      resourceName: "torrent",
      notAField: "nope",
    });

    assert.ok(patch);
    assert.equal(patch.id, 4);
    assert.equal(patch.progress, 0.8);
    assert.equal(patch.uploadSpeed, 1500);
    assert.equal(patch.status, "Downloading");
    assert.equal(patch.posterUrl, "https://img.example/poster.jpg");
    assert.equal("resourceName" in patch, false);
    assert.equal("notAField" in patch, false);
  });

  void it("reads PascalCase domain payloads and maps numeric status", () => {
    const patch = torrentPatchFromSignalRPayload({
      Id: 9,
      Name: "Debian",
      Progress: 0.4,
      UploadSpeed: 42,
      Status: 1,
      MediaTitle: "Debian GNU/Linux",
    });

    assert.ok(patch);
    assert.equal(patch.id, 9);
    assert.equal(patch.name, "Debian");
    assert.equal(patch.progress, 0.4);
    assert.equal(patch.uploadSpeed, 42);
    assert.equal(patch.status, "Seeding");
    assert.equal(patch.mediaTitle, "Debian GNU/Linux");
  });

  void it("prefers camelCase when both casings are present and rejects bad ids", () => {
    const patch = torrentPatchFromSignalRPayload({
      id: 3,
      progress: 0.2,
      Progress: 0.9,
    });
    assert.equal(patch?.progress, 0.2);
    assert.equal(torrentPatchFromSignalRPayload({ name: "missing id" }), null);
    assert.equal(torrentPatchFromSignalRPayload({ id: 0, progress: 1 }), null);
    assert.equal(torrentPatchFromSignalRPayload(null), null);
  });

  void it("unwraps a nested torrent object", () => {
    const patch = torrentPatchFromSignalRPayload({
      torrent: { id: 11, progress: 0.33 },
    });
    assert.equal(patch?.id, 11);
    assert.equal(patch?.progress, 0.33);
  });

  void it("patches matching rows and leaves every other row untouched", () => {
    const first = torrent({ id: 1, progress: 0.1, mediaTitle: "Keep me" });
    const second = torrent({ id: 2, progress: 0.2 });
    const next = applyPatchesToTorrentList(
      [first, second],
      [{ id: 1, progress: 0.75, uploadSpeed: 99 }],
    );

    assert.ok(next);
    assert.equal(next[0].progress, 0.75);
    assert.equal(next[0].uploadSpeed, 99);
    assert.equal(next[0].mediaTitle, "Keep me");
    assert.equal(next[0].name, first.name);
    assert.equal(next[1], second);
    const original = [first];
    const unchanged = applyPatchesToTorrentList(original, [
      { id: 99, progress: 1 },
    ]);
    assert.equal(unchanged, original);
  });

  void it("coalesces aliased TorrentUpdated bursts into one cache write without invalidating", async () => {
    const queryClient = new QueryClient();
    const first = torrent({ id: 1, progress: 0.1, uploadSpeed: 1 });
    const second = torrent({ id: 2, progress: 0.2, uploadSpeed: 2 });
    queryClient.setQueryData(["torrents"], [first, second]);
    queryClient.setQueryData(["torrents", 1], first);

    enqueueTorrentUpdatedPatch(queryClient, {
      id: 1,
      progress: 0.3,
      uploadSpeed: 10,
    });
    enqueueTorrentUpdatedPatch(queryClient, {
      id: 1,
      progress: 0.55,
      uploadSpeed: 80,
    });
    enqueueTorrentUpdatedPatch(queryClient, { id: 2, progress: 0.9 });
    enqueueTorrentUpdatedPatch(queryClient, { id: 50, progress: 1 });

    assert.equal(queryClient.getQueryData<Torrent[]>(["torrents"])?.[0].progress, 0.1);

    await flushMicrotasks();

    const list = queryClient.getQueryData<Torrent[]>(["torrents"]);
    assert.ok(list);
    assert.equal(list.length, 2);
    assert.equal(list[0].progress, 0.55);
    assert.equal(list[0].uploadSpeed, 80);
    assert.equal(list[1].progress, 0.9);
    assert.equal(list[1].uploadSpeed, 2);
    assert.equal(list[1].id, 2);

    const detail = queryClient.getQueryData<Torrent>(["torrents", 1]);
    assert.equal(detail?.progress, 0.55);
    assert.equal(queryClient.getQueryData(["torrents", 50]), undefined);

    const listQuery = queryClient.getQueryCache().find({ queryKey: ["torrents"] });
    assert.equal(listQuery?.state.isInvalidated, false);
    const detailQuery = queryClient.getQueryCache().find({
      queryKey: ["torrents", 1],
    });
    assert.equal(detailQuery?.state.isInvalidated, false);
  });

  void it("does not create a list cache when none is loaded", async () => {
    const queryClient = new QueryClient();
    enqueueTorrentUpdatedPatch(queryClient, { id: 1, progress: 0.4 });
    await flushMicrotasks();
    assert.equal(queryClient.getQueryData(["torrents"]), undefined);
  });

  void it("copies every known torrent field from a resource payload", () => {
    const patch = torrentPatchFromSignalRPayload({
      id: 15,
      torrentId: 99,
      name: "Full",
      infoHash: "hash",
      totalSize: 10,
      pieceCount: 2,
      pieceLength: 3,
      uploaded: 4,
      downloaded: 5,
      ratio: 1.5,
      seeders: 6,
      leechers: 7,
      priority: 8,
      uploadLimit: 9,
      downloadLimit: 11,
      progress: 0.4,
      announceInterval: 12,
      nextUpdate: 13,
      sessionUploaded: 14,
      sessionDownloaded: 16,
      smallTorrentLimit: 17,
      threshold: 18,
      uploadSpeed: 19,
      downloadSpeed: 20,
      availability: 1.2,
      eta: 21,
      sortOrder: 22,
      seedingTime: 23,
      year: 2024,
      rating: 8.1,
      isPrivate: true,
      superSeeding: true,
      forceStart: true,
      sequentialDownload: true,
      firstLastPiecePrio: true,
      active: false,
      forceCompleted: true,
      isVpnPaused: true,
      status: "Downloading",
      dateAdded: "2026-02-02T00:00:00Z",
      comment: "note",
      createdBy: "me",
      creationDate: "2026-01-01T00:00:00Z",
      trackerUrl: "udp://tracker",
      sourcePath: "/tmp/a",
      lastActive: "2026-02-03T00:00:00Z",
      label: "lbl",
      category: "movies",
      savePath: "/save",
      downloadPath: "/down",
      posterUrl: "poster",
      fanartUrl: "fan",
      bannerUrl: "banner",
      mediaTitle: "Title",
      overview: "overview",
      source: "radarr",
      errorMessage: "stalled",
      magnetLink: "magnet:?xt=urn:btih:abc",
      resolution: "1080p",
      hdrFormat: "HDR",
      videoCodec: "x265",
      audioCodec: "aac",
      audioChannels: "5.1",
      genres: ["drama"],
      trackers: ["udp://tracker"],
      tagIds: [1, 2],
      ignored: "nope",
    });

    assert.deepEqual(patch, {
      id: 15,
      name: "Full",
      infoHash: "hash",
      totalSize: 10,
      pieceCount: 2,
      pieceLength: 3,
      uploaded: 4,
      downloaded: 5,
      ratio: 1.5,
      seeders: 6,
      leechers: 7,
      priority: 8,
      uploadLimit: 9,
      downloadLimit: 11,
      progress: 0.4,
      announceInterval: 12,
      nextUpdate: 13,
      sessionUploaded: 14,
      sessionDownloaded: 16,
      smallTorrentLimit: 17,
      threshold: 18,
      uploadSpeed: 19,
      downloadSpeed: 20,
      availability: 1.2,
      eta: 21,
      sortOrder: 22,
      seedingTime: 23,
      year: 2024,
      rating: 8.1,
      isPrivate: true,
      superSeeding: true,
      forceStart: true,
      sequentialDownload: true,
      firstLastPiecePrio: true,
      active: false,
      forceCompleted: true,
      isVpnPaused: true,
      status: "Downloading",
      dateAdded: "2026-02-02T00:00:00Z",
      comment: "note",
      createdBy: "me",
      creationDate: "2026-01-01T00:00:00Z",
      trackerUrl: "udp://tracker",
      sourcePath: "/tmp/a",
      lastActive: "2026-02-03T00:00:00Z",
      label: "lbl",
      category: "movies",
      savePath: "/save",
      downloadPath: "/down",
      posterUrl: "poster",
      fanartUrl: "fan",
      bannerUrl: "banner",
      mediaTitle: "Title",
      overview: "overview",
      source: "radarr",
      errorMessage: "stalled",
      magnetLink: "magnet:?xt=urn:btih:abc",
      resolution: "1080p",
      hdrFormat: "HDR",
      videoCodec: "x265",
      audioCodec: "aac",
      audioChannels: "5.1",
      genres: ["drama"],
      trackers: ["udp://tracker"],
      tagIds: [1, 2],
    });
  });

  void it("drops values of the wrong type and accepts nulls on nullable fields", () => {
    const patch = torrentPatchFromSignalRPayload({
      id: 16,
      name: 1,
      infoHash: null,
      totalSize: "big",
      progress: Number.NaN,
      year: "2024",
      rating: null,
      isPrivate: "yes",
      active: 1,
      comment: null,
      overview: null,
      status: "",
      genres: [1, "drama"],
      trackers: "udp://tracker",
      tagIds: ["1"],
      posterUrl: 4,
    });

    assert.deepEqual(patch, { id: 16, rating: null, comment: null, overview: null });
  });

  void it("maps each numeric torrent status and ignores unknown codes", () => {
    const expected = [
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
    ];
    for (const [code, status] of expected.entries()) {
      assert.equal(
        torrentPatchFromSignalRPayload({ id: 1, Status: code })?.status,
        status,
      );
    }
    assert.equal(
      torrentPatchFromSignalRPayload({ id: 1, Status: 99 })?.status,
      undefined,
    );
    assert.equal(
      torrentPatchFromSignalRPayload({ id: 1, Status: 1.5 })?.status,
      undefined,
    );
  });

  void it("returns the original list when there is nothing to apply", () => {
    const row = torrent({ id: 1 });
    const rows = [row];
    assert.equal(applyPatchesToTorrentList(undefined, [{ id: 1, progress: 1 }]), undefined);
    assert.equal(applyPatchesToTorrentList(rows, []), rows);
    const queryClient = new QueryClient();
    flushTorrentPatches(queryClient, []);
    assert.equal(queryClient.getQueryData(["torrents"]), undefined);
  });
});

void describe("torrent list query timing", () => {
  void it("keeps staleTime aligned with the poll interval", () => {
    assert.deepEqual(torrentListCacheTiming(5000), {
      refetchInterval: 5000,
      staleTime: 5000,
    });
    assert.deepEqual(torrentListCacheTiming(2000), {
      refetchInterval: 2000,
      staleTime: 2000,
    });
  });

  void it("applies that timing to useTorrents and useTorrent", () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(["config", "advanced"], { uiRefreshRateSec: 8 });

    function Probe() {
      useTorrents();
      useTorrent(4);
      return null;
    }

    renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(Probe),
      ),
    );

    const timingOf = (queryKey: readonly unknown[]) => {
      const options = queryClient.getQueryCache().find({ queryKey })?.options as
        | { staleTime?: number; refetchInterval?: number }
        | undefined;
      return {
        staleTime: options?.staleTime,
        refetchInterval: options?.refetchInterval,
      };
    };
    assert.deepEqual(timingOf(["torrents"]), {
      staleTime: 8000,
      refetchInterval: 8000,
    });
    assert.deepEqual(timingOf(["torrents", 4]), {
      staleTime: 8000,
      refetchInterval: 8000,
    });
  });
});

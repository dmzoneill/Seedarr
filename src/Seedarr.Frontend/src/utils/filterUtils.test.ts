import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Torrent } from "../api/types";
import { filterTorrents } from "./filterUtils";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Ubuntu.24.04.LTS.Desktop.iso",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 4_000_000_000,
    pieceCount: 1000,
    pieceLength: 4_000_000,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "Seeding",
    uploaded: 10_000_000_000,
    downloaded: 4_000_000_000,
    ratio: 2.5,
    progress: 1.0,
    seeders: 25,
    leechers: 2,
    trackerUrl: "https://tracker.ubuntu.com/announce",
    trackers: ["https://tracker.ubuntu.com/announce"],
    category: "Linux",
    label: "OS",
    sourcePath: null,
    dateAdded: "2026-01-01T00:00:00Z",
    lastActive: null,
    priority: 1,
    uploadLimit: 0,
    downloadLimit: 0,
    superSeeding: false,
    forceStart: false,
    sequentialDownload: false,
    announceInterval: 1800,
    nextUpdate: 1800,
    sessionUploaded: 0,
    sessionDownloaded: 0,
    smallTorrentLimit: 0,
    threshold: 0,
    uploadSpeed: 1024 * 1024,
    downloadSpeed: 0,
    active: true,
    availability: 1,
    eta: 0,
    sortOrder: 0,
    forceCompleted: true,
    seedingTime: 86400,
    ...overrides,
  };
}

describe("filterUtils: filterTorrents", () => {
  const torrents: Torrent[] = [
    createMockTorrent({
      id: 1,
      name: "Ubuntu 24.04 Desktop",
      status: "Seeding",
      trackerUrl: "https://tracker.ubuntu.com:6969/announce",
      category: "Linux",
      label: "ISO",
    }),
    createMockTorrent({
      id: 2,
      name: "Debian 12 Bookworm",
      status: "Stopped",
      trackerUrl: "udp://tracker.debian.org:1337/announce",
      category: "Linux",
      label: "Archive",
    }),
    createMockTorrent({
      id: 3,
      name: "Arch Linux 2026",
      status: "Seeding",
      trackerUrl: "https://tracker.archlinux.org/announce",
      category: "Arch",
      label: "ISO",
    }),
    createMockTorrent({
      id: 4,
      name: "FreeBSD 14.1 Release",
      status: "Queued",
      trackerUrl: null,
      trackers: [],
      category: null,
      label: null,
    }),
  ];

  it("should filter by search text query", () => {
    const result = filterTorrents(torrents, { filter: "debian" });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 2);
  });

  it("should filter by status state", () => {
    const seeding = filterTorrents(torrents, { stateFilter: "Seeding" });
    assert.equal(seeding.length, 2);
    assert.deepEqual(
      seeding.map((t) => t.id),
      [1, 3],
    );

    const all = filterTorrents(torrents, { stateFilter: "All" });
    assert.equal(all.length, 4);
  });

  it("should filter by tracker domain", () => {
    const result = filterTorrents(torrents, {
      trackerFilter: "tracker.ubuntu.com",
    });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 1);
  });

  it("should filter by category and handle Uncategorized", () => {
    const linux = filterTorrents(torrents, { categoryFilter: "Linux" });
    assert.equal(linux.length, 2);
    assert.deepEqual(
      linux.map((t) => t.id),
      [1, 2],
    );

    const uncat = filterTorrents(torrents, {
      categoryFilter: "Uncategorized",
    });
    assert.equal(uncat.length, 1);
    assert.equal(uncat[0].id, 4);
  });

  it("should filter by tag / label and handle Untagged", () => {
    const iso = filterTorrents(torrents, { tagFilter: "ISO" });
    assert.equal(iso.length, 2);
    assert.deepEqual(
      iso.map((t) => t.id),
      [1, 3],
    );

    const untagged = filterTorrents(torrents, { tagFilter: "Untagged" });
    assert.equal(untagged.length, 1);
    assert.equal(untagged[0].id, 4);
  });

  it("should apply multiple filter combinations concurrently", () => {
    const result = filterTorrents(torrents, {
      filter: "Linux",
      stateFilter: "Seeding",
      categoryFilter: "Arch",
      tagFilter: "ISO",
    });
    assert.equal(result.length, 1);
    assert.equal(result[0].id, 3);
  });

  it("should return empty array for empty or undefined torrent list", () => {
    assert.deepEqual(filterTorrents(undefined, {}), []);
    assert.deepEqual(filterTorrents([], { filter: "test" }), []);
  });
});

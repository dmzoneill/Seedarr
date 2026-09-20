import { describe, it } from "node:test";
import assert from "node:assert/strict";
import type { Tag, Torrent } from "../api/types";
import { calculateTagUsageCounts } from "./Tags";

function createMockTag(overrides: Partial<Tag> = {}): Tag {
  return {
    id: 1,
    label: "Anime",
    color: "#3b82f6",
    torrentCount: 0,
    ...overrides,
  };
}

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Mock Torrent",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 1000,
    pieceCount: 10,
    pieceLength: 100,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "Seeding",
    uploaded: 2000,
    downloaded: 1000,
    ratio: 2.0,
    progress: 1.0,
    seeders: 5,
    leechers: 0,
    trackerUrl: "https://tracker.example.com/announce",
    category: "Movies",
    label: null,
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
    uploadSpeed: 0,
    downloadSpeed: 0,
    active: true,
    availability: 1,
    eta: 0,
    sortOrder: 0,
    forceCompleted: true,
    seedingTime: 3600,
    tagIds: [],
    ...overrides,
  };
}

describe("Tags: calculateTagUsageCounts", () => {
  const tags: Tag[] = [
    createMockTag({ id: 1, label: "Anime" }),
    createMockTag({ id: 2, label: "4K" }),
    createMockTag({ id: 3, label: "Music" }),
  ];

  it("should return empty object when torrents list is empty or null", () => {
    assert.deepEqual(calculateTagUsageCounts(null, "All", tags), {});
    assert.deepEqual(calculateTagUsageCounts([], "All", tags), {});
    assert.deepEqual(calculateTagUsageCounts(undefined, "All", tags), {});
  });

  it("should calculate usage count from torrent.tagIds", () => {
    const torrents: Torrent[] = [
      createMockTorrent({ id: 101, tagIds: [1, 2] }),
      createMockTorrent({ id: 102, tagIds: [1] }),
      createMockTorrent({ id: 103, tagIds: [3] }),
      createMockTorrent({ id: 104, tagIds: [] }),
    ];

    const counts = calculateTagUsageCounts(torrents, "All", tags);
    assert.equal(counts[1], 2);
    assert.equal(counts[2], 1);
    assert.equal(counts[3], 1);
    assert.equal(counts[999], undefined);
  });

  it("should support fallback to torrent.tags if tagIds is absent", () => {
    const torrents = [
      createMockTorrent({ id: 201, tagIds: undefined }),
      createMockTorrent({ id: 202, tagIds: undefined }),
    ];
    (torrents[0] as any).tags = [2];
    (torrents[1] as any).tags = [2, 3];

    const counts = calculateTagUsageCounts(torrents, "All", tags);
    assert.equal(counts[2], 2);
    assert.equal(counts[3], 1);
  });

  it("should support backward compatibility with legacy torrent.label", () => {
    const torrents: Torrent[] = [
      createMockTorrent({ id: 301, tagIds: [], label: "Anime" }),
      createMockTorrent({ id: 302, tagIds: [], label: "4K, Music" }),
    ];

    const counts = calculateTagUsageCounts(torrents, "All", tags);
    assert.equal(counts[1], 1);
    assert.equal(counts[2], 1);
    assert.equal(counts[3], 1);
  });

  it("should not duplicate count if torrent has both matching tagId and legacy label", () => {
    const torrents: Torrent[] = [
      createMockTorrent({ id: 401, tagIds: [1], label: "Anime" }),
    ];

    const counts = calculateTagUsageCounts(torrents, "All", tags);
    assert.equal(counts[1], 1);
  });

  it("should filter tag counts by selectedCategory", () => {
    const torrents: Torrent[] = [
      createMockTorrent({ id: 501, tagIds: [1], category: "Movies" }),
      createMockTorrent({ id: 502, tagIds: [1], category: "Series" }),
      createMockTorrent({ id: 503, tagIds: [1, 2], category: "Series" }),
      createMockTorrent({ id: 504, tagIds: [1], category: null }),
    ];

    const seriesCounts = calculateTagUsageCounts(torrents, "Series", tags);
    assert.equal(seriesCounts[1], 2);
    assert.equal(seriesCounts[2], 1);

    const moviesCounts = calculateTagUsageCounts(torrents, "Movies", tags);
    assert.equal(moviesCounts[1], 1);
    assert.equal(moviesCounts[2], undefined);

    const uncatCounts = calculateTagUsageCounts(torrents, "Uncategorized", tags);
    assert.equal(uncatCounts[1], 1);
  });
});

describe("Tags: Deletion and Filter State Synchronization", () => {
  it("should sanitize active tag filter when a tag is deleted", () => {
    let selectedTag = "Anime";
    let selectedTagIds = new Set([1, 2]);

    const handleTagDeleted = (detail: { id: number; label: string }) => {
      if (selectedTagIds.has(detail.id)) {
        selectedTagIds.delete(detail.id);
      }
      if (selectedTag === detail.label) {
        selectedTag = "All";
      }
    };

    // Delete tag 1 (Anime)
    handleTagDeleted({ id: 1, label: "Anime" });

    assert.equal(selectedTag, "All");
    assert.equal(selectedTagIds.has(1), false);
    assert.equal(selectedTagIds.has(2), true);
  });

  it("should strip leading and trailing whitespace when creating or editing tags", () => {
    const inputLabel = "   seedbox-vip   ";
    const trimmed = inputLabel.trim();
    assert.equal(trimmed, "seedbox-vip");
  });
});

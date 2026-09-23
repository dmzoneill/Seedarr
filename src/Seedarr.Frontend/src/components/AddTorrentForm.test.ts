import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  buildExistingHashesSet,
  isReleaseInLibrary,
  isReleaseAdded,
  getReleaseButtonState,
  TORZNAB_CATEGORIES,
  sortReleases,
  paginateReleases,
} from "./AddTorrentForm";

describe("AddTorrentForm: Library Detection & Grab Tracking (Issue #308)", () => {
  describe("buildExistingHashesSet", () => {
    it("returns an empty set when torrents array is empty or undefined", () => {
      assert.equal(buildExistingHashesSet(undefined).size, 0);
      assert.equal(buildExistingHashesSet([]).size, 0);
    });

    it("extracts and lowercases valid infoHashes while filtering null/empty", () => {
      const torrents = [
        { infoHash: "4A8B9C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B" },
        { infoHash: "abcdef1234567890abcdef1234567890abcdef12" },
        { infoHash: null },
        { infoHash: "" },
        { infoHash: undefined },
      ];

      const set = buildExistingHashesSet(torrents);
      assert.equal(set.size, 2);
      assert.ok(set.has("4a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b"));
      assert.ok(set.has("abcdef1234567890abcdef1234567890abcdef12"));
      assert.ok(!set.has(""));
    });
  });

  describe("isReleaseInLibrary", () => {
    const existingHashes = new Set([
      "4a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b",
      "11223344556677889900aabbccddeeff00112233",
    ]);

    it("returns false if release has no infoHash", () => {
      assert.equal(
        isReleaseInLibrary({ infoHash: undefined }, existingHashes),
        false,
      );
      assert.equal(
        isReleaseInLibrary({ infoHash: null }, existingHashes),
        false,
      );
      assert.equal(isReleaseInLibrary({ infoHash: "" }, existingHashes), false);
    });

    it("returns true if release infoHash matches existingHashes (case-insensitive)", () => {
      assert.equal(
        isReleaseInLibrary(
          { infoHash: "4A8B9C0D1E2F3A4B5C6D7E8F9A0B1C2D3E4F5A6B" },
          existingHashes,
        ),
        true,
      );
      assert.equal(
        isReleaseInLibrary(
          { infoHash: "4a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b" },
          existingHashes,
        ),
        true,
      );
    });

    it("returns false if release infoHash is not in library", () => {
      assert.equal(
        isReleaseInLibrary(
          { infoHash: "9999999999999999999999999999999999999999" },
          existingHashes,
        ),
        false,
      );
    });
  });

  describe("isReleaseAdded", () => {
    it("matches release by itemKey, guid, or infoHash", () => {
      const addedKeys = new Set(["release-guid-1", "00aabbccddeeff"]);

      // Matches by guid
      assert.equal(
        isReleaseAdded(
          { guid: "release-guid-1", infoHash: "otherhash", title: "Release 1" },
          addedKeys,
        ),
        true,
      );

      // Matches by infoHash
      assert.equal(
        isReleaseAdded(
          { guid: "guid-2", infoHash: "00AABBCCDDEEFF", title: "Release 2" },
          addedKeys,
        ),
        true,
      );

      // Matches by title if title was the itemKey
      const titleAddedKeys = new Set(["Release Title 3"]);
      assert.equal(
        isReleaseAdded(
          { guid: "", infoHash: "", title: "Release Title 3" },
          titleAddedKeys,
        ),
        true,
      );

      // Does not match unknown release
      assert.equal(
        isReleaseAdded(
          { guid: "unknown-guid", infoHash: "unknown-hash", title: "Unknown" },
          addedKeys,
        ),
        false,
      );
    });
  });

  describe("getReleaseButtonState", () => {
    it("returns 'Adding...' and disabled=true when isDownloading=true", () => {
      const state = getReleaseButtonState({
        isDownloading: true,
        isAdded: false,
        isInLibrary: false,
      });
      assert.equal(state.label, "Adding...");
      assert.equal(state.disabled, true);
      assert.equal(state.className, "btn btn-success");
    });

    it("returns '✓ Added' and disabled=true when isAdded=true", () => {
      const state = getReleaseButtonState({
        isDownloading: false,
        isAdded: true,
        isInLibrary: false,
      });
      assert.equal(state.label, "✓ Added");
      assert.equal(state.disabled, true);
      assert.equal(state.className, "btn btn-secondary");
    });

    it("returns 'In Library' and disabled=true when isInLibrary=true and not added in session", () => {
      const state = getReleaseButtonState({
        isDownloading: false,
        isAdded: false,
        isInLibrary: true,
      });
      assert.equal(state.label, "In Library");
      assert.equal(state.disabled, true);
      assert.equal(state.className, "btn btn-secondary");
    });

    it("prioritizes '✓ Added' over 'In Library' when both are true", () => {
      const state = getReleaseButtonState({
        isDownloading: false,
        isAdded: true,
        isInLibrary: true,
      });
      assert.equal(state.label, "✓ Added");
      assert.equal(state.disabled, true);
    });

    it("returns '+ Add' and disabled=false when neither added nor in library", () => {
      const state = getReleaseButtonState({
        isDownloading: false,
        isAdded: false,
        isInLibrary: false,
      });
      assert.equal(state.label, "+ Add");
      assert.equal(state.disabled, false);
      assert.equal(state.className, "btn btn-success");
    });
  });
});

describe("AddTorrentForm: Category Selector, Column Sorting & Pagination (Issue #306)", () => {
  describe("TORZNAB_CATEGORIES", () => {
    it("defines standard Torznab category options", () => {
      const catMap = new Map(TORZNAB_CATEGORIES.map((c) => [c.id, c.name]));
      assert.equal(catMap.get(""), "All Categories");
      assert.equal(catMap.get("2000"), "Movies (2000)");
      assert.equal(catMap.get("5000"), "TV (5000)");
      assert.equal(catMap.get("3000"), "Audio (3000)");
      assert.equal(catMap.get("4000"), "Software (4000)");
      assert.equal(catMap.get("1000"), "Games (1000)");
      assert.equal(catMap.get("7000"), "Books (7000)");
      assert.equal(catMap.get("5070"), "Anime (5070)");
    });
  });

  describe("sortReleases", () => {
    const sampleReleases = [
      {
        title: "Beta Release",
        size: 2000,
        seeders: 10,
        leechers: 2,
        publishDate: "2026-01-02T00:00:00Z",
      },
      {
        title: "Alpha Release",
        size: 5000,
        seeders: 50,
        leechers: 1,
        publishDate: "2026-01-05T00:00:00Z",
      },
      {
        title: "Gamma Release",
        size: 1000,
        seeders: 10,
        leechers: 8,
        publishDate: null,
      },
    ];

    it("returns empty array for empty or undefined input", () => {
      assert.deepEqual(sortReleases(undefined, "title", "asc"), []);
      assert.deepEqual(sortReleases([], "title", "asc"), []);
    });

    it("sorts by title asc and desc", () => {
      const asc = sortReleases(sampleReleases, "title", "asc");
      assert.equal(asc[0].title, "Alpha Release");
      assert.equal(asc[1].title, "Beta Release");
      assert.equal(asc[2].title, "Gamma Release");

      const desc = sortReleases(sampleReleases, "title", "desc");
      assert.equal(desc[0].title, "Gamma Release");
      assert.equal(desc[1].title, "Beta Release");
      assert.equal(desc[2].title, "Alpha Release");
    });

    it("sorts by size asc and desc", () => {
      const asc = sortReleases(sampleReleases, "size", "asc");
      assert.equal(asc[0].size, 1000);
      assert.equal(asc[1].size, 2000);
      assert.equal(asc[2].size, 5000);

      const desc = sortReleases(sampleReleases, "size", "desc");
      assert.equal(desc[0].size, 5000);
      assert.equal(desc[1].size, 2000);
      assert.equal(desc[2].size, 1000);
    });

    it("sorts by peers/seeders asc and desc, breaking ties with leechers", () => {
      const desc = sortReleases(sampleReleases, "peers", "desc");
      assert.equal(desc[0].title, "Alpha Release"); // 50 seeders
      assert.equal(desc[1].title, "Gamma Release"); // 10 seeders, 8 leechers
      assert.equal(desc[2].title, "Beta Release"); // 10 seeders, 2 leechers

      const asc = sortReleases(sampleReleases, "peers", "asc");
      assert.equal(asc[0].title, "Beta Release");
      assert.equal(asc[1].title, "Gamma Release");
      assert.equal(asc[2].title, "Alpha Release");
    });

    it("sorts by date asc and desc, handling null dates", () => {
      const desc = sortReleases(sampleReleases, "date", "desc");
      assert.equal(desc[0].title, "Alpha Release"); // 2026-01-05
      assert.equal(desc[1].title, "Beta Release"); // 2026-01-02
      assert.equal(desc[2].title, "Gamma Release"); // null

      const asc = sortReleases(sampleReleases, "date", "asc");
      assert.equal(asc[0].title, "Gamma Release");
      assert.equal(asc[1].title, "Beta Release");
      assert.equal(asc[2].title, "Alpha Release");
    });
  });

  describe("paginateReleases", () => {
    const items = Array.from({ length: 55 }, (_, i) => ({
      title: `Release ${i + 1}`,
      size: i * 100,
    }));

    it("handles empty items array gracefully", () => {
      const res = paginateReleases([], 1, 25);
      assert.equal(res.totalCount, 0);
      assert.equal(res.totalPages, 1);
      assert.equal(res.currentPage, 1);
      assert.equal(res.items.length, 0);
    });

    it("paginates page 1 correctly", () => {
      const res = paginateReleases(items, 1, 25);
      assert.equal(res.totalCount, 55);
      assert.equal(res.totalPages, 3);
      assert.equal(res.currentPage, 1);
      assert.equal(res.startIndex, 0);
      assert.equal(res.endIndex, 25);
      assert.equal(res.items.length, 25);
      assert.equal(res.items[0].title, "Release 1");
      assert.equal(res.items[24].title, "Release 25");
    });

    it("paginates page 2 correctly", () => {
      const res = paginateReleases(items, 2, 25);
      assert.equal(res.currentPage, 2);
      assert.equal(res.startIndex, 25);
      assert.equal(res.endIndex, 50);
      assert.equal(res.items.length, 25);
      assert.equal(res.items[0].title, "Release 26");
    });

    it("paginates final page correctly", () => {
      const res = paginateReleases(items, 3, 25);
      assert.equal(res.currentPage, 3);
      assert.equal(res.startIndex, 50);
      assert.equal(res.endIndex, 55);
      assert.equal(res.items.length, 5);
      assert.equal(res.items[4].title, "Release 55");
    });

    it("clamps requested page that exceeds totalPages", () => {
      const res = paginateReleases(items, 999, 25);
      assert.equal(res.currentPage, 3);
      assert.equal(res.items.length, 5);
    });
  });
});

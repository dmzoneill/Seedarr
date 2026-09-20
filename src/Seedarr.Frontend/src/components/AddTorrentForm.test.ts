import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  buildExistingHashesSet,
  isReleaseInLibrary,
  isReleaseAdded,
  getReleaseButtonState,
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
      assert.equal(isReleaseInLibrary({ infoHash: undefined }, existingHashes), false);
      assert.equal(isReleaseInLibrary({ infoHash: null }, existingHashes), false);
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

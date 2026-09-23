import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { buildMagnetLink } from "./TorrentContextMenu";
import { copyToClipboard } from "../utils/clipboard";
import type { Torrent } from "../api/types";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Ubuntu 22.04 LTS",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    totalSize: 3800000000,
    pieceCount: 1000,
    pieceLength: 3800000,
    comment: null,
    createdBy: null,
    creationDate: null,
    isPrivate: false,
    status: "downloading",
    uploaded: 0,
    downloaded: 0,
    ratio: 0,
    progress: 0.5,
    seeders: 10,
    leechers: 2,
    trackerUrl: "http://primary-tracker.example.com/announce",
    sourcePath: null,
    dateAdded: "2026-01-01T00:00:00Z",
    lastActive: null,
    priority: 1,
    uploadLimit: 0,
    downloadLimit: 0,
    superSeeding: false,
    forceStart: false,
    label: null,
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
    sortOrder: 1,
    forceCompleted: false,
    seedingTime: 0,
    ...overrides,
  };
}

interface MockElement {
  tagName: string;
  value: string;
  style: Record<string, string>;
  setAttribute: (name: string, val: string) => void;
  select: () => void;
  setSelectionRange: (start: number, end: number) => void;
  parentNode: unknown;
}

interface MockDocState {
  createdElement: MockElement | null;
  appendedChild: MockElement | null;
  removedChild: MockElement | null;
  execCommandCalledWith: string | null;
  selected: boolean;
}

describe("TorrentContextMenu: buildMagnetLink", () => {
  it("should return t.magnetLink directly when t.magnetLink is present", () => {
    const existingMagnet =
      "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Custom+Name&tr=http%3A%2F%2Fcustom-tracker.com%2Fannounce";
    const torrent = createMockTorrent({
      magnetLink: existingMagnet,
    });

    const result = buildMagnetLink(torrent);
    assert.equal(result, existingMagnet);
  });

  it("should build magnet link with multi-trackers from t.trackers", () => {
    const torrent = createMockTorrent({
      magnetLink: null,
      trackerUrl: "http://tracker-primary.org/announce",
      trackers: [
        "http://tracker-primary.org/announce",
        "http://tracker-backup.org/announce",
        "udp://tracker-udp.org:1337/announce",
      ],
    });

    const result = buildMagnetLink(torrent);
    assert.ok(
      result.startsWith(
        "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Ubuntu%2022.04%20LTS",
      ),
    );
    assert.ok(
      result.includes("&tr=http%3A%2F%2Ftracker-primary.org%2Fannounce"),
    );
    assert.ok(
      result.includes("&tr=http%3A%2F%2Ftracker-backup.org%2Fannounce"),
    );
    assert.ok(
      result.includes("&tr=udp%3A%2F%2Ftracker-udp.org%3A1337%2Fannounce"),
    );
  });

  it("should deduplicate identical trackers from t.trackers and t.trackerUrl", () => {
    const torrent = createMockTorrent({
      magnetLink: undefined,
      trackerUrl: "http://tracker-dup.org/announce",
      trackers: [
        "http://tracker-dup.org/announce",
        "http://tracker-dup.org/announce",
        "http://tracker-unique.org/announce",
        "http://tracker-dup.org/announce",
      ],
    });

    const result = buildMagnetLink(torrent);
    const primaryMatches = result.match(
      /&tr=http%3A%2F%2Ftracker-dup\.org%2Fannounce/g,
    );
    assert.equal(
      primaryMatches?.length,
      1,
      "Duplicate tracker should only appear once in magnet link",
    );
    const uniqueMatches = result.match(
      /&tr=http%3A%2F%2Ftracker-unique\.org%2Fannounce/g,
    );
    assert.equal(
      uniqueMatches?.length,
      1,
      "Unique tracker should appear once in magnet link",
    );
  });
});

describe("TorrentContextMenu: copyToClipboard fallback", () => {
  let originalNavigator: unknown;
  let originalDocument: unknown;

  beforeEach(() => {
    const g = globalThis as Record<string, unknown>;
    originalNavigator = g.navigator;
    originalDocument = g.document;
  });

  afterEach(() => {
    const g = globalThis as Record<string, unknown>;
    g.navigator = originalNavigator;
    g.document = originalDocument;
  });

  it("should fall back to document.execCommand when navigator.clipboard is undefined", async () => {
    const g = globalThis as Record<string, unknown>;
    g.navigator = {};

    const state: MockDocState = {
      createdElement: null,
      appendedChild: null,
      removedChild: null,
      execCommandCalledWith: null,
      selected: false,
    };

    const mockDoc = {
      createElement: (tag: string): MockElement => {
        const el: MockElement = {
          tagName: tag.toUpperCase(),
          value: "",
          style: {},
          setAttribute: (name: string, val: string) => {
            (el as unknown as Record<string, string>)[name] = val;
          },
          select: () => {
            state.selected = true;
          },
          setSelectionRange: () => {},
          parentNode: null,
        };
        state.createdElement = el;
        return el;
      },
      body: {
        appendChild: (el: MockElement) => {
          state.appendedChild = el;
          el.parentNode = mockDoc.body;
        },
        removeChild: (el: MockElement) => {
          state.removedChild = el;
          el.parentNode = null;
        },
      },
      execCommand: (command: string) => {
        state.execCommandCalledWith = command;
        return true;
      },
    };
    g.document = mockDoc;

    const textToCopy = "magnet:?xt=urn:btih:0123456789abcdef";
    const result = await copyToClipboard(textToCopy);

    assert.equal(result, true);
    assert.equal(state.execCommandCalledWith, "copy");
    assert.ok(state.createdElement);
    assert.equal(state.createdElement.value, textToCopy);
    assert.equal(state.selected, true);
    assert.equal(state.appendedChild, state.createdElement);
    assert.equal(state.removedChild, state.createdElement);
  });

  it("should use navigator.clipboard.writeText when available", async () => {
    const g = globalThis as Record<string, unknown>;
    let writtenText: string | null = null;
    g.navigator = {
      clipboard: {
        writeText: async (text: string) => {
          writtenText = text;
        },
      },
    };

    const textToCopy = "test-copy-text";
    const result = await copyToClipboard(textToCopy);

    assert.equal(result, true);
    assert.equal(writtenText, textToCopy);
  });
});

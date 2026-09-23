import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
(globalThis as any).React = React;
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TorrentTable from "./TorrentTable";
import TorrentGrid from "./TorrentGrid";
import type { Torrent } from "../api/types";

function createMockTorrent(overrides: Partial<Torrent> = {}): Torrent {
  return {
    id: 1,
    name: "Ubuntu.24.04.Desktop.iso",
    infoHash: "0123456789abcdef0123456789abcdef01234567",
    mediaTitle: "Ubuntu 24.04",
    status: "Downloading",
    progress: 0.65,
    totalSize: 4_500_000_000,
    downloadSpeed: 12_500_000,
    uploadSpeed: 1_200_000,
    uploaded: 500_000_000,
    downloaded: 2_925_000_000,
    sessionUploaded: 500_000_000,
    sessionDownloaded: 2_925_000_000,
    ratio: 0.17,
    seeders: 120,
    leechers: 15,
    trackerUrl: "udp://tracker.opentrackr.org:1337/announce",
    announceInterval: 1800,
    nextUpdate: 900,
    dateAdded: "2026-09-18T10:00:00Z",
    lastActive: "2026-09-20T09:00:00Z",
    creationDate: "2026-04-20T00:00:00Z",
    pieceCount: 1000,
    pieceLength: 4_500_000,
    priority: 1,
    uploadLimit: 0,
    downloadLimit: 0,
    isPrivate: false,
    superSeeding: false,
    sequentialDownload: false,
    forceStart: false,
    active: true,
    availability: 1.0,
    eta: 126,
    threshold: 0,
    smallTorrentLimit: 0,
    ...overrides,
  } as unknown as Torrent;
}

function createQueryClient(torrents: Torrent[] = []) {
  const qc = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });
  qc.setQueryData(["torrents"], torrents);
  qc.setQueryData(["download-history"], []);
  qc.setQueryData(["arr-connections"], []);
  return qc;
}

describe("TorrentTable Accessibility (Issue #349)", () => {
  it("renders header select-all checkbox with accessible aria-label", () => {
    const torrent = createMockTorrent();
    const qc = createQueryClient([torrent]);

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentTable, {
          torrents: [torrent],
        }),
      ),
    );

    assert.ok(
      html.includes('aria-label="Select all torrents"'),
      'Header select-all checkbox must declare aria-label="Select all torrents"',
    );
  });

  it('renders table rows with role="row", tabIndex=0, and correct aria-selected state', () => {
    const torrent1 = createMockTorrent({ id: 1, name: "Torrent 1" });
    const torrent2 = createMockTorrent({ id: 2, name: "Torrent 2" });
    const qc = createQueryClient([torrent1, torrent2]);

    // Selected by selectedTorrentId
    const htmlSelected1 = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentTable, {
          torrents: [torrent1, torrent2],
          selectedTorrentId: 1,
        }),
      ),
    );

    assert.ok(
      htmlSelected1.includes('role="row"'),
      'Table rows must render with role="row"',
    );
    assert.ok(
      htmlSelected1.includes('tabindex="0"'),
      'Table rows must render with tabindex="0"',
    );
    assert.ok(
      htmlSelected1.includes('aria-selected="true"'),
      'Selected row must declare aria-selected="true"',
    );
    assert.ok(
      htmlSelected1.includes('aria-selected="false"'),
      'Unselected row must declare aria-selected="false"',
    );

    // Selected by selectedIds set
    const htmlSelected2 = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentTable, {
          torrents: [torrent1, torrent2],
          selectedIds: new Set([2]),
        }),
      ),
    );

    assert.ok(
      htmlSelected2.includes('aria-selected="true"'),
      'Row selected via selectedIds must declare aria-selected="true"',
    );
  });

  it("renders row checkboxes with accessible aria-label containing torrent name", () => {
    const torrent1 = createMockTorrent({
      id: 10,
      name: "ArchLinux-2026.09.iso",
    });
    const torrent2 = createMockTorrent({
      id: 20,
      name: "Fedora-Workstation-42.iso",
    });
    const qc = createQueryClient([torrent1, torrent2]);

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentTable, {
          torrents: [torrent1, torrent2],
        }),
      ),
    );

    assert.ok(
      html.includes('aria-label="Select ArchLinux-2026.09.iso"'),
      'Row checkbox must declare aria-label="Select ArchLinux-2026.09.iso"',
    );
    assert.ok(
      html.includes('aria-label="Select Fedora-Workstation-42.iso"'),
      'Row checkbox must declare aria-label="Select Fedora-Workstation-42.iso"',
    );
  });

  it("handles keyboard events on table row: Space toggles selection, Enter activates torrent", () => {
    const torrent = createMockTorrent({
      id: 42,
      name: "Debian-13-netinst.iso",
    });
    const qc = createQueryClient([torrent]);

    let capturedTrProps: any = null;
    const origCreateElement = React.createElement;

    let toggledId: number | null = null;
    let selectedId: number | null | undefined = undefined;

    try {
      (React as any).createElement = function (
        type: any,
        props: any,
        ...children: any[]
      ) {
        if (type === "tr" && props?.role === "row") {
          capturedTrProps = props;
        }
        return origCreateElement.apply(this, [type, props, ...children]);
      };

      renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(TorrentTable, {
            torrents: [torrent],
            selectedTorrentId: null,
            onToggleSelect: (id: number) => {
              toggledId = id;
            },
            onSelectTorrent: (id: number | null) => {
              selectedId = id;
            },
          }),
        ),
      );
    } finally {
      (React as any).createElement = origCreateElement;
    }

    assert.ok(capturedTrProps, "Must capture row props");
    assert.strictEqual(capturedTrProps.role, "row");
    assert.strictEqual(capturedTrProps.tabIndex, 0);
    assert.strictEqual(typeof capturedTrProps.onKeyDown, "function");

    // Space key: toggles selection
    let spacePrevented = false;
    let spaceStopped = false;
    capturedTrProps.onKeyDown({
      key: " ",
      preventDefault: () => {
        spacePrevented = true;
      },
      stopPropagation: () => {
        spaceStopped = true;
      },
    });

    assert.strictEqual(
      toggledId,
      42,
      "Space key must toggle selection for torrent 42",
    );
    assert.ok(spacePrevented, "Space key must call preventDefault");
    assert.ok(spaceStopped, "Space key must call stopPropagation");

    // Enter key: activates torrent
    let enterPrevented = false;
    let enterStopped = false;
    capturedTrProps.onKeyDown({
      key: "Enter",
      preventDefault: () => {
        enterPrevented = true;
      },
      stopPropagation: () => {
        enterStopped = true;
      },
    });

    assert.strictEqual(selectedId, 42, "Enter key must select torrent 42");
    assert.ok(enterPrevented, "Enter key must call preventDefault");
    assert.ok(enterStopped, "Enter key must call stopPropagation");

    // Other keys (e.g. Tab) should not trigger selection or activation
    toggledId = null;
    selectedId = undefined;
    capturedTrProps.onKeyDown({
      key: "Tab",
      preventDefault: () => {},
      stopPropagation: () => {},
    });
    assert.strictEqual(
      toggledId,
      null,
      "Tab key must not trigger toggle select",
    );
    assert.strictEqual(
      selectedId,
      undefined,
      "Tab key must not trigger select",
    );
  });
});

describe("TorrentGrid Accessibility (Issue #349)", () => {
  it('renders grid cards with role="button", tabIndex=0, and aria-selected', () => {
    const torrent1 = createMockTorrent({
      id: 1,
      name: "Ubuntu",
      mediaTitle: "Ubuntu Linux",
    });
    const torrent2 = createMockTorrent({
      id: 2,
      name: "Debian",
      mediaTitle: "Debian GNU/Linux",
    });
    const qc = createQueryClient([torrent1, torrent2]);

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentGrid, {
          torrents: [torrent1, torrent2],
          selectedTorrentId: 1,
        }),
      ),
    );

    assert.ok(
      html.includes('role="button"'),
      'Grid card must render with role="button"',
    );
    assert.ok(
      html.includes('tabindex="0"'),
      'Grid card must render with tabindex="0"',
    );
    assert.ok(
      html.includes('aria-selected="true"'),
      'Selected grid card must declare aria-selected="true"',
    );
    assert.ok(
      html.includes('aria-selected="false"'),
      'Unselected grid card must declare aria-selected="false"',
    );
  });

  it("renders grid card checkbox with accessible aria-label using displayTitle", () => {
    const torrentWithMediaTitle = createMockTorrent({
      id: 10,
      name: "Big.Buck.Bunny.1080p.mkv",
      mediaTitle: "Big Buck Bunny",
    });
    const torrentWithoutMediaTitle = createMockTorrent({
      id: 20,
      name: "Free.Software.Manual.pdf",
      mediaTitle: undefined,
    });
    const qc = createQueryClient([
      torrentWithMediaTitle,
      torrentWithoutMediaTitle,
    ]);

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: qc },
        React.createElement(TorrentGrid, {
          torrents: [torrentWithMediaTitle, torrentWithoutMediaTitle],
        }),
      ),
    );

    assert.ok(
      html.includes('aria-label="Select Big Buck Bunny"'),
      "Checkbox must use mediaTitle when available: Select Big Buck Bunny",
    );
    assert.ok(
      html.includes('aria-label="Select Free.Software.Manual.pdf"'),
      "Checkbox must fallback to name when mediaTitle is absent",
    );
  });

  it("handles keyboard events on grid card: Space toggles selection, Enter selects torrent", () => {
    const torrent = createMockTorrent({
      id: 99,
      name: "OpenSUSE.iso",
      mediaTitle: "openSUSE Leap",
    });
    const qc = createQueryClient([torrent]);

    let capturedCardProps: any = null;
    const origCreateElement = React.createElement;

    let toggledId: number | null = null;
    let selectedId: number | null | undefined = undefined;

    try {
      (React as any).createElement = function (
        type: any,
        props: any,
        ...children: any[]
      ) {
        if (type === "div" && props?.role === "button") {
          capturedCardProps = props;
        }
        return origCreateElement.apply(this, [type, props, ...children]);
      };

      renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(TorrentGrid, {
            torrents: [torrent],
            selectedTorrentId: null,
            onToggleSelect: (id: number) => {
              toggledId = id;
            },
            onSelectTorrent: (id: number | null) => {
              selectedId = id;
            },
          }),
        ),
      );
    } finally {
      (React as any).createElement = origCreateElement;
    }

    assert.ok(capturedCardProps, "Must capture card props");
    assert.strictEqual(capturedCardProps.role, "button");
    assert.strictEqual(capturedCardProps.tabIndex, 0);
    assert.strictEqual(typeof capturedCardProps.onKeyDown, "function");

    // Space key: toggles selection
    let spacePrevented = false;
    let spaceStopped = false;
    capturedCardProps.onKeyDown({
      key: " ",
      preventDefault: () => {
        spacePrevented = true;
      },
      stopPropagation: () => {
        spaceStopped = true;
      },
    });

    assert.strictEqual(
      toggledId,
      99,
      "Space key must toggle selection for torrent 99",
    );
    assert.ok(spacePrevented, "Space key must call preventDefault");
    assert.ok(spaceStopped, "Space key must call stopPropagation");

    // Enter key: selects torrent
    let enterPrevented = false;
    let enterStopped = false;
    capturedCardProps.onKeyDown({
      key: "Enter",
      preventDefault: () => {
        enterPrevented = true;
      },
      stopPropagation: () => {
        enterStopped = true;
      },
    });

    assert.strictEqual(selectedId, 99, "Enter key must select torrent 99");
    assert.ok(enterPrevented, "Enter key must call preventDefault");
    assert.ok(enterStopped, "Enter key must call stopPropagation");
  });
});

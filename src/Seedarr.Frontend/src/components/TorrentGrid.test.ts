import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import TorrentGrid, {
  torrentGridColumnCount,
  TORRENT_GRID_INITIAL_WIDTH,
} from "./TorrentGrid";
import type { Torrent } from "../api/types";

(globalThis as unknown as { React: typeof React }).React = React;

void describe("TorrentGrid load errors", () => {
  void it("shows a failure state instead of an empty torrent list", () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false, retryOnMount: false },
      },
    });
    const query = queryClient.getQueryCache().build(queryClient, {
      queryKey: ["torrents"],
      queryFn: () => Promise.reject(new Error("GET /torrent failed")),
    });
    query.setState({
      status: "error",
      fetchStatus: "idle",
      error: new Error("GET /torrent failed"),
      data: undefined,
    });

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(TorrentGrid, {}),
      ),
    );

    assert.ok(html.includes("Failed to load data."));
    assert.equal(html.includes("No torrents found"), false);
  });
});

void describe("TorrentGrid virtualization", () => {
  void it("derives a responsive column count from the viewport", () => {
    assert.equal(torrentGridColumnCount(200), 1);
    assert.equal(torrentGridColumnCount(TORRENT_GRID_INITIAL_WIDTH), 4);
    assert.ok(torrentGridColumnCount(2000) > 4);
  });

  void it("renders only the visible poster rows for a large library", () => {
    const torrents = Array.from({ length: 48 }, (_, index) =>
      ({
        id: index + 1,
        name: `Torrent ${index + 1}`,
        mediaTitle: index === 0 ? "Visible First" : index === 47 ? "Hidden Last" : `Title ${index + 1}`,
        infoHash: `hash-${index + 1}`,
        totalSize: 1000,
        pieceCount: 1,
        pieceLength: 1000,
        comment: null,
        createdBy: null,
        creationDate: null,
        isPrivate: false,
        status: "Seeding",
        uploaded: 1,
        downloaded: 1,
        ratio: 1,
        progress: 1,
        seeders: 1,
        leechers: 0,
        trackerUrl: null,
        sourcePath: null,
        dateAdded: "2026-01-01T00:00:00Z",
        lastActive: null,
        priority: 0,
        uploadLimit: 0,
        downloadLimit: 0,
        superSeeding: false,
        forceStart: false,
        label: null,
        category: null,
        savePath: null,
        sequentialDownload: false,
        announceInterval: 1800,
        nextUpdate: 0,
        sessionUploaded: 0,
        sessionDownloaded: 0,
        smallTorrentLimit: 0,
        threshold: 0,
        uploadSpeed: 0,
        downloadSpeed: 0,
        active: true,
        availability: 1,
        eta: 0,
        sortOrder: index,
        forceCompleted: false,
        seedingTime: 0,
      }) satisfies Torrent,
    );
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    queryClient.setQueryData(["torrents"], torrents);

    const html = renderToStaticMarkup(
      React.createElement(
        QueryClientProvider,
        { client: queryClient },
        React.createElement(TorrentGrid, { torrents }),
      ),
    );

    const rendered = html.match(/aria-label="Select /g) ?? [];
    assert.ok(html.includes('aria-label="Select Visible First"'));
    assert.equal(html.includes('aria-label="Select Hidden Last"'), false);
    assert.ok(rendered.length > 0);
    assert.ok(rendered.length < torrents.length);
  });
});

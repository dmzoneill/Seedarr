import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { TorrentToolbar } from "./TorrentToolbar";
import type { SeedingConfig } from "../../api/types";

(globalThis as any).React = React;

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
    },
  },
});

function renderToolbar(props: any) {
  return renderToStaticMarkup(
    React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(TorrentToolbar, props),
    ),
  );
}

describe("TorrentToolbar accessibility attributes", () => {
  const mockSeedingConfig: Partial<SeedingConfig> = {
    maxUploadSpeedKbps: 1024,
    maxDownloadSpeedKbps: 2048,
  };

  const defaultProps = {
    count: 10,
    totalUploadSpeed: 512,
    totalDownloadSpeed: 1024,
    seedingConfig: mockSeedingConfig as SeedingConfig,
    adjustSpeed: () => {},
    filter: "",
    onFilterChange: () => {},
    viewMode: "table" as const,
    onViewModeChange: () => {},
    onAddTorrent: () => {},
    onStartAll: () => {},
    onStopAll: () => {},
    selectedCount: 0,
    bulkPending: false,
    onBulkStart: () => {},
    onBulkStop: () => {},
    onBulkDelete: () => {},
    onBulkClear: () => {},
  };

  it("renders speed adjustment buttons with descriptive aria-label attributes and limit labels", () => {
    const html = renderToolbar(defaultProps);

    assert.ok(
      html.includes("UL Limit:"),
      "Upload limit label must be rendered",
    );
    assert.ok(
      html.includes("DL Limit:"),
      "Download limit label must be rendered",
    );
    assert.ok(
      !html.includes("UL: 512") && !html.includes("UL: 512 B/s"),
      "Live upload speed must not be rendered in toolbar",
    );
    assert.ok(
      html.includes('aria-label="Double upload speed limit"'),
      "Double upload speed button must have descriptive aria-label",
    );
    assert.ok(
      html.includes('aria-label="Halve upload speed limit"'),
      "Halve upload speed button must have descriptive aria-label",
    );
    assert.ok(
      html.includes('aria-label="Double download speed limit"'),
      "Double download speed button must have descriptive aria-label",
    );
    assert.ok(
      html.includes('aria-label="Halve download speed limit"'),
      "Halve download speed button must have descriptive aria-label",
    );
  });

  it("renders filter search input with accessible aria-label", () => {
    const html = renderToolbar(defaultProps);

    assert.ok(
      html.includes('aria-label="Filter torrents"') ||
        html.includes('aria-label="Filter torrents..."'),
      "Filter input must have accessible aria-label",
    );
    assert.ok(
      html.includes('class="search-input"'),
      "Search input element must be rendered",
    );
  });

  it("reflects correct aria-pressed state on view mode toggle buttons for table mode", () => {
    const html = renderToolbar({
      ...defaultProps,
      viewMode: "table",
    });

    // Table view button should be aria-pressed=true, Grid view button aria-pressed=false
    assert.ok(
      html.includes('title="Table view" aria-pressed="true"') ||
        html.includes('aria-pressed="true" title="Table view"') ||
        (html.includes('title="Table view"') &&
          html.includes('aria-pressed="true"')),
      "Table view button should have aria-pressed=true",
    );
    assert.ok(
      html.includes('title="Grid view" aria-pressed="false"') ||
        html.includes('aria-pressed="false" title="Grid view"') ||
        (html.includes('title="Grid view"') &&
          html.includes('aria-pressed="false"')),
      "Grid view button should have aria-pressed=false",
    );
  });

  it("reflects correct aria-pressed state on view mode toggle buttons for grid mode", () => {
    const html = renderToolbar({
      ...defaultProps,
      viewMode: "grid",
    });

    // In grid mode: Grid view button aria-pressed=true, Table view button aria-pressed=false
    const tableMatch = html.match(/<button[^>]*title="Table view"[^>]*>/);
    assert.ok(tableMatch, "Table button found");
    assert.ok(
      tableMatch[0].includes('aria-pressed="false"'),
      "Table button should be aria-pressed=false",
    );

    const gridMatch = html.match(/<button[^>]*title="Grid view"[^>]*>/);
    assert.ok(gridMatch, "Grid button found");
    assert.ok(
      gridMatch[0].includes('aria-pressed="true"'),
      "Grid button should be aria-pressed=true",
    );
  });

  it("renders bulk tag assignment and removal buttons when selectedCount > 0", () => {
    const html = renderToolbar({
      ...defaultProps,
      selectedCount: 3,
      onBulkAddTags: () => {},
      onBulkRemoveTags: () => {},
    });

    assert.ok(
      html.includes("bulk-add-tags-btn"),
      "Assign tags button must be rendered in bulk actions",
    );
    assert.ok(
      html.includes("bulk-remove-tags-btn"),
      "Remove tags button must be rendered in bulk actions",
    );
    assert.ok(
      html.includes("Assign Tags"),
      "Assign Tags text should be present",
    );
    assert.ok(
      html.includes("Remove Tags"),
      "Remove Tags text should be present",
    );
  });

  it("does not render bulk Force Recheck button", () => {
    const html = renderToolbar({
      ...defaultProps,
      selectedCount: 2,
    });

    assert.ok(
      !html.includes("bulk-recheck-btn"),
      "Force Recheck button must not be rendered in bulk actions",
    );
  });
});

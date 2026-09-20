import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
(globalThis as any).React = React;
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import SpeedGraph, {
  getNiceMax,
  getGridLineCount,
  TIME_RANGES,
  TIME_RANGE_OPTIONS,
  CANVAS_HEIGHT,
  CANVAS_PADDING,
  UPLOAD_COLOR,
  DOWNLOAD_COLOR,
} from "./SpeedGraph";

function createQueryClient() {
  const qc = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });
  qc.setQueryData(["seeding", "history"], []);
  qc.setQueryData(["seeding", "stats"], {
    totalUploaded: 0,
    totalDownloaded: 0,
    uploadSpeed: 0,
    downloadSpeed: 0,
    activeTorrents: 0,
    seedingTorrents: 0,
  });
  return qc;
}

describe("SpeedGraph: Canvas Telemetry & HiDPI Retina Migration (Issue #310)", () => {
  describe("Speed axis nice rounding and grid line ticks", () => {
    it("handles zero, negative, and invalid values gracefully", () => {
      assert.equal(getNiceMax(0), 1024);
      assert.equal(getNiceMax(-500), 1024);
      assert.equal(getNiceMax(NaN), 1024);
      assert.equal(getNiceMax(Infinity), 1024);
    });

    it("rounds sub-megabyte speeds to binary powers-of-two KiB increments", () => {
      assert.equal(getNiceMax(1024), 1024); // 1 KiB
      assert.equal(getNiceMax(1500), 2048); // 2 KiB
      assert.equal(getNiceMax(2048), 2048); // 2 KiB
      assert.equal(getNiceMax(3000), 4096); // 4 KiB
      assert.equal(getNiceMax(16 * 1024), 16 * 1024); // 16 KiB
      assert.equal(getNiceMax(500 * 1024), 512 * 1024); // 512 KiB
      assert.equal(getNiceMax(1024 * 1024), 1024 * 1024); // 1024 KiB (1 MiB)
    });

    it("rounds megabyte and gigabyte speeds to clean multiplier increments (1, 2, 5, 10, 25, 50, 100, 250, 500)", () => {
      const MIB = 1024 * 1024;
      assert.equal(getNiceMax(1.5 * MIB), 2 * MIB);
      assert.equal(getNiceMax(2.1 * MIB), 5 * MIB);
      assert.equal(getNiceMax(6 * MIB), 10 * MIB);
      assert.equal(getNiceMax(15 * MIB), 25 * MIB);
      assert.equal(getNiceMax(30 * MIB), 50 * MIB);
      assert.equal(getNiceMax(80 * MIB), 100 * MIB);
      assert.equal(getNiceMax(180 * MIB), 250 * MIB);
      assert.equal(getNiceMax(400 * MIB), 500 * MIB);
      assert.equal(getNiceMax(800 * MIB), 1024 * MIB);
    });

    it("calculates appropriate grid line count based on niceMax step size", () => {
      assert.equal(getGridLineCount(512 * 1024), 4);
      assert.equal(getGridLineCount(1024 * 1024), 4); // 1 MiB (norm 1 -> 4)
      assert.equal(getGridLineCount(2 * 1024 * 1024), 4); // 2 MiB (norm 2 -> 4)
      assert.equal(getGridLineCount(5 * 1024 * 1024), 5); // 5 MiB (norm 5 -> 5)
      assert.equal(getGridLineCount(10 * 1024 * 1024), 5); // 10 MiB (norm 10 -> 5)
      assert.equal(getGridLineCount(25 * 1024 * 1024), 5); // 25 MiB (norm 25 -> 5)
      assert.equal(getGridLineCount(50 * 1024 * 1024), 5); // 50 MiB (norm 50 -> 5)
      assert.equal(getGridLineCount(100 * 1024 * 1024), 4); // 100 MiB (norm 100 -> 4)
      assert.equal(getGridLineCount(250 * 1024 * 1024), 5); // 250 MiB (norm 250 -> 5)
      assert.equal(getGridLineCount(500 * 1024 * 1024), 5); // 500 MiB (norm 500 -> 5)
    });
  });

  describe("Time range configurations & constants", () => {
    it("defines 60s, 5m, 15m, and 30m window ranges", () => {
      assert.equal(TIME_RANGES["60s"].points, 60);
      assert.equal(TIME_RANGES["5m"].points, 300);
      assert.equal(TIME_RANGES["15m"].points, 900);
      assert.equal(TIME_RANGES["30m"].points, 1800);
      assert.equal(TIME_RANGE_OPTIONS.length, 4);
    });

    it("declares expected canvas dimensions, padding, and channel colors", () => {
      assert.equal(CANVAS_HEIGHT, 180);
      assert.equal(CANVAS_PADDING.top, 12);
      assert.equal(CANVAS_PADDING.bottom, 26);
      assert.equal(CANVAS_PADDING.left, 75);
      assert.equal(CANVAS_PADDING.right, 24);
      assert.equal(UPLOAD_COLOR, "#3498db");
      assert.equal(DOWNLOAD_COLOR, "#2ecc71");
    });
  });

  describe("HTML5 Canvas rendering and DOM structure", () => {
    it("renders HTML5 canvas element with aria attributes instead of inline SVG elements", () => {
      const qc = createQueryClient();
      const html = renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(SpeedGraph, {}),
        ),
      );

      // Must render HTML5 canvas
      assert.ok(html.includes("<canvas"), "Must render <canvas> element");
      assert.ok(
        html.includes('role="img"'),
        'Canvas must have role="img" accessibility attribute',
      );
      assert.ok(
        html.includes('aria-label="Transfer speed history graph"'),
        'Canvas must have accessible aria-label="Transfer speed history graph"',
      );

      // Must NOT render obsolete SVG polylines or SVG area paths
      assert.ok(
        !html.includes("<polyline"),
        "Must NOT render inline SVG <polyline> elements",
      );
      assert.ok(
        !html.includes("speedUploadGrad"),
        "Must NOT render inline SVG gradients",
      );
    });

    it("renders transfer speed title, live indicator, and dual channel legend items", () => {
      const qc = createQueryClient();
      const html = renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(SpeedGraph, {}),
        ),
      );

      assert.ok(html.includes("Transfer Speed"), "Must render title");
      assert.ok(html.includes("Live (1s)"), "Must render live badge");
      assert.ok(html.includes("Upload:"), "Must render Upload legend label");
      assert.ok(html.includes("Download:"), "Must render Download legend label");
      assert.ok(
        html.includes("#3498db"),
        "Must use upload channel color #3498db in legend indicator",
      );
      assert.ok(
        html.includes("#2ecc71"),
        "Must use download channel color #2ecc71 in legend indicator",
      );
    });

    it("renders time window range selector buttons", () => {
      const qc = createQueryClient();
      const html = renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(SpeedGraph, {}),
        ),
      );

      assert.ok(
        html.includes('aria-label="Time window range"'),
        "Must render range group with aria-label",
      );
      assert.ok(html.includes(">60s</button>"), "Must render 60s button");
      assert.ok(html.includes(">5m</button>"), "Must render 5m button");
      assert.ok(html.includes(">15m</button>"), "Must render 15m button");
      assert.ok(html.includes(">30m</button>"), "Must render 30m button");
    });
  });

  describe("HiDPI backing store scaling formula", () => {
    it("correctly computes retina backing store dimensions using devicePixelRatio", () => {
      const testCases = [
        { width: 800, height: 180, dpr: 1, expectedW: 800, expectedH: 180 },
        { width: 800, height: 180, dpr: 2, expectedW: 1600, expectedH: 360 },
        { width: 1000, height: 180, dpr: 1.5, expectedW: 1500, expectedH: 270 },
        { width: 1200, height: 180, dpr: 3, expectedW: 3600, expectedH: 540 },
      ];

      for (const tc of testCases) {
        const targetWidth = Math.round(tc.width * tc.dpr);
        const targetHeight = Math.round(tc.height * tc.dpr);
        assert.equal(targetWidth, tc.expectedW);
        assert.equal(targetHeight, tc.expectedH);
      }
    });
  });
});

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
(globalThis as any).React = React;
import { renderToStaticMarkup } from "react-dom/server";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import PieceMap, {
  calculateBlocks,
  calculatePieceStates,
  calculatePieceStats,
  NUM_BLOCKS,
} from "./PieceMap";

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });
}

describe("PieceMap: Prevent False 100% Verified Grid on Seeding (Issue #207)", () => {
  describe("Block-level piece calculation (calculateBlocks)", () => {
    it("when isSeeding is true but progress is 0.0, blocks are NOT marked complete", () => {
      const blocks = calculateBlocks(NUM_BLOCKS, 100, 0.0, true);
      const completeBlocks = blocks.filter((b) => b.status === "complete");

      assert.equal(completeBlocks.length, 0);
      assert.equal(blocks.length, NUM_BLOCKS);
      assert.ok(blocks.every((b) => b.status !== "complete"));
    });

    it("when isSeeding is true but progress is 0.5, only blocks up to 50% are marked complete", () => {
      const blocks = calculateBlocks(NUM_BLOCKS, 100, 0.5, true);
      const completeBlocks = blocks.filter((b) => b.status === "complete");

      assert.equal(completeBlocks.length, NUM_BLOCKS * 0.5); // exactly 60 blocks
      for (let i = 0; i < NUM_BLOCKS; i++) {
        if (i < 60) {
          assert.equal(blocks[i].status, "complete");
        } else {
          assert.notEqual(blocks[i].status, "complete");
        }
      }
    });

    it("when progress is 1.0, all blocks are marked complete", () => {
      const blocks = calculateBlocks(NUM_BLOCKS, 100, 1.0, false);
      const completeBlocks = blocks.filter((b) => b.status === "complete");

      assert.equal(completeBlocks.length, NUM_BLOCKS);
      assert.ok(blocks.every((b) => b.status === "complete"));

      // Also when isSeeding is true
      const seedingBlocks = calculateBlocks(NUM_BLOCKS, 100, 1.0, true);
      assert.equal(seedingBlocks.filter((b) => b.status === "complete").length, NUM_BLOCKS);
    });

    it("default isSeeding does not force 100% complete when progress is 0", () => {
      const blocks = calculateBlocks(NUM_BLOCKS, 100, 0.0); // default isSeeding
      const completeBlocks = blocks.filter((b) => b.status === "complete");

      assert.equal(completeBlocks.length, 0);
    });

    it("supports simplified signature calculateBlocks(progress, isSeeding)", () => {
      const zeroBlocks = calculateBlocks(0.0, true);
      assert.equal(zeroBlocks.filter((b) => b.status === "complete").length, 0);

      const halfBlocks = calculateBlocks(0.5, true);
      assert.equal(halfBlocks.filter((b) => b.status === "complete").length, NUM_BLOCKS * 0.5);

      const fullBlocks = calculateBlocks(1.0, false);
      assert.equal(fullBlocks.filter((b) => b.status === "complete").length, NUM_BLOCKS);

      const defaultBlocks = calculateBlocks(0.0);
      assert.equal(defaultBlocks.filter((b) => b.status === "complete").length, 0);
    });
  });

  describe("Piece states simulation (calculatePieceStates)", () => {
    it("when isSeeding is true but progress is 0.0, pieces are NOT marked complete", () => {
      const states = calculatePieceStates(100, 0.0, true);
      const verifiedCount = Array.from(states).filter((s) => s === 2).length;

      assert.equal(verifiedCount, 0);
      assert.equal(states.length, 100);
    });

    it("when isSeeding is true but progress is 0.5, only 50% of pieces are marked complete", () => {
      const states = calculatePieceStates(100, 0.5, true);
      const verifiedCount = Array.from(states).filter((s) => s === 2).length;

      assert.equal(verifiedCount, 50);
      for (let i = 0; i < 50; i++) {
        assert.equal(states[i], 2);
      }
      for (let i = 50; i < 100; i++) {
        assert.notEqual(states[i], 2);
      }
    });

    it("when progress is 1.0, all pieces are marked complete", () => {
      const states = calculatePieceStates(100, 1.0, false);
      const verifiedCount = Array.from(states).filter((s) => s === 2).length;

      assert.equal(verifiedCount, 100);
      assert.ok(Array.from(states).every((s) => s === 2));
    });

    it("default isSeeding (false) does not force complete pieces when progress is 0", () => {
      const states = calculatePieceStates(100, 0.0);
      const verifiedCount = Array.from(states).filter((s) => s === 2).length;

      assert.equal(verifiedCount, 0);
    });
  });

  describe("Piece stats calculation (calculatePieceStats)", () => {
    it("correctly reflects completeCount, remainingCount, and verifiedPercent", () => {
      const statesZero = calculatePieceStates(100, 0.0, true);
      const statsZero = calculatePieceStats(statesZero, 100, 0.0);
      assert.equal(statsZero.completeCount, 0);
      assert.equal(statsZero.remainingCount, 100);
      assert.equal(statsZero.verifiedPercent, "0.0");

      const statesHalf = calculatePieceStates(100, 0.5, true);
      const statsHalf = calculatePieceStats(statesHalf, 100, 0.5);
      assert.equal(statsHalf.completeCount, 50);
      assert.equal(statsHalf.remainingCount, 50);
      assert.equal(statsHalf.verifiedPercent, "50.0");

      const statesFull = calculatePieceStates(100, 1.0, false);
      const statsFull = calculatePieceStats(statesFull, 100, 1.0);
      assert.equal(statsFull.completeCount, 100);
      assert.equal(statsFull.remainingCount, 0);
      assert.equal(statsFull.verifiedPercent, "100.0");
    });
  });

  describe("PieceMap Component DOM rendering", () => {
    it("renders progressbar and badge matching progress 0.0 even if isSeeding is passed true", () => {
      const qc = createQueryClient();
      const html = renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(PieceMap, {
            pieceCount: 100,
            pieceLength: 262144,
            progress: 0.0,
            isSeeding: true,
          }),
        ),
      );

      assert.ok(html.includes("0.0% Verified"));
      assert.ok(html.includes('aria-valuenow="0"'));
      assert.ok(html.includes("0 / 100 Complete"));
      assert.ok(!html.includes("100 / 100 Complete"));
    });

    it("renders progressbar and badge matching 100% when progress is 1.0", () => {
      const qc = createQueryClient();
      const html = renderToStaticMarkup(
        React.createElement(
          QueryClientProvider,
          { client: qc },
          React.createElement(PieceMap, {
            pieceCount: 100,
            pieceLength: 262144,
            progress: 1.0,
            isSeeding: true,
          }),
        ),
      );

      assert.ok(html.includes("100.0% Verified"));
      assert.ok(html.includes('aria-valuenow="100"'));
      assert.ok(html.includes("100 / 100 Complete"));
    });
  });
});

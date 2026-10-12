import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  binBitfieldBlocks,
  clearPieceBitInPlace,
  countVerifiedPieces,
  decodeBase64Bitfield,
  isPieceVerifiedInBitfield,
  mergeBitfields,
  POPCOUNT_8,
  setPieceBit,
  setPieceBitInPlace,
  setPieceBits,
  setPieceBitsInPlace,
  setPieceRangeInPlace,
  setPieceRanges,
  setPieceRangesInPlace,
} from "./pieceMapUtils";

describe("POPCOUNT_8", () => {
  it("counts bits for sample bytes", () => {
    assert.equal(POPCOUNT_8[0], 0);
    assert.equal(POPCOUNT_8[255], 8);
    assert.equal(POPCOUNT_8[0b10101010], 4);
  });
});

describe("decodeBase64Bitfield", () => {
  it("returns null for empty input", () => {
    assert.equal(decodeBase64Bitfield(null), null);
    assert.equal(decodeBase64Bitfield(""), null);
  });

  it("decodes valid base64", () => {
    const bytes = decodeBase64Bitfield("AQ==");
    assert.ok(bytes);
    assert.equal(bytes!.length, 1);
    assert.equal(bytes![0], 1);
  });

  it("returns null for invalid base64", () => {
    assert.equal(decodeBase64Bitfield("not!!!"), null);
  });
});

describe("bitfield mutations", () => {
  it("sets and clears single bits in place", () => {
    const field = new Uint8Array(2);
    setPieceBitInPlace(field, 0);
    assert.ok(isPieceVerifiedInBitfield(field, 0));
    clearPieceBitInPlace(field, 0);
    assert.equal(isPieceVerifiedInBitfield(field, 0), false);
  });

  it("sets ranges in place across byte boundaries", () => {
    const field = new Uint8Array(2);
    setPieceRangeInPlace(field, 0, 10);
    assert.ok(isPieceVerifiedInBitfield(field, 0));
    assert.ok(isPieceVerifiedInBitfield(field, 10));
  });

  it("ignores invalid ranges", () => {
    const field = new Uint8Array(1);
    setPieceRangeInPlace(field, 5, 2);
    assert.equal(countVerifiedPieces(field), 0);
  });

  it("setPieceRangesInPlace applies multiple ranges", () => {
    const field = new Uint8Array(2);
    setPieceRangesInPlace(field, [[0, 1], [8, 9]]);
    assert.ok(isPieceVerifiedInBitfield(field, 0));
    assert.ok(isPieceVerifiedInBitfield(field, 9));
  });

  it("allocating helpers grow buffers", () => {
    const next = setPieceBit(undefined, 15);
    assert.ok(isPieceVerifiedInBitfield(next, 15));
    const many = setPieceBits(next, [0, 1, 2]);
    assert.equal(countVerifiedPieces(many), 4);
    const ranged = setPieceRanges(many, [[20, 25]]);
    assert.ok(isPieceVerifiedInBitfield(ranged, 22));
  });

  it("setPieceBitsInPlace skips invalid indices", () => {
    const field = new Uint8Array(1);
    setPieceBitsInPlace(field, [-1, 1, 999]);
    assert.ok(isPieceVerifiedInBitfield(field, 1));
    assert.equal(countVerifiedPieces(field), 1);
  });

  it("mergeBitfields ORs bytes", () => {
    const a = new Uint8Array([0b00000001]);
    const b = new Uint8Array([0b00000010]);
    const merged = mergeBitfields(a, b);
    assert.equal(merged[0], 0b00000011);
  });

  it("countVerifiedPieces caps at totalPieces", () => {
    const field = new Uint8Array([0xff]);
    assert.equal(countVerifiedPieces(field, 3), 3);
  });
});

describe("binBitfieldBlocks", () => {
  it("marks complete when isComplete flag set", () => {
    const blocks = binBitfieldBlocks(null, 100, 10, true);
    assert.equal(blocks.length, 10);
    assert.equal(blocks[0].status, "complete");
    assert.equal(blocks[0].completedCount, blocks[0].totalInBlock);
  });

  it("classifies active vs missing from bitfield", () => {
    const field = new Uint8Array(4);
    setPieceBitInPlace(field, 0);
    const blocks = binBitfieldBlocks(field, 64, 4, false);
    const active = blocks.filter((b) => b.status === "active");
    const missing = blocks.filter((b) => b.status === "missing");
    assert.ok(active.length >= 1);
    assert.ok(missing.length >= 1);
  });
});

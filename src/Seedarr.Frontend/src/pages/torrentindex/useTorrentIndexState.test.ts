import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { selectionAfterSelectAll } from "./useTorrentIndexState";

void describe("selectionAfterSelectAll", () => {
  void it("selects the requested ids when the current selection merely has the same size", () => {
    const next = selectionAfterSelectAll(new Set([4, 5, 6]), [1, 2, 3]);
    assert.deepEqual([...next].sort((a, b) => a - b), [1, 2, 3]);
  });

  void it("selects the full visible set when only some members are selected", () => {
    const next = selectionAfterSelectAll(new Set([1, 2]), [1, 2, 3]);
    assert.deepEqual([...next].sort((a, b) => a - b), [1, 2, 3]);
  });

  void it("clears a selection that is exactly the requested membership", () => {
    const next = selectionAfterSelectAll(new Set([1, 2, 3]), [1, 2, 3]);
    assert.equal(next.size, 0);
  });

  void it("removes only the requested members and keeps ids outside the list", () => {
    const next = selectionAfterSelectAll(new Set([1, 2, 3, 9]), [1, 2, 3]);
    assert.deepEqual([...next], [9]);
  });

  void it("clears when the requested list is empty", () => {
    const next = selectionAfterSelectAll(new Set([1, 2]), []);
    assert.equal(next.size, 0);
  });
});

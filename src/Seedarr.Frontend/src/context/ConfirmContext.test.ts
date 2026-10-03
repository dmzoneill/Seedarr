import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseConfirmOptions } from "./ConfirmContext";

describe("ConfirmContext: parseConfirmOptions", () => {
  it("wraps string into ConfirmOptions object", () => {
    const res = parseConfirmOptions("Are you sure?");
    assert.deepEqual(res, { message: "Are you sure?" });
  });

  it("passes ConfirmOptions object through untouched", () => {
    const opts = {
      title: "Delete Torrent",
      message: "Are you sure you want to delete this torrent?",
      danger: true,
      confirmText: "Delete",
      cancelText: "Keep",
    };
    const res = parseConfirmOptions(opts);
    assert.deepEqual(res, opts);
  });
});

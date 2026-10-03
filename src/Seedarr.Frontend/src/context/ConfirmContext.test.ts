import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { parseConfirmOptions, createConfirmQueue } from "./ConfirmContext";

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

describe("ConfirmContext: sequential queue handling", () => {
  it("handles sequential queue execution when multiple confirms are triggered", async () => {
    const queue = createConfirmQueue();

    let p1Resolved: boolean | undefined = undefined;
    let p2Resolved: boolean | undefined = undefined;
    let p3Resolved: boolean | undefined = undefined;

    const p1 = queue.confirm("First action").then((v) => {
      p1Resolved = v;
      return v;
    });

    assert.equal(queue.getState().isOpen, true);
    assert.deepEqual(queue.getState().options, { message: "First action" });
    assert.equal(queue.getQueueLength(), 0);

    const p2 = queue.confirm({ title: "Second", message: "Second action", danger: true }).then((v) => {
      p2Resolved = v;
      return v;
    });
    const p3 = queue.confirm("Third action").then((v) => {
      p3Resolved = v;
      return v;
    });

    // Still showing first confirm, subsequent items are queued
    assert.equal(queue.getState().isOpen, true);
    assert.deepEqual(queue.getState().options, { message: "First action" });
    assert.equal(queue.getQueueLength(), 2);
    assert.equal(p1Resolved, undefined);
    assert.equal(p2Resolved, undefined);
    assert.equal(p3Resolved, undefined);

    // Confirm first dialog
    queue.handleConfirm();
    const r1 = await p1;
    assert.equal(r1, true);
    assert.equal(p1Resolved, true);

    // Queue shifts: Second action becomes active
    assert.equal(queue.getState().isOpen, true);
    assert.equal(queue.getState().options.title, "Second");
    assert.equal(queue.getState().options.message, "Second action");
    assert.equal(queue.getState().options.danger, true);
    assert.equal(queue.getQueueLength(), 1);
    assert.equal(p2Resolved, undefined);

    // Cancel second dialog
    queue.handleCancel();
    const r2 = await p2;
    assert.equal(r2, false);
    assert.equal(p2Resolved, false);

    // Queue shifts: Third action becomes active
    assert.equal(queue.getState().isOpen, true);
    assert.deepEqual(queue.getState().options, { message: "Third action" });
    assert.equal(queue.getQueueLength(), 0);
    assert.equal(p3Resolved, undefined);

    // Confirm third dialog
    queue.handleConfirm();
    const r3 = await p3;
    assert.equal(r3, true);
    assert.equal(p3Resolved, true);

    // Queue is empty: modal closes
    assert.equal(queue.getState().isOpen, false);
    assert.equal(queue.getQueueLength(), 0);
  });

  it("notifies state change listener on each step of sequential processing", async () => {
    const states: { isOpen: boolean; message: unknown }[] = [];
    const queue = createConfirmQueue((s) => {
      states.push({ isOpen: s.isOpen, message: s.options.message });
    });

    const p1 = queue.confirm("A");
    const p2 = queue.confirm("B");

    assert.equal(states.length, 1);
    assert.deepEqual(states[0], { isOpen: true, message: "A" });

    queue.handleConfirm();
    await p1;

    assert.equal(states.length, 2);
    assert.deepEqual(states[1], { isOpen: true, message: "B" });

    queue.handleCancel();
    await p2;

    assert.equal(states.length, 3);
    assert.deepEqual(states[2], { isOpen: false, message: "" });
  });
});

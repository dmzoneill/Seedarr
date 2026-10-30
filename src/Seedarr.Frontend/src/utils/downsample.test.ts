import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { downsampleLTTB, SpeedRingBuffer } from "./downsample";

void describe("downsampleLTTB", () => {
  const points = Array.from({ length: 10 }, (_, i) => ({ x: i, y: i * i }));

  void it("returns empty array for empty input", () => {
    const empty: { x: number; y: number }[] = [];
    assert.deepEqual(
      downsampleLTTB(
        empty,
        5,
        (d) => d.x,
        (d) => d.y,
      ),
      [],
    );
  });

  void it("returns empty array for non-positive target", () => {
    assert.deepEqual(
      downsampleLTTB(
        points,
        0,
        (d) => d.x,
        (d) => d.y,
      ),
      [],
    );
  });

  void it("returns original data when target is larger than input", () => {
    assert.deepEqual(
      downsampleLTTB(
        points,
        20,
        (d) => d.x,
        (d) => d.y,
      ),
      points,
    );
  });

  void it("keeps endpoints for target of two", () => {
    const result = downsampleLTTB(
      points,
      2,
      (d) => d.x,
      (d) => d.y,
    );
    assert.equal(result.length, 2);
    assert.equal(result[0].x, 0);
    assert.equal(result[1].x, 9);
  });

  void it("downsamples while preserving first and last points", () => {
    const result = downsampleLTTB(
      points,
      4,
      (d) => d.x,
      (d) => d.y,
    );
    assert.equal(result.length, 4);
    assert.equal(result[0].x, 0);
    assert.equal(result[result.length - 1].x, 9);
  });
});

void describe("SpeedRingBuffer", () => {
  void it("stores and returns points in order", () => {
    const buffer = new SpeedRingBuffer(3);
    buffer.push(1, 10, 20);
    buffer.push(2, 30, 40);
    const points = buffer.getPoints();
    assert.equal(points.length, 2);
    assert.equal(points[0].uploadSpeed, 10);
    assert.equal(points[1].downloadSpeed, 40);
  });

  void it("overwrites oldest entries when capacity is exceeded", () => {
    const buffer = new SpeedRingBuffer(2);
    buffer.push(1, 1, 1);
    buffer.push(2, 2, 2);
    buffer.push(3, 3, 3);
    const points = buffer.getPoints();
    assert.equal(points.length, 2);
    assert.equal(points[0].timestamp, 2);
    assert.equal(points[1].timestamp, 3);
  });

  void it("limits returned points with maxCount", () => {
    const buffer = new SpeedRingBuffer(5);
    for (let i = 0; i < 4; i++) {
      buffer.push(i, i, i);
    }
    const points = buffer.getPoints(2);
    assert.equal(points.length, 2);
    assert.equal(points[0].timestamp, 2);
    assert.equal(points[1].timestamp, 3);
  });

  void it("clears stored samples", () => {
    const buffer = new SpeedRingBuffer(2);
    buffer.push(1, 1, 1);
    buffer.clear();
    assert.equal(buffer.size, 0);
    assert.deepEqual(buffer.getPoints(), []);
  });
});

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { secureRandom, generateRandomId } from "./random";

describe("random utilities", () => {
  it("secureRandom returns numbers within [0, 1)", () => {
    for (let i = 0; i < 100; i++) {
      const val = secureRandom();
      assert(val >= 0 && val < 1, `Expected ${val} to be in [0, 1)`);
    }
  });

  it("generateRandomId generates hex strings of length 16 by default", () => {
    const id = generateRandomId();
    assert.strictEqual(id.length, 16);
    assert(/^[0-9a-f]{16}$/.test(id));
  });

  it("generateRandomId respects prefix", () => {
    const id = generateRandomId("test-");
    assert(id.startsWith("test-"));
    assert.strictEqual(id.length, 21);
    assert(/^test-[0-9a-f]{16}$/.test(id));
  });
});

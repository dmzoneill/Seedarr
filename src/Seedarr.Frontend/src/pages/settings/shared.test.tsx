import { describe, it } from "node:test";
import assert from "node:assert/strict";
import React from "react";
(globalThis as any).React = React;
import { renderToStaticMarkup } from "react-dom/server";
import { NumberInput, clampNumber } from "./shared";

function captureInputProps(props: Parameters<typeof NumberInput>[0]) {
  let captured: any = null;
  const origCreateElement = React.createElement;
  try {
    (React as any).createElement = function (
      type: any,
      elemProps: any,
      ...children: any[]
    ) {
      if (type === "input" && elemProps?.type === "number") {
        captured = elemProps;
      }
      return origCreateElement.apply(this, [type, elemProps, ...children]);
    };
    renderToStaticMarkup(React.createElement(NumberInput, props));
  } finally {
    (React as any).createElement = origCreateElement;
  }
  return captured;
}

describe("clampNumber helper", () => {
  it("clamps values below min to min", () => {
    assert.equal(clampNumber(-5, 0, 100), 0);
    assert.equal(clampNumber(3, 10, 20), 10);
  });

  it("clamps values above max to max", () => {
    assert.equal(clampNumber(150, 0, 100), 100);
    assert.equal(clampNumber(25, 10, 20), 20);
  });

  it("leaves values within bounds untouched", () => {
    assert.equal(clampNumber(50, 0, 100), 50);
    assert.equal(clampNumber(0, 0, 100), 0);
    assert.equal(clampNumber(100, 0, 100), 100);
  });

  it("handles optional min or max bounds", () => {
    assert.equal(clampNumber(-10, 0, undefined), 0);
    assert.equal(clampNumber(500, undefined, 100), 100);
    assert.equal(clampNumber(42, undefined, undefined), 42);
  });
});

describe("NumberInput DOM rendering", () => {
  it("renders with label, initial value, hint, and suffix", () => {
    const html = renderToStaticMarkup(
      React.createElement(NumberInput, {
        label: "Max Upload Speed",
        value: 625,
        onChange: () => {},
        min: 0,
        suffix: "KB/s",
        hint: "Default 625 KB/s (5 Mbit/s) (0 for unlimited)",
      }),
    );

    assert.ok(html.includes("Max Upload Speed"));
    assert.ok(html.includes('value="625"'));
    assert.ok(html.includes('min="0"'));
    assert.ok(html.includes("form-input-suffix"));
    assert.ok(html.includes("KB/s"));
    assert.ok(html.includes("0 for unlimited"));
  });

  it("renders disabled state and step attribute", () => {
    const html = renderToStaticMarkup(
      React.createElement(NumberInput, {
        label: "Alt Upload Speed",
        value: 50,
        onChange: () => {},
        min: 0,
        step: 0.5,
        disabled: true,
      }),
    );

    assert.ok(html.includes("disabled"));
    assert.ok(html.includes('step="0.5"'));
  });
});

describe("NumberInput typing, backspace, and bounds behavior (Issue #205)", () => {
  it("does not snap to 0 immediately when clearing input with backspace while typing", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Max Upload Speed",
      value: 625,
      onChange: (v) => calls.push(v),
      min: 0,
    });

    // User hits backspace to clear the field: internal text becomes ""
    inputProps.onChange({ target: { value: "" } });
    assert.equal(
      calls.length,
      0,
      "Empty string should not trigger onChange immediately while typing",
    );

    // User types "8"
    inputProps.onChange({ target: { value: "8" } });
    assert.deepEqual(calls, [8], "Typing 8 should call onChange(8)");

    // User types "80"
    inputProps.onChange({ target: { value: "80" } });
    assert.deepEqual(
      calls,
      [8, 80],
      "Typing 80 should call onChange(80) without leading 0 snap",
    );

    // User types "8080"
    inputProps.onChange({ target: { value: "8080" } });
    assert.deepEqual(calls, [8, 80, 8080], "Typing 8080 should call onChange(8080)");
  });

  it("clamps empty input to min (or defaultValue) on blur", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Max Upload Speed",
      value: 625,
      onChange: (v) => calls.push(v),
      min: 0,
    });

    // User clears input
    inputProps.onChange({ target: { value: "" } });
    assert.equal(calls.length, 0);

    // User blurs
    inputProps.onBlur({} as any);
    assert.deepEqual(
      calls,
      [0],
      "Blur on empty input should clamp to min ?? 0 and call onChange",
    );
  });

  it("falls back to defaultValue on blur when provided and input is empty", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Timeout",
      value: 30,
      onChange: (v) => calls.push(v),
      min: 5,
      max: 120,
      defaultValue: 15,
    });

    inputProps.onChange({ target: { value: "" } });
    assert.equal(calls.length, 0);

    inputProps.onBlur({} as any);
    assert.deepEqual(
      calls,
      [15],
      "Blur on empty input should fall back to defaultValue",
    );
  });

  it("does not call onChange when typing out-of-bounds values, but clamps to bounds on blur", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Percentage",
      value: 50,
      onChange: (v) => calls.push(v),
      min: 10,
      max: 100,
    });

    // Type below min (5 < 10)
    inputProps.onChange({ target: { value: "5" } });
    assert.equal(
      calls.length,
      0,
      "Typing 5 (< min 10) should not call onChange while typing",
    );

    // Blur clamps 5 to 10
    inputProps.onBlur({} as any);
    assert.deepEqual(calls, [10], "Blur should clamp 5 to min=10");

    // Now type above max (200 > 100)
    inputProps.onChange({ target: { value: "200" } });
    assert.equal(
      calls.length,
      1,
      "Typing 200 (> max 100) should not call onChange while typing",
    );

    // Blur clamps 200 to 100
    inputProps.onBlur({} as any);
    assert.deepEqual(calls, [10, 100], "Blur should clamp 200 to max=100");
  });

  it("allows typing intermediate decimals without premature onChange, calling onChange on valid decimal", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Global Seed Ratio Limit",
      value: 0,
      onChange: (v) => calls.push(v),
      min: 0,
      step: 0.1,
    });

    // Type "1"
    inputProps.onChange({ target: { value: "1" } });
    assert.deepEqual(calls, [1]);

    // Type "1." (intermediate decimal)
    inputProps.onChange({ target: { value: "1." } });
    assert.deepEqual(
      calls,
      [1],
      "Typing '1.' should not trigger onChange while ending in decimal dot",
    );

    // Type "1.5"
    inputProps.onChange({ target: { value: "1.5" } });
    assert.deepEqual(calls, [1, 1.5], "Typing '1.5' should call onChange(1.5)");

    // Blur maintains 1.5
    inputProps.onBlur({} as any);
    assert.deepEqual(calls, [1, 1.5, 1.5], "Blur maintains 1.5");
  });

  it("clamps intermediate decimal on blur if blurred before adding fractional digits", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Ratio",
      value: 2,
      onChange: (v) => calls.push(v),
      min: 0,
      step: 0.1,
    });

    // User types "3." and blurs immediately
    inputProps.onChange({ target: { value: "3." } });
    assert.equal(calls.length, 0);

    inputProps.onBlur({} as any);
    assert.deepEqual(calls, [3], "Blur on '3.' should parse and clamp to 3");
  });

  it("handles typing intermediate '-' and clamps appropriately on blur", () => {
    const calls: number[] = [];
    const inputProps = captureInputProps({
      label: "Signed Value",
      value: 0,
      onChange: (v) => calls.push(v),
      min: -50,
      max: 50,
    });

    inputProps.onChange({ target: { value: "-" } });
    assert.equal(
      calls.length,
      0,
      "Typing '-' should not call onChange while typing",
    );

    inputProps.onBlur({} as any);
    assert.deepEqual(
      calls,
      [-50],
      "Blur on '-' should fallback and clamp to min (-50)",
    );
  });
});

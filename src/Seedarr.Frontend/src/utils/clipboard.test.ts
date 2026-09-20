import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { copyToClipboard } from "./clipboard";

interface MockClipboard {
  writeText: (text: string) => Promise<void>;
}

interface MockElement {
  tagName: string;
  value: string;
  style: Record<string, string>;
  setAttribute: (name: string, val: string) => void;
  select: () => void;
  setSelectionRange: (start: number, end: number) => void;
  parentNode: unknown;
}

interface MockDocState {
  appended: MockElement | null;
  removed: MockElement | null;
  commandExecuted: string;
  selected: boolean;
}

describe("clipboard: copyToClipboard", () => {
  let originalNavigator: unknown;
  let originalDocument: unknown;

  beforeEach(() => {
    const g = globalThis as Record<string, unknown>;
    originalNavigator = g.navigator;
    originalDocument = g.document;
  });

  afterEach(() => {
    const g = globalThis as Record<string, unknown>;
    g.navigator = originalNavigator;
    g.document = originalDocument;
  });

  it("should succeed via navigator.clipboard.writeText when supported", async () => {
    let copiedText = "";
    const g = globalThis as Record<string, unknown>;
    const mockClipboard: MockClipboard = {
      writeText: async (text: string) => {
        copiedText = text;
      },
    };
    g.navigator = { clipboard: mockClipboard };

    const success = await copyToClipboard("Hello World");
    assert.equal(success, true);
    assert.equal(copiedText, "Hello World");
  });

  it("should fall back to document.execCommand when navigator.clipboard is undefined", async () => {
    const g = globalThis as Record<string, unknown>;
    g.navigator = undefined;

    const state: MockDocState = {
      appended: null,
      removed: null,
      commandExecuted: "",
      selected: false,
    };

    const mockDoc = {
      createElement: (tag: string): MockElement => ({
        tagName: tag.toUpperCase(),
        value: "",
        style: {},
        setAttribute: () => {},
        select: () => {
          state.selected = true;
        },
        setSelectionRange: () => {},
        parentNode: null,
      }),
      body: {
        appendChild: (child: MockElement) => {
          state.appended = child;
          child.parentNode = mockDoc.body;
        },
        removeChild: (child: MockElement) => {
          state.removed = child;
          child.parentNode = null;
        },
      },
      execCommand: (cmd: string) => {
        state.commandExecuted = cmd;
        return true;
      },
    };
    g.document = mockDoc;

    const success = await copyToClipboard("fallback text");
    assert.equal(success, true);
    assert.equal(state.commandExecuted, "copy");
    assert.equal(state.selected, true);
    assert.ok(state.appended);
    assert.equal(state.appended.value, "fallback text");
    assert.equal(state.removed, state.appended);
  });

  it("should fall back to textarea when navigator.clipboard.writeText throws", async () => {
    const g = globalThis as Record<string, unknown>;
    const failingClipboard: MockClipboard = {
      writeText: async () => {
        throw new Error("Permission denied");
      },
    };
    g.navigator = { clipboard: failingClipboard };

    let commandExecuted = "";
    const mockDoc = {
      createElement: (tag: string): MockElement => ({
        tagName: tag.toUpperCase(),
        value: "",
        style: {},
        setAttribute: () => {},
        select: () => {},
        setSelectionRange: () => {},
        parentNode: null,
      }),
      body: {
        appendChild: (child: MockElement) => {
          child.parentNode = mockDoc.body;
        },
        removeChild: (child: MockElement) => {
          child.parentNode = null;
        },
      },
      execCommand: (cmd: string) => {
        commandExecuted = cmd;
        return true;
      },
    };
    g.document = mockDoc;

    const success = await copyToClipboard("throw recovery");
    assert.equal(success, true);
    assert.equal(commandExecuted, "copy");
  });

  it("should return false if neither clipboard API nor document is available", async () => {
    const g = globalThis as Record<string, unknown>;
    g.navigator = undefined;
    g.document = undefined;

    const success = await copyToClipboard("no env");
    assert.equal(success, false);
  });
});

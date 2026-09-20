import { describe, it, beforeEach } from "node:test";
import assert from "node:assert/strict";
import {
  ALL_COLUMNS,
  COLUMN_CATEGORIES,
  COLUMN_I18N_KEYS,
  DEFAULT_VISIBLE,
  COMPACT_VISIBLE,
  STORAGE_KEY,
  loadVisibleColumns,
  saveVisibleColumns,
  type ColumnKey,
  type ColumnCategory,
} from "./columnPreferences";

// Mock localStorage for Node test runner environment
const mockStorage: Record<string, string> = {};
global.localStorage = {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, value: string) => {
    mockStorage[key] = value;
  },
  removeItem: (key: string) => {
    delete mockStorage[key];
  },
  clear: () => {
    for (const k in mockStorage) delete mockStorage[k];
  },
  length: 0,
  key: () => null,
} as unknown as Storage;

describe("ColumnCustomizer: Column Definitions & Configuration", () => {
  it("should contain exactly 39 column definitions", () => {
    assert.strictEqual(ALL_COLUMNS.length, 39);
  });

  it("should ensure all column keys are unique", () => {
    const keys = new Set(ALL_COLUMNS.map((c) => c.key));
    assert.strictEqual(keys.size, ALL_COLUMNS.length);
  });

  it("should assign every column to a recognized category", () => {
    const validCategories = new Set<ColumnCategory>([
      "basic",
      "transfer",
      "swarm",
      "activity",
      "metadata",
    ]);

    for (const col of ALL_COLUMNS) {
      assert.ok(
        validCategories.has(col.category),
        `Column ${col.key} has invalid category: ${col.category}`,
      );
    }
  });

  it("should have all 5 categories populated", () => {
    for (const cat of COLUMN_CATEGORIES) {
      const count = ALL_COLUMNS.filter((c) => c.category === cat.id).length;
      assert.ok(count > 0, `Category ${cat.id} has no columns`);
    }
  });

  it("should provide an i18n translation key for every column", () => {
    for (const col of ALL_COLUMNS) {
      const key = col.key as ColumnKey;
      assert.ok(
        COLUMN_I18N_KEYS[key],
        `Column ${col.key} is missing an i18n key in COLUMN_I18N_KEYS`,
      );
    }
  });

  it("should define default visible columns as a subset of ALL_COLUMNS", () => {
    const allKeys = new Set(ALL_COLUMNS.map((c) => c.key));
    assert.strictEqual(DEFAULT_VISIBLE.size, 11);
    for (const key of DEFAULT_VISIBLE) {
      assert.ok(allKeys.has(key as ColumnKey), `Default column ${key} not in ALL_COLUMNS`);
    }
  });

  it("should define compact visible columns as a subset of ALL_COLUMNS", () => {
    const allKeys = new Set(ALL_COLUMNS.map((c) => c.key));
    assert.strictEqual(COMPACT_VISIBLE.size, 7);
    for (const key of COMPACT_VISIBLE) {
      assert.ok(allKeys.has(key as ColumnKey), `Compact column ${key} not in ALL_COLUMNS`);
    }
  });
});

describe("ColumnCustomizer: Storage Persistence", () => {
  beforeEach(() => {
    global.localStorage.clear();
  });

  it("should load defaults when localStorage is empty", () => {
    const cols = loadVisibleColumns();
    assert.strictEqual(cols.size, DEFAULT_VISIBLE.size);
    for (const key of DEFAULT_VISIBLE) {
      assert.ok(cols.has(key));
    }
  });

  it("should save and load custom column sets from localStorage", () => {
    const custom = new Set(["name", "totalSize", "uploadSpeed"]);
    saveVisibleColumns(custom);

    const loaded = loadVisibleColumns();
    assert.strictEqual(loaded.size, 3);
    assert.ok(loaded.has("name"));
    assert.ok(loaded.has("totalSize"));
    assert.ok(loaded.has("uploadSpeed"));
  });

  it("should ignore invalid or non-existent column keys in localStorage", () => {
    mockStorage[STORAGE_KEY] = JSON.stringify([
      "name",
      "non_existent_column_123",
      "invalid_key",
    ]);

    const loaded = loadVisibleColumns();
    assert.strictEqual(loaded.size, 1);
    assert.ok(loaded.has("name"));
    assert.ok(!loaded.has("non_existent_column_123"));
  });

  it("should fall back to default when localStorage contains invalid JSON", () => {
    mockStorage[STORAGE_KEY] = "{not valid json]";
    const loaded = loadVisibleColumns();
    assert.strictEqual(loaded.size, DEFAULT_VISIBLE.size);
  });
});

describe("ColumnCustomizer: Toggle & Preset Logic", () => {
  it("should correctly toggle an unchecked column to checked", () => {
    const current = new Set(["name", "status"]);
    const keyToToggle = "uploadSpeed";

    // Simulate toggle
    const next = new Set(current);
    if (next.has(keyToToggle)) {
      if (next.size > 1) next.delete(keyToToggle);
    } else {
      next.add(keyToToggle);
    }

    assert.ok(next.has("uploadSpeed"));
    assert.strictEqual(next.size, 3);
  });

  it("should correctly toggle a checked column to unchecked", () => {
    const current = new Set(["name", "status", "uploadSpeed"]);
    const keyToToggle = "uploadSpeed";

    const next = new Set(current);
    if (next.has(keyToToggle)) {
      if (next.size > 1) next.delete(keyToToggle);
    } else {
      next.add(keyToToggle);
    }

    assert.ok(!next.has("uploadSpeed"));
    assert.strictEqual(next.size, 2);
  });

  it("should prevent deselecting the last remaining column", () => {
    const current = new Set(["name"]);
    const keyToToggle = "name";

    const next = new Set(current);
    if (next.has(keyToToggle)) {
      if (next.size > 1) next.delete(keyToToggle);
    } else {
      next.add(keyToToggle);
    }

    // Must still contain name
    assert.ok(next.has("name"));
    assert.strictEqual(next.size, 1);
  });

  it("should toggle an entire category of columns", () => {
    const current = new Set(["#", "name"]);
    const transferCols = ALL_COLUMNS.filter((c) => c.category === "transfer").map((c) => c.key);

    // Some or none selected -> toggle should enable all
    const next = new Set(current);
    const allEnabled = transferCols.every((k) => next.has(k));
    assert.strictEqual(allEnabled, false);

    transferCols.forEach((k) => next.add(k));
    assert.ok(transferCols.every((k) => next.has(k)));

    // Now all enabled -> toggle should disable transfer columns
    transferCols.forEach((k) => {
      if (next.size > 1) next.delete(k);
    });
    assert.ok(!transferCols.some((k) => next.has(k)));
    assert.ok(next.has("name"));
  });

  it("should apply 'compact' preset", () => {
    const preset = new Set(COMPACT_VISIBLE);
    assert.strictEqual(preset.size, 7);
    assert.ok(preset.has("name"));
    assert.ok(preset.has("status"));
    assert.ok(preset.has("progress"));
    assert.ok(!preset.has("trackerUrl"));
  });

  it("should apply 'all' preset", () => {
    const preset = new Set(ALL_COLUMNS.map((c) => c.key));
    assert.strictEqual(preset.size, 39);
  });
});

describe("ColumnCustomizer: Search Filtering", () => {
  function searchColumns(query: string) {
    const q = query.trim().toLowerCase();
    if (!q) return ALL_COLUMNS;
    return ALL_COLUMNS.filter(
      (col) =>
        col.label.toLowerCase().includes(q) ||
        col.key.toLowerCase().includes(q),
    );
  }

  it("should return all columns when search query is empty", () => {
    const results = searchColumns("");
    assert.strictEqual(results.length, 39);
  });

  it("should filter columns by partial label match", () => {
    const results = searchColumns("speed");
    const keys = results.map((r) => r.key);
    assert.ok(keys.includes("uploadSpeed"));
    assert.ok(keys.includes("downloadSpeed"));
    assert.strictEqual(results.length, 2);
  });

  it("should filter columns by key match", () => {
    const results = searchColumns("queue");
    assert.strictEqual(results.length, 1);
    assert.strictEqual(results[0].key, "queuePosition");
  });

  it("should be case-insensitive", () => {
    const lower = searchColumns("tracker");
    const upper = searchColumns("TRACKER");
    assert.strictEqual(lower.length, upper.length);
    assert.ok(lower.length >= 1);
  });

  it("should return empty array for non-matching search", () => {
    const results = searchColumns("nonexistent_column_search_term");
    assert.strictEqual(results.length, 0);
  });
});

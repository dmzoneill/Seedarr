import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";

type StorageMap = Record<string, string>;

type AnalyticsModule = typeof import("./analytics");

function installBrowserStubs(storage: StorageMap) {
  const gtagCalls: unknown[][] = [];
  const win = globalThis as typeof globalThis & {
    localStorage: {
      getItem: (k: string) => string | null;
      setItem: (k: string, v: string) => void;
      clear: () => void;
    };
    window: typeof globalThis & { gtag?: (...args: unknown[]) => void };
    document: { title: string };
  };

  win.localStorage = {
    getItem: (key) => (key in storage ? storage[key] : null),
    setItem: (key, value) => {
      storage[key] = value;
    },
    clear: () => {
      for (const key of Object.keys(storage)) {
        delete storage[key];
      }
    },
  };

  win.document = { title: "Seedarr" };
  win.window = win as typeof globalThis & { gtag?: (...args: unknown[]) => void };
  win.window.gtag = (...args: unknown[]) => {
    gtagCalls.push(args);
  };

  return { gtagCalls };
}

async function loadAnalytics(): Promise<AnalyticsModule> {
  return import("./analytics");
}

describe("analytics", () => {
  let storage: StorageMap;
  let gtagCalls: unknown[][];

  beforeEach(() => {
    storage = {};
    const stubs = installBrowserStubs(storage);
    gtagCalls = stubs.gtagCalls;
  });

  afterEach(() => {
    gtagCalls.length = 0;
  });

  it("tracks telemetry preference", async () => {
    const analytics = await loadAnalytics();
    assert.equal(analytics.isTelemetryEnabled(), true);
    analytics.setTelemetryEnabled(false);
    assert.equal(storage[analytics.STORAGE_KEY_TELEMETRY_ENABLED], "false");
    assert.equal(analytics.isTelemetryEnabled(), false);
  });

  it("sanitizes error messages", async () => {
    const analytics = await loadAnalytics();
    const msg =
      "failed https://secret.example/path/to/file token=abc123 password=secret " +
      "hash abcdef0123456789abcdef0123456789";
    const sanitized = analytics.sanitizeErrorMessage(msg);
    assert.match(sanitized, /\[URL\]/);
    assert.match(sanitized, /\[HASH\]/);
    assert.match(sanitized, /\[REDACTED\]/i);
    assert.ok(sanitized.length <= 150);
    assert.equal(analytics.sanitizeErrorMessage(""), "Unknown error");
  });

  it("sends events when telemetry enabled", async () => {
    const analytics = await loadAnalytics();
    analytics.trackPageView("/torrents?apikey=secret&tab=files", "Title");
    analytics.trackTorrentAdd("magnet", 2, "Movies", { sequential: true });
    analytics.trackTorrentAction("pause", 9);
    analytics.trackIndexerSearch("  title  ", 3, "tv");
    analytics.trackReleaseGrab("secret-name.mkv", "Indexer");
    analytics.trackThemeChange("dark");
    analytics.trackLanguageChange("en");
    analytics.trackModalOpen("add-torrent");
    analytics.trackTrackerBoostAction("boost_all", 4);
    analytics.trackEngineSwitch("libtorrent");
    analytics.trackBulkAction("pause", 10);
    analytics.trackSettingSave("network", { port: 6881 });
    analytics.trackDownloadClientAction("qbittorrent", "test", true);
    analytics.trackQuickSettingChange("max_connections", 200);
    analytics.trackViewModeChange("grid");
    analytics.trackFilterChange("category", "movies");
    analytics.trackException("Error: boom https://x.test", false, "unit");

    assert.ok(gtagCalls.length >= 10);
    const pageView = gtagCalls.find((c) => c[1] === "page_view");
    assert.ok(pageView);
    const params = pageView![2] as { page_path?: string };
    assert.equal(params.page_path, "/torrents?tab=files");
  });

  it("no-ops when telemetry disabled", async () => {
    const analytics = await loadAnalytics();
    analytics.setTelemetryEnabled(false);
    analytics.trackEvent("test_event", { foo: "bar" });
    analytics.trackPageView("/torrents?apikey=secret");
    assert.equal(gtagCalls.length, 0);
  });
});

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  isHandledByNamedEvent,
  EVENT_INVALIDATION_MAP,
  RECONNECT_QUERY_KEYS,
} from "./SignalRProvider";

describe("SignalRProvider: isHandledByNamedEvent", () => {
  it("returns true for messages with dedicated named event handlers to prevent duplicate invalidation", () => {
    // Torrent events
    assert.equal(isHandledByNamedEvent("Torrent"), true);
    assert.equal(isHandledByNamedEvent("Torrents"), true);
    assert.equal(isHandledByNamedEvent("TorrentAdded"), true);
    assert.equal(isHandledByNamedEvent("TorrentUpdated"), true);
    assert.equal(isHandledByNamedEvent("TorrentDeleted"), true);

    // Seeding events
    assert.equal(isHandledByNamedEvent("Seeding"), true);
    assert.equal(isHandledByNamedEvent("SeedingStatsUpdated"), true);

    // Health events
    assert.equal(isHandledByNamedEvent("Health"), true);
    assert.equal(isHandledByNamedEvent("HealthCheckCompleted"), true);

    // Command and System events
    assert.equal(isHandledByNamedEvent("Command"), true);
    assert.equal(isHandledByNamedEvent("CommandStarted"), true);
    assert.equal(isHandledByNamedEvent("CommandCompleted"), true);
    assert.equal(isHandledByNamedEvent("System"), true);

    // Task events
    assert.equal(isHandledByNamedEvent("Task"), true);
    assert.equal(isHandledByNamedEvent("TaskStarted"), true);
    assert.equal(isHandledByNamedEvent("TaskCompleted"), true);

    // Automation events
    assert.equal(isHandledByNamedEvent("Automation"), true);
    assert.equal(isHandledByNamedEvent("AutomationExecuted"), true);
    assert.equal(isHandledByNamedEvent("AutomationTriggerEvaluated"), true);

    // Tracker events
    assert.equal(isHandledByNamedEvent("TrackerUpdated"), true);
    assert.equal(isHandledByNamedEvent("TrackerAnnounced"), true);
    assert.equal(isHandledByNamedEvent("trackerUpdated"), true);
    assert.equal(isHandledByNamedEvent("trackerAnnounced"), true);
  });

  it("returns false for generic messages that are NOT dispatched via named events", () => {
    assert.equal(isHandledByNamedEvent("Tracker"), false);
    assert.equal(isHandledByNamedEvent("Category"), false);
    assert.equal(isHandledByNamedEvent("Tag"), false);
    assert.equal(isHandledByNamedEvent(undefined), false);
    assert.equal(isHandledByNamedEvent(""), false);
  });
});

describe("SignalRProvider: configuration maps", () => {
  it("EVENT_INVALIDATION_MAP includes invalidations for all primary domain events", () => {
    assert.ok(EVENT_INVALIDATION_MAP.TorrentAdded);
    assert.ok(EVENT_INVALIDATION_MAP.TorrentUpdated);
    assert.ok(EVENT_INVALIDATION_MAP.TorrentDeleted);
    assert.ok(EVENT_INVALIDATION_MAP.SeedingStatsUpdated);
    assert.ok(EVENT_INVALIDATION_MAP.HealthCheckCompleted);
    assert.ok(EVENT_INVALIDATION_MAP.TrackerUpdated);
    assert.ok(EVENT_INVALIDATION_MAP.TrackerAnnounced);

    // Ensure torrent events invalidate torrents and trackerboost
    const torrentUpdatedKeys = EVENT_INVALIDATION_MAP.TorrentUpdated.map((k) =>
      k.join("/"),
    );
    assert.ok(torrentUpdatedKeys.includes("torrents"));
    assert.ok(torrentUpdatedKeys.includes("trackerboost"));

    // Ensure 1Hz seeding tick events ONLY invalidate seeding stats and NOT torrents
    const seedingKeys = EVENT_INVALIDATION_MAP.SeedingStatsUpdated.map((k) =>
      k.join("/"),
    );
    assert.ok(seedingKeys.includes("seeding/stats"));
    assert.equal(seedingKeys.includes("torrents"), false);
  });

  it("RECONNECT_QUERY_KEYS includes essential caches for reconnect resynchronization", () => {
    const keyStrings = RECONNECT_QUERY_KEYS.map((k) => k.join("/"));
    assert.ok(keyStrings.includes("torrents"));
    assert.ok(keyStrings.includes("seeding/stats"));
    assert.ok(keyStrings.includes("health"));
    assert.ok(keyStrings.includes("trackerboost"));
    assert.ok(keyStrings.includes("categories"));
    assert.ok(keyStrings.includes("tags"));
  });
});

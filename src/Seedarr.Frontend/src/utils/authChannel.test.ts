import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import type { CurrentUser } from "../api/types";
import {
  broadcastLogin,
  broadcastLogout,
  broadcastSessionExpired,
  subscribeAuthChannel,
  closeAuthChannel,
  AUTH_CHANNEL_NAME,
  AUTH_STORAGE_KEY,
  type AuthEvent,
} from "./authChannel";

// Mock BroadcastChannel implementation for Node test environment
class MockBroadcastChannel {
  name: string;
  onmessage: ((event: MessageEvent<AuthEvent>) => void) | null = null;
  onmessageerror: ((event: MessageEvent) => void) | null = null;
  static instances: MockBroadcastChannel[] = [];

  constructor(name: string) {
    this.name = name;
    MockBroadcastChannel.instances.push(this);
  }

  postMessage(data: AuthEvent) {
    // Deliver message asynchronously to all other instances on the same channel
    const peers = MockBroadcastChannel.instances.filter(
      (inst) => inst !== this && inst.name === this.name,
    );
    for (const peer of peers) {
      if (peer.onmessage) {
        peer.onmessage(new MessageEvent("message", { data }));
      }
    }
  }

  close() {
    const idx = MockBroadcastChannel.instances.indexOf(this);
    if (idx !== -1) {
      MockBroadcastChannel.instances.splice(idx, 1);
    }
  }
}

// Mock localStorage implementation
class MockLocalStorage {
  private store = new Map<string, string>();

  getItem(key: string): string | null {
    return this.store.get(key) ?? null;
  }

  setItem(key: string, value: string): void {
    const oldValue = this.store.get(key) ?? null;
    this.store.set(key, value);

    // Trigger storage event on window if registered
    if (typeof (globalThis as any).window !== "undefined") {
      const storageEvent = {
        key,
        newValue: value,
        oldValue,
      } as StorageEvent;

      const listeners = (globalThis as any).__storageListeners || [];
      for (const listener of listeners) {
        listener(storageEvent);
      }
    }
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  clear(): void {
    this.store.clear();
  }
}

describe("authChannel: Cross-tab authentication synchronization", () => {
  let originalWindow: any;
  let originalBroadcastChannel: any;
  let mockStorage: MockLocalStorage;
  let storageListeners: Array<(e: StorageEvent) => void>;

  beforeEach(() => {
    originalWindow = (globalThis as any).window;
    originalBroadcastChannel = (globalThis as any).BroadcastChannel;
    MockBroadcastChannel.instances = [];
    mockStorage = new MockLocalStorage();
    storageListeners = [];

    (globalThis as any).__storageListeners = storageListeners;
    (globalThis as any).window = {
      BroadcastChannel: MockBroadcastChannel,
      localStorage: mockStorage,
      addEventListener: (type: string, listener: any) => {
        if (type === "storage") {
          storageListeners.push(listener);
        }
      },
      removeEventListener: (type: string, listener: any) => {
        if (type === "storage") {
          const idx = storageListeners.indexOf(listener);
          if (idx !== -1) storageListeners.splice(idx, 1);
        }
      },
    };
    (globalThis as any).BroadcastChannel = MockBroadcastChannel;
  });

  afterEach(() => {
    closeAuthChannel();
    (globalThis as any).window = originalWindow;
    (globalThis as any).BroadcastChannel = originalBroadcastChannel;
    delete (globalThis as any).__storageListeners;
  });

  it("broadcasts and receives AUTH_LOGOUT via BroadcastChannel", () => {
    const received: AuthEvent[] = [];

    // Simulate Tab B subscribing
    const unsubscribe = subscribeAuthChannel((event) => {
      received.push(event);
    });

    // Create a peer channel simulating Tab A broadcasting
    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_LOGOUT",
      timestamp: Date.now(),
    });

    assert.equal(received.length, 1);
    assert.equal(received[0].type, "AUTH_LOGOUT");
    assert.ok(typeof received[0].timestamp === "number");

    unsubscribe();
  });

  it("broadcasts and receives AUTH_LOGIN with user payload", () => {
    const received: AuthEvent[] = [];

    const unsubscribe = subscribeAuthChannel((event) => {
      received.push(event);
    });

    const mockUser: CurrentUser = {
      id: 1,
      username: "alice",
      displayName: "Alice Admin",
      roles: ["Admin"],
      isAuthenticated: true,
    };

    // Tab A broadcasting
    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_LOGIN",
      user: mockUser,
      timestamp: Date.now(),
    });

    assert.equal(received.length, 1);
    assert.equal(received[0].type, "AUTH_LOGIN");
    if (received[0].type === "AUTH_LOGIN") {
      assert.deepEqual(received[0].user, mockUser);
    }

    unsubscribe();
  });

  it("broadcasts and receives AUTH_SESSION_EXPIRED", () => {
    const received: AuthEvent[] = [];

    const unsubscribe = subscribeAuthChannel((event) => {
      received.push(event);
    });

    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_SESSION_EXPIRED",
      timestamp: Date.now(),
    });

    assert.equal(received.length, 1);
    assert.equal(received[0].type, "AUTH_SESSION_EXPIRED");

    unsubscribe();
  });

  it("broadcastLogin, broadcastLogout, and broadcastSessionExpired post to BroadcastChannel", () => {
    let lastPosted: AuthEvent | null = null;

    // Simulate a receiver tab channel
    const receiverChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    receiverChannel.onmessage = (e) => {
      lastPosted = e.data;
    };

    broadcastLogout();
    assert.ok(lastPosted);
    assert.equal((lastPosted as AuthEvent).type, "AUTH_LOGOUT");

    broadcastSessionExpired();
    assert.equal((lastPosted as AuthEvent).type, "AUTH_SESSION_EXPIRED");

    const mockUser: CurrentUser = {
      username: "bob",
      roles: ["User"],
      isAuthenticated: true,
    };
    broadcastLogin(mockUser);
    assert.equal((lastPosted as AuthEvent).type, "AUTH_LOGIN");
    if ((lastPosted as AuthEvent).type === "AUTH_LOGIN") {
      assert.deepEqual((lastPosted as any).user, mockUser);
    }
  });

  it("unsubscribing removes the listener so further events are not received", () => {
    const received: AuthEvent[] = [];

    const unsubscribe = subscribeAuthChannel((event) => {
      received.push(event);
    });

    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_LOGOUT",
      timestamp: Date.now(),
    });
    assert.equal(received.length, 1);

    // Unsubscribe Tab B
    unsubscribe();

    tabAChannel.postMessage({
      type: "AUTH_SESSION_EXPIRED",
      timestamp: Date.now(),
    });
    // Count remains 1
    assert.equal(received.length, 1);
  });

  it("supports multiple concurrent subscribers", () => {
    const received1: AuthEvent[] = [];
    const received2: AuthEvent[] = [];

    const unsub1 = subscribeAuthChannel((e) => received1.push(e));
    const unsub2 = subscribeAuthChannel((e) => received2.push(e));

    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_LOGOUT",
      timestamp: Date.now(),
    });

    assert.equal(received1.length, 1);
    assert.equal(received2.length, 1);

    unsub1();

    tabAChannel.postMessage({
      type: "AUTH_SESSION_EXPIRED",
      timestamp: Date.now(),
    });

    assert.equal(received1.length, 1); // unsubscribed
    assert.equal(received2.length, 2); // still subscribed

    unsub2();
  });

  it("falls back to localStorage and storage event when BroadcastChannel is unsupported", () => {
    // Disable BroadcastChannel to test fallback
    (globalThis as any).window.BroadcastChannel = undefined;
    (globalThis as any).BroadcastChannel = undefined;
    closeAuthChannel();

    const received: AuthEvent[] = [];
    const unsubscribe = subscribeAuthChannel((e) => received.push(e));

    // Broadcasting should write to localStorage
    broadcastLogout();

    const storedValue = mockStorage.getItem(AUTH_STORAGE_KEY);
    assert.ok(storedValue);
    const parsed = JSON.parse(storedValue);
    assert.equal(parsed.type, "AUTH_LOGOUT");

    // The mockStorage.setItem triggers storage event, which the subscriber receives
    assert.equal(received.length, 1);
    assert.equal(received[0].type, "AUTH_LOGOUT");

    // Test AUTH_LOGIN via localStorage fallback
    const user: CurrentUser = {
      username: "charlie",
      roles: [],
      isAuthenticated: true,
    };
    broadcastLogin(user);
    assert.equal(received.length, 2);
    assert.equal(received[1].type, "AUTH_LOGIN");

    unsubscribe();
  });

  it("falls back to localStorage if BroadcastChannel.postMessage throws", () => {
    // Make BroadcastChannel.postMessage throw
    class FaultyBroadcastChannel extends MockBroadcastChannel {
      override postMessage() {
        throw new Error("Channel failed");
      }
    }

    (globalThis as any).window.BroadcastChannel = FaultyBroadcastChannel;
    (globalThis as any).BroadcastChannel = FaultyBroadcastChannel;
    closeAuthChannel();

    const received: AuthEvent[] = [];
    const unsubscribe = subscribeAuthChannel((e) => received.push(e));

    // Should fall back to localStorage without throwing
    assert.doesNotThrow(() => {
      broadcastSessionExpired();
    });

    const stored = mockStorage.getItem(AUTH_STORAGE_KEY);
    assert.ok(stored);
    assert.equal(JSON.parse(stored).type, "AUTH_SESSION_EXPIRED");
    assert.equal(received.length, 1);
    assert.equal(received[0].type, "AUTH_SESSION_EXPIRED");

    unsubscribe();
  });

  it("ignores storage events for unrelated keys or invalid JSON", () => {
    (globalThis as any).window.BroadcastChannel = undefined;
    (globalThis as any).BroadcastChannel = undefined;
    closeAuthChannel();

    const received: AuthEvent[] = [];
    const unsubscribe = subscribeAuthChannel((e) => received.push(e));

    // Storage event for other key
    mockStorage.setItem("other_key", "some_data");
    assert.equal(received.length, 0);

    // Storage event with invalid JSON
    mockStorage.setItem(AUTH_STORAGE_KEY, "invalid-json-string{");
    assert.equal(received.length, 0);

    unsubscribe();
  });

  it("closeAuthChannel closes channel and detaches all listeners", () => {
    const received: AuthEvent[] = [];
    subscribeAuthChannel((e) => received.push(e));

    closeAuthChannel();

    // After close, no events should be received
    const tabAChannel = new MockBroadcastChannel(AUTH_CHANNEL_NAME);
    tabAChannel.postMessage({
      type: "AUTH_LOGOUT",
      timestamp: Date.now(),
    });

    assert.equal(received.length, 0);
  });
});

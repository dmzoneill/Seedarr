import type { CurrentUser } from "../api/types";

export type AuthEventType =
  "AUTH_LOGIN" | "AUTH_LOGOUT" | "AUTH_SESSION_EXPIRED";

export interface AuthLoginEvent {
  type: "AUTH_LOGIN";
  user?: CurrentUser | null;
  timestamp: number;
}

export interface AuthLogoutEvent {
  type: "AUTH_LOGOUT";
  timestamp: number;
}

export interface AuthSessionExpiredEvent {
  type: "AUTH_SESSION_EXPIRED";
  timestamp: number;
}

export type AuthEvent =
  AuthLoginEvent | AuthLogoutEvent | AuthSessionExpiredEvent;

export type AuthChannelListener = (event: AuthEvent) => void;

export const AUTH_CHANNEL_NAME = "seedarr_auth";
export const AUTH_STORAGE_KEY = "seedarr_auth_event";

const listeners = new Set<AuthChannelListener>();

let channel: BroadcastChannel | null = null;
let storageListenerAttached = false;

function getBroadcastChannelClass():
  (new (name: string) => BroadcastChannel) | null {
  if (
    typeof window !== "undefined" &&
    typeof (window as any).BroadcastChannel === "function"
  ) {
    return (window as any).BroadcastChannel;
  }
  if (typeof BroadcastChannel === "function") {
    return BroadcastChannel;
  }
  return null;
}

function getStorage(): Storage | null {
  if (typeof window !== "undefined" && window.localStorage) {
    return window.localStorage;
  }
  if (typeof localStorage !== "undefined") {
    return localStorage;
  }
  return null;
}

function notifyListeners(event: AuthEvent): void {
  listeners.forEach((listener) => {
    try {
      listener(event);
    } catch (err) {
      console.error("[AuthChannel] Error in auth event listener:", err);
    }
  });
}

function handleBroadcastMessage(event: MessageEvent<AuthEvent>): void {
  if (event && event.data && typeof event.data.type === "string") {
    notifyListeners(event.data);
  }
}

function handleStorageEvent(e: StorageEvent): void {
  if (e.key === AUTH_STORAGE_KEY && e.newValue) {
    try {
      const parsed: AuthEvent = JSON.parse(e.newValue);
      if (parsed && typeof parsed.type === "string") {
        notifyListeners(parsed);
      }
    } catch {
      // ignore parse errors
    }
  }
}

function getBroadcastChannel(): BroadcastChannel | null {
  const BC = getBroadcastChannelClass();
  if (!channel && BC) {
    try {
      channel = new BC(AUTH_CHANNEL_NAME);
      channel.onmessage = handleBroadcastMessage;
      channel.onmessageerror = (err) => {
        console.warn("[AuthChannel] BroadcastChannel message error:", err);
      };
    } catch {
      channel = null;
    }
  }
  return channel;
}

function ensureStorageListener(): void {
  if (
    !storageListenerAttached &&
    typeof window !== "undefined" &&
    typeof window.addEventListener === "function"
  ) {
    window.addEventListener("storage", handleStorageEvent);
    storageListenerAttached = true;
  }
}

/**
 * Broadcast an authentication event to other open tabs using BroadcastChannel,
 * with a fallback to localStorage storage events when BroadcastChannel is unsupported.
 */
export function broadcastAuthEvent(event: AuthEvent): void {
  const bc = getBroadcastChannel();
  if (bc) {
    try {
      bc.postMessage(event);
      return;
    } catch (err) {
      console.warn(
        "[AuthChannel] Failed to postMessage via BroadcastChannel, falling back to localStorage:",
        err,
      );
    }
  }

  const storage = getStorage();
  if (storage) {
    try {
      storage.setItem(
        AUTH_STORAGE_KEY,
        JSON.stringify({ ...event, _nonce: Math.random() }),
      );
    } catch {
      // localStorage may fail in private mode or quota exceeded
    }
  }
}

/**
 * Broadcasts a login event containing user details across tabs.
 */
export function broadcastLogin(user?: CurrentUser | null): void {
  broadcastAuthEvent({
    type: "AUTH_LOGIN",
    user: user ?? undefined,
    timestamp: Date.now(),
  });
}

/**
 * Broadcasts a logout event across tabs.
 */
export function broadcastLogout(): void {
  broadcastAuthEvent({
    type: "AUTH_LOGOUT",
    timestamp: Date.now(),
  });
}

/**
 * Broadcasts a session expiration (HTTP 401) event across tabs.
 */
export function broadcastSessionExpired(): void {
  broadcastAuthEvent({
    type: "AUTH_SESSION_EXPIRED",
    timestamp: Date.now(),
  });
}

/**
 * Subscribes a listener to cross-tab auth state changes.
 * Returns an unsubscribe cleanup function.
 */
export function subscribeAuthChannel(
  listener: AuthChannelListener,
): () => void {
  listeners.add(listener);

  if (getBroadcastChannelClass()) {
    getBroadcastChannel();
  }
  if (getStorage()) {
    ensureStorageListener();
  }

  return () => {
    listeners.delete(listener);
  };
}

/**
 * Closes the active BroadcastChannel and clears all subscribers.
 */
export function closeAuthChannel(): void {
  listeners.clear();
  if (channel) {
    try {
      channel.close();
    } catch {
      // ignore
    }
    channel = null;
  }
  if (
    storageListenerAttached &&
    typeof window !== "undefined" &&
    typeof window.removeEventListener === "function"
  ) {
    window.removeEventListener("storage", handleStorageEvent);
    storageListenerAttached = false;
  }
}

/**
 * Test isolation helper.
 */
export const _resetAuthChannelForTesting = closeAuthChannel;

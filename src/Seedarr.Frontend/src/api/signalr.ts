import { useState, useEffect, useRef } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
  type IRetryPolicy,
  type RetryContext,
} from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useAppStore } from "../store/app";

export type ConnectionStatus = "connected" | "disconnected" | "reconnecting";

/**
 * Checks if the given error indicates an authentication / authorization failure (HTTP 401 or 403).
 */
export function isUnauthorizedOrForbidden(error?: unknown): boolean {
  if (!error) return false;
  const anyErr = error as { statusCode?: number; status?: number; message?: string };
  if (
    anyErr.statusCode === 401 ||
    anyErr.statusCode === 403 ||
    anyErr.status === 401 ||
    anyErr.status === 403
  ) {
    return true;
  }
  const errorMsg =
    anyErr.message || (typeof error === "string" ? error : String(error)) || "";
  const lower = errorMsg.toLowerCase();
  return (
    lower.includes("401") ||
    lower.includes("403") ||
    lower.includes("unauthorized") ||
    lower.includes("forbidden")
  );
}

/**
 * Resolves the active access token or API key from API client, Zustand app store, or localStorage.
 */
export function getAccessToken(): string {
  return (
    apiClient.getStoredApiKey?.() ||
    useAppStore.getState?.().apiKey ||
    (typeof window !== "undefined" && window.localStorage
      ? localStorage.getItem("seedarr_api_key") ||
        localStorage.getItem("seedarr_token") ||
        ""
      : "") ||
    ""
  );
}

/**
 * Exponential backoff retry policy with randomized jitter and HTTP 401/403 guards.
 * Ensures the connection never permanently gives up on temporary backend reboots/reloads.
 */
export class ExponentialBackoffRetryPolicy implements IRetryPolicy {
  private readonly initialDelayMs: number;
  private readonly maxDelayMs: number;
  private readonly backoffFactor: number;

  constructor(
    initialDelayMs: number = 1000,
    maxDelayMs: number = 30000,
    backoffFactor: number = 1.5,
  ) {
    this.initialDelayMs = initialDelayMs;
    this.maxDelayMs = maxDelayMs;
    this.backoffFactor = backoffFactor;
  }

  nextRetryDelayInMilliseconds(retryContext: RetryContext): number | null {
    // 401/403 HTTP error guards: do not retry if unauthorized or forbidden
    if (isUnauthorizedOrForbidden(retryContext.retryReason)) {
      return null;
    }

    // Exponential backoff calculation
    const baseDelay = Math.min(
      this.maxDelayMs,
      this.initialDelayMs *
        Math.pow(this.backoffFactor, retryContext.previousRetryCount),
    );

    // Random jitter (+/- 20%)
    const jitter = Math.random() * 0.4 + 0.8;
    return Math.min(this.maxDelayMs, Math.round(baseDelay * jitter));
  }
}

let connection: HubConnection | null = null;
let startPromise: Promise<void> | null = null;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;
const statusListeners = new Set<(status: ConnectionStatus) => void>();

function notifyStatus(status: ConnectionStatus) {
  statusListeners.forEach((listener) => {
    try {
      listener(status);
    } catch (err) {
      console.error("Error in SignalR status listener:", err);
    }
  });
}

function mapHubState(state: HubConnectionState): ConnectionStatus {
  switch (state) {
    case HubConnectionState.Connected:
      return "connected";
    case HubConnectionState.Reconnecting:
      return "reconnecting";
    default:
      return "disconnected";
  }
}

export function getHubUrl(): string {
  if (typeof window === "undefined" || !window.location) {
    return "/signalr/messages";
  }

  const pathname = window.location.pathname;
  if (!pathname || pathname === "/") {
    return "/signalr/messages";
  }

  // Known client-side route segments to strip in order to determine reverse proxy base path
  const knownRoutes = [
    "login",
    "torrents",
    "add-torrent",
    "activity",
    "history",
    "downloadplusplus",
    "download++",
    "trackerboost",
    "tracker",
    "trackermetrics",
    "peermap",
    "schedule",
    "statistics",
    "automation",
    "settings",
    "system",
    "api-docs",
  ];

  const segments = pathname.split("/").filter(Boolean);
  const routeIndex = segments.findIndex((seg) =>
    knownRoutes.includes(seg.toLowerCase()),
  );

  let basePath = "";
  if (routeIndex > 0) {
    basePath = "/" + segments.slice(0, routeIndex).join("/");
  } else if (routeIndex === -1 && segments.length > 0) {
    basePath = "/" + segments.join("/");
  }

  const hubUrl = `${basePath}/signalr/messages`.replace(/\/+/g, "/");
  return hubUrl;
}

export function getSignalRConnection(): HubConnection {
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(getHubUrl(), {
        accessTokenFactory: () => {
          return (
            apiClient.getStoredApiKey?.() ||
            useAppStore.getState?.().apiKey ||
            (typeof window !== "undefined" && window.localStorage
              ? localStorage.getItem("seedarr_api_key") ||
                localStorage.getItem("seedarr_token") ||
                ""
              : "") ||
            ""
          );
        },
      })
      .withAutomaticReconnect(new ExponentialBackoffRetryPolicy())
      .configureLogging(LogLevel.Warning)
      .build();

    connection.onreconnecting(() => notifyStatus("reconnecting"));
    connection.onreconnected(() => notifyStatus("connected"));
    connection.onclose((error) => {
      notifyStatus("disconnected");
      if (reconnectTimer) {
        clearTimeout(reconnectTimer);
        reconnectTimer = null;
      }
      if (isUnauthorizedOrForbidden(error)) {
        return;
      }
      // If closed, trigger reconnection after a short delay
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        if (connection?.state === HubConnectionState.Disconnected) {
          startSignalR();
        }
      }, 2000);
    });
  }
  return connection;
}

export async function startSignalR(): Promise<void> {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  const conn = getSignalRConnection();
  if (conn.state === HubConnectionState.Connecting) {
    return startPromise ?? undefined;
  }
  if (conn.state === HubConnectionState.Disconnected) {
    if (!startPromise) {
      startPromise = conn
        .start()
        .then(() => {
          notifyStatus("connected");
        })
        .catch((err) => {
          console.error("SignalR connection failed:", err);
          notifyStatus("disconnected");
        })
        .finally(() => {
          startPromise = null;
        });
    }
    return startPromise;
  }
}

export async function reconnectSignalR(): Promise<void> {
  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  const conn = getSignalRConnection();
  if (conn.state === HubConnectionState.Connected) {
    return;
  }
  if (conn.state === HubConnectionState.Connecting) {
    if (startPromise) {
      try {
        await startPromise;
      } catch {
        // ignore in-flight start error
      }
    }
    if ((conn.state as HubConnectionState) === HubConnectionState.Connected) {
      return;
    }
  }
  if (conn.state === HubConnectionState.Reconnecting) {
    try {
      await conn.stop();
    } catch {
      // ignore
    }
  }
  notifyStatus("reconnecting");
  return startSignalR();
}

export function onSignalRMessage(
  action: string,
  callback: (data: unknown) => void,
): () => void {
  const conn = getSignalRConnection();
  conn.on(action, callback);
  return () => {
    conn.off(action, callback);
  };
}

export function useSignalR(queryClient?: QueryClient) {
  const conn = getSignalRConnection();
  const [status, setStatus] = useState<ConnectionStatus>(() =>
    mapHubState(conn.state),
  );
  const queryClientRef = useRef(queryClient);
  queryClientRef.current = queryClient;

  useEffect(() => {
    let isMounted = true;

    const handleStatusChange = (newStatus: ConnectionStatus) => {
      if (isMounted) {
        setStatus(newStatus);
      }
    };

    statusListeners.add(handleStatusChange);
    setStatus(mapHubState(conn.state));

    if (conn.state === HubConnectionState.Disconnected) {
      startSignalR();
    }

    const handleReconnected = () => {
      const qc = queryClientRef.current;
      if (qc) {
        qc.invalidateQueries({ queryKey: ["torrents"] });
        qc.invalidateQueries({ queryKey: ["seeding", "stats"] });
        qc.invalidateQueries({ queryKey: ["health"] });
      }
    };

    conn.onreconnected(handleReconnected);

    return () => {
      isMounted = false;
      statusListeners.delete(handleStatusChange);
      const callbacks = (
        conn as unknown as { _reconnectedCallbacks?: Array<unknown> }
      )._reconnectedCallbacks;
      if (callbacks) {
        const idx = callbacks.indexOf(handleReconnected);
        if (idx !== -1) {
          callbacks.splice(idx, 1);
        }
      }
    };
  }, [conn]);

  return {
    connection: conn,
    status,
    connected: status === "connected",
    isReconnecting: status === "reconnecting",
    reconnect: reconnectSignalR,
  };
}

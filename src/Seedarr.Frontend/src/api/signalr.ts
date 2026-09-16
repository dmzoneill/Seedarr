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

export type ConnectionStatus = "connected" | "disconnected" | "reconnecting";

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
    const errorMsg = retryContext.retryReason?.message || "";

    // 401/403 HTTP error guards: do not retry if unauthorized or forbidden
    if (
      errorMsg.includes("401") ||
      errorMsg.includes("403") ||
      errorMsg.includes("Unauthorized") ||
      errorMsg.includes("Forbidden")
    ) {
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
      .withUrl(getHubUrl())
      .withAutomaticReconnect(new ExponentialBackoffRetryPolicy())
      .configureLogging(LogLevel.Warning)
      .build();

    connection.onreconnecting(() => notifyStatus("reconnecting"));
    connection.onreconnected(() => notifyStatus("connected"));
    connection.onclose(() => {
      notifyStatus("disconnected");
      // If closed, trigger reconnection after a short delay
      setTimeout(() => {
        if (connection?.state === HubConnectionState.Disconnected) {
          startSignalR();
        }
      }, 2000);
    });
  }
  return connection;
}

export async function startSignalR(): Promise<void> {
  const conn = getSignalRConnection();
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
  const conn = getSignalRConnection();
  if (conn.state === HubConnectionState.Connected) {
    return;
  }
  if (
    conn.state === HubConnectionState.Connecting ||
    conn.state === HubConnectionState.Reconnecting
  ) {
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

    return () => {
      isMounted = false;
      statusListeners.delete(handleStatusChange);
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

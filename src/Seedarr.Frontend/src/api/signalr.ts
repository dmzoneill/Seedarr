import { useState, useEffect, useRef } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";

export type ConnectionStatus = "connected" | "disconnected" | "reconnecting";

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

export function getSignalRConnection(): HubConnection {
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl("/signalr/messages")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.onreconnecting(() => notifyStatus("reconnecting"));
    connection.onreconnected(() => notifyStatus("connected"));
    connection.onclose(() => notifyStatus("disconnected"));
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

  return { connection: conn, status };
}

import { useState, useEffect, useCallback, useRef } from 'react';
import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import type { QueryClient } from '@tanstack/react-query';

export type ConnectionStatus = 'connected' | 'disconnected' | 'reconnecting';

let connection: HubConnection | null = null;

export function getSignalRConnection(): HubConnection {
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl('/signalr/messages')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
  return connection;
}

export async function startSignalR(): Promise<void> {
  const conn = getSignalRConnection();
  if (conn.state === 'Disconnected') {
    try {
      await conn.start();
    } catch (err) {
      console.error('SignalR connection failed:', err);
    }
  }
}

export function onSignalRMessage(
  action: string,
  callback: (data: unknown) => void
): void {
  const conn = getSignalRConnection();
  conn.on(action, callback);
}

function mapHubState(state: HubConnectionState): ConnectionStatus {
  switch (state) {
    case HubConnectionState.Connected:
      return 'connected';
    case HubConnectionState.Reconnecting:
      return 'reconnecting';
    default:
      return 'disconnected';
  }
}

export function useSignalR(queryClient: QueryClient) {
  const [status, setStatus] = useState<ConnectionStatus>('disconnected');
  const queryClientRef = useRef(queryClient);
  queryClientRef.current = queryClient;

  const registerEventHandlers = useCallback((conn: HubConnection) => {
    conn.onreconnecting(() => setStatus('reconnecting'));
    conn.onreconnected(() => setStatus('connected'));
    conn.onclose(() => setStatus('disconnected'));
  }, []);

  useEffect(() => {
    const conn = getSignalRConnection();
    registerEventHandlers(conn);

    if (conn.state === HubConnectionState.Disconnected) {
      conn.start()
        .then(() => setStatus('connected'))
        .catch((err) => {
          console.error('SignalR connection failed:', err);
          setStatus('disconnected');
        });
    } else {
      setStatus(mapHubState(conn.state));
    }

    return () => {
      // Don't stop the shared connection on unmount -- other consumers may
      // still be using it.  State listeners are cleaned up by React.
    };
  }, [registerEventHandlers]);

  return { connection: getSignalRConnection(), status };
}

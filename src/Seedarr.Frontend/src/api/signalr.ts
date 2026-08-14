import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
} from '@microsoft/signalr';

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

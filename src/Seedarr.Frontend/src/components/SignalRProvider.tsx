import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useSignalR } from '../api/signalr';
import { useToast } from '../context/ToastContext';

const EVENT_INVALIDATION_MAP: Record<string, string[][]> = {
  TorrentAdded: [['torrents']],
  TorrentUpdated: [['torrents']],
  TorrentDeleted: [['torrents']],
  SeedingStatsUpdated: [['seeding', 'stats']],
  HealthCheckCompleted: [['health']],
  CommandCompleted: [['system', 'status']],
};

export default function SignalRProvider() {
  const queryClient = useQueryClient();
  const { connection, status } = useSignalR(queryClient);
  const { showToast } = useToast();
  const showToastRef = useRef(showToast);
  showToastRef.current = showToast;

  useEffect(() => {
    for (const [event, queryKeys] of Object.entries(EVENT_INVALIDATION_MAP)) {
      connection.on(event, (data?: unknown) => {
        for (const key of queryKeys) {
          queryClient.invalidateQueries({ queryKey: key });
        }

        // Fire toast notifications for key events
        if (event === 'TorrentAdded') {
          const name =
            data && typeof data === 'object' && 'name' in data
              ? String((data as Record<string, unknown>).name)
              : undefined;
          showToastRef.current(
            name ? `Torrent added: ${name}` : 'Torrent added',
            'success'
          );
        } else if (event === 'TorrentDeleted') {
          showToastRef.current('Torrent removed', 'info');
        }
      });
    }

    return () => {
      for (const event of Object.keys(EVENT_INVALIDATION_MAP)) {
        connection.off(event);
      }
    };
  }, [connection, queryClient]);

  const dotColor =
    status === 'connected'
      ? 'var(--signalr-connected, #22c55e)'
      : status === 'reconnecting'
        ? 'var(--signalr-reconnecting, #f59e0b)'
        : 'var(--signalr-disconnected, #ef4444)';

  const title =
    status === 'connected'
      ? 'Real-time: connected'
      : status === 'reconnecting'
        ? 'Real-time: reconnecting...'
        : 'Real-time: disconnected';

  return (
    <span
      title={title}
      aria-label={title}
      style={{
        display: 'inline-block',
        width: 8,
        height: 8,
        borderRadius: '50%',
        backgroundColor: dotColor,
        position: 'fixed',
        bottom: 12,
        right: 12,
        zIndex: 9999,
        transition: 'background-color 0.3s ease',
      }}
    />
  );
}

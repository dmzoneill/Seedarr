import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type {
  Torrent,
  SeedingStats,
  SystemStatus,
  HealthCheckResult,
  NetworkStatus,
  Peer,
  GeneralConfig,
  SeedingConfig,
  NetworkConfig,
  BitTorrentConfig,
  PeerProtocolConfig,
  ProtocolsConfig,
  SimulationConfig,
  TrackerServerConfig,
  TrackerServerStats,
  SchedulerConfig,
  AdvancedConfig,
} from './types';

type AddTorrentInput =
  | { file: File; magnetLink?: never }
  | { magnetLink: string; file?: never };

export function useTorrents() {
  return useQuery<Torrent[]>({
    queryKey: ['torrents'],
    queryFn: () => apiClient.get('/torrents'),
    refetchInterval: 5000,
  });
}

export function useTorrent(id: number) {
  return useQuery<Torrent>({
    queryKey: ['torrents', id],
    queryFn: () => apiClient.get(`/torrents/${id}`),
    enabled: id > 0,
  });
}

export function useAddTorrent() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, AddTorrentInput>({
    mutationFn: async (input) => {
      if (input.file) {
        const formData = new FormData();
        formData.append('file', input.file);
        const response = await fetch('/api/v1/torrents', {
          method: 'POST',
          body: formData,
        });
        if (!response.ok) {
          throw new Error(`API error: ${response.status} ${response.statusText}`);
        }
        return response.json();
      }
      return apiClient.post('/torrents', { magnetLink: input.magnetLink });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['torrents'] }),
  });
}

export function useDeleteTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/torrents/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['torrents'] }),
  });
}

export function useStartSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/start/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['torrents'] });
      queryClient.invalidateQueries({ queryKey: ['seeding'] });
    },
  });
}

export function useStopSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/stop/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['torrents'] });
      queryClient.invalidateQueries({ queryKey: ['seeding'] });
    },
  });
}

export function useStartAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/seeding/start-all'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['torrents'] });
      queryClient.invalidateQueries({ queryKey: ['seeding'] });
    },
  });
}

export function useStopAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/seeding/stop-all'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['torrents'] });
      queryClient.invalidateQueries({ queryKey: ['seeding'] });
    },
  });
}

export function useSeedingStats() {
  return useQuery<SeedingStats>({
    queryKey: ['seeding', 'stats'],
    queryFn: () => apiClient.get('/seeding/stats'),
    refetchInterval: 5000,
  });
}

export function useSystemStatus() {
  return useQuery<SystemStatus>({
    queryKey: ['system', 'status'],
    queryFn: () => apiClient.get('/system/status'),
  });
}

export function useHealthChecks() {
  return useQuery<HealthCheckResult[]>({
    queryKey: ['health'],
    queryFn: () => apiClient.get('/health'),
    refetchInterval: 30000,
  });
}

export function useNetworkStatus() {
  return useQuery<NetworkStatus>({
    queryKey: ['network', 'status'],
    queryFn: () => apiClient.get('/network/status'),
  });
}

export function usePeers(torrentId: number) {
  return useQuery<Peer[]>({
    queryKey: ['torrents', torrentId, 'peers'],
    queryFn: () => apiClient.get(`/torrents/${torrentId}/peers`),
    enabled: torrentId > 0,
    refetchInterval: 5000,
  });
}

export function useArrSync() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/arrsync/sync'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['torrents'] }),
  });
}

function useConfigQuery<T>(section: string) {
  return useQuery<T>({
    queryKey: ['config', section],
    queryFn: () => apiClient.get(`/config/${section}`),
  });
}

function useConfigMutation<T>(section: string) {
  const queryClient = useQueryClient();
  return useMutation<T, Error, T>({
    mutationFn: (config) => apiClient.put(`/config/${section}`, config),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config', section] }),
  });
}

export function useGeneralConfig() {
  return useConfigQuery<GeneralConfig>('general');
}

export function useSaveGeneralConfig() {
  return useConfigMutation<GeneralConfig>('general');
}

export function useSeedingConfig() {
  return useConfigQuery<SeedingConfig>('seeding');
}

export function useSaveSeedingConfig() {
  return useConfigMutation<SeedingConfig>('seeding');
}

export function useNetworkConfig() {
  return useConfigQuery<NetworkConfig>('network');
}

export function useSaveNetworkConfig() {
  return useConfigMutation<NetworkConfig>('network');
}

export function useBitTorrentConfig() {
  return useConfigQuery<BitTorrentConfig>('bittorrent');
}

export function useSaveBitTorrentConfig() {
  return useConfigMutation<BitTorrentConfig>('bittorrent');
}

export function usePeerProtocolConfig() {
  return useConfigQuery<PeerProtocolConfig>('peerprotocol');
}

export function useSavePeerProtocolConfig() {
  return useConfigMutation<PeerProtocolConfig>('peerprotocol');
}

export function useProtocolsConfig() {
  return useConfigQuery<ProtocolsConfig>('protocols');
}

export function useSaveProtocolsConfig() {
  return useConfigMutation<ProtocolsConfig>('protocols');
}

export function useSimulationConfig() {
  return useConfigQuery<SimulationConfig>('simulation');
}

export function useSaveSimulationConfig() {
  return useConfigMutation<SimulationConfig>('simulation');
}

export function useTrackerServerConfig() {
  return useConfigQuery<TrackerServerConfig>('trackerserver');
}

export function useSaveTrackerServerConfig() {
  return useConfigMutation<TrackerServerConfig>('trackerserver');
}

export function useTrackerServerStats() {
  return useQuery<TrackerServerStats>({
    queryKey: ['trackerserver', 'stats'],
    queryFn: () => apiClient.get('/trackerserver/stats'),
    refetchInterval: 5000,
  });
}

export function useSchedulerConfig() {
  return useConfigQuery<SchedulerConfig>('scheduler');
}

export function useSaveSchedulerConfig() {
  return useConfigMutation<SchedulerConfig>('scheduler');
}

export function useAdvancedConfig() {
  return useConfigQuery<AdvancedConfig>('advanced');
}

export function useSaveAdvancedConfig() {
  return useConfigMutation<AdvancedConfig>('advanced');
}

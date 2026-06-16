import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type {
  Torrent,
  SeedingStats,
  SystemStatus,
  HealthCheckResult,
  NetworkStatus,
  Peer,
  TrackerServerConfig,
  TrackerServerStats,
  GeneralConfig,
  SeedingConfig,
  NetworkConfig,
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

export function useTrackerServerConfig() {
  return useQuery<TrackerServerConfig>({
    queryKey: ['trackerserver', 'config'],
    queryFn: () => apiClient.get('/trackerserver/config'),
  });
}

export function useUpdateTrackerServerConfig() {
  const queryClient = useQueryClient();
  return useMutation<TrackerServerConfig, Error, TrackerServerConfig>({
    mutationFn: (config) => apiClient.put('/trackerserver/config', config),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['trackerserver', 'config'] }),
  });
}

export function useTrackerServerStats() {
  return useQuery<TrackerServerStats>({
    queryKey: ['trackerserver', 'stats'],
    queryFn: () => apiClient.get('/trackerserver/stats'),
    refetchInterval: 5000,
  });
}

export function useGeneralConfig() {
  return useQuery<GeneralConfig>({
    queryKey: ['config', 'general'],
    queryFn: () => apiClient.get('/config/general'),
  });
}

export function useSaveGeneralConfig() {
  const queryClient = useQueryClient();
  return useMutation<GeneralConfig, Error, GeneralConfig>({
    mutationFn: (config) => apiClient.put('/config/general', config),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config', 'general'] }),
  });
}

export function useSeedingConfig() {
  return useQuery<SeedingConfig>({
    queryKey: ['config', 'seeding'],
    queryFn: () => apiClient.get('/config/seeding'),
  });
}

export function useSaveSeedingConfig() {
  const queryClient = useQueryClient();
  return useMutation<SeedingConfig, Error, SeedingConfig>({
    mutationFn: (config) => apiClient.put('/config/seeding', config),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config', 'seeding'] }),
  });
}

export function useNetworkConfig() {
  return useQuery<NetworkConfig>({
    queryKey: ['config', 'network'],
    queryFn: () => apiClient.get('/config/network'),
  });
}

export function useSaveNetworkConfig() {
  const queryClient = useQueryClient();
  return useMutation<NetworkConfig, Error, NetworkConfig>({
    mutationFn: (config) => apiClient.put('/config/network', config),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['config', 'network'] }),
  });
}

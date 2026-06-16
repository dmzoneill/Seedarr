import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type {
  Torrent,
  SeedingStats,
  SystemStatus,
  HealthCheckResult,
  NetworkStatus,
} from './types';

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

export function useArrSync() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post('/arrsync/sync'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['torrents'] }),
  });
}

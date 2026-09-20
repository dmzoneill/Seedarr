import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { trackTorrentAction } from "../utils/analytics";
import type {
  Torrent,
  BulkTorrentActionResource,
  BulkActionResult,
  Category,
  TorrentFileInfo,
  SubtitleTrack,
  PieceMapResource,
  SeedingStats,
  SpeedSnapshot,
  TorrentSpeedSnapshot,
  SystemStatus,
  HealthCheckResult,
  NetworkStatus,
  NetworkInterfaceResource,
  Peer,
  TrackerEntry,
  TrackerServerTorrent,
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
  ArrConnection,
  ArrTestResult,
  DownloadClientTestResult,
  IndexerTestResult,
  TorznabCapabilities,
  ProwlarrSyncResult,
  SyncResult,
  BatchImportResponse,
  IndexerDefinition,
  DownloadClientDefinition,
  DownloadClientRemoteItem,
  DiskSpaceInfo,
  Backup,
  UpdateEntry,
  LogFile,
  PeerGraphData,
  SpeedScheduleEntry,
  SpeedLimits,
  Tag,
  PeerConnectionLogEntry,
  TorrentEventLogEntry,
  NetworkDiagnostics,
  PortTestResult,
  DownloadHistoryEntry,
  ReleaseInfo,
  DownloadReleaseRequest,
  TrackerBoostTracker,
  TrackerBoostStatusSummary,
  TrackerBoostSettings,
  TrackerCrossMatrixResult,
  TrackerBoostLogEntry,
  DownloadPlusPlusTracker,
  DownloadPlusPlusStatusSummary,
  SwarmBoostResult,
  TorrentTrackerInspectionResult,
  TrackerMetric,
  TrackerMetricsSummary,
  TrackerMetricSnapshot,
  NotificationResource,
  NotificationTestResult,
  RssRule,
  RssGrabHistory,
  AutomationScript,
  AutomationExecutionResult,
  AutomationMarketplaceTemplate,
  AutomationTestRequest,
  InstallMarketplaceTemplateRequest,
  FileSystemResource,
  CustomScriptTestRequest,
  CustomScriptTestResult,
  RemotePathMapping,
  RemotePathMappingTestResult,
  RemotePathMappingTestRequest,
} from "./types";

const DEFAULT_REFETCH_MS = 5000;

export function useRefetchInterval(): number {
  const { data } = useQuery<{ uiRefreshRateSec: number }>({
    queryKey: ["config", "advanced"],
    queryFn: () => apiClient.get("/config/advanced"),
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
  return data?.uiRefreshRateSec
    ? data.uiRefreshRateSec * 1000
    : DEFAULT_REFETCH_MS;
}

export interface IngestionOptions {
  category?: string;
  savePath?: string;
  paused?: boolean;
  sequentialDownload?: boolean;
  firstLastPiecePrio?: boolean;
}

export type AddTorrentInput =
  | ({ files: File[]; magnetLink?: never } & IngestionOptions)
  | ({ magnetLink: string; files?: never } & IngestionOptions);

export interface TorrentUploadFailure {
  fileName: string;
  reason: string;
}

export interface AddTorrentResult {
  added: Torrent[];
  failed: TorrentUploadFailure[];
}

export function useTorrents() {
  const interval = useRefetchInterval();
  return useQuery<Torrent[]>({
    queryKey: ["torrents"],
    queryFn: () => apiClient.get("/torrent"),
    refetchInterval: interval,
  });
}

export function useTorrent(id: number) {
  const interval = useRefetchInterval();
  return useQuery<Torrent>({
    queryKey: ["torrents", id],
    queryFn: () => apiClient.get(`/torrent/${id}`),
    enabled: id > 0,
    refetchInterval: interval,
  });
}

export function useTorrentFiles(torrentId: number) {
  return useQuery<TorrentFileInfo[]>({
    queryKey: ["torrents", torrentId, "files"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/files`),
    enabled: torrentId > 0,
  });
}

export function useRenameTorrentFile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      hash,
      oldPath,
      newPath,
    }: {
      torrentId?: number;
      hash: string;
      oldPath: string;
      newPath: string;
    }) => apiClient.renameTorrentFile(hash, oldPath, newPath),
    onSuccess: (_, variables) => {
      if (variables.torrentId) {
        queryClient.invalidateQueries({
          queryKey: ["torrents", variables.torrentId, "files"],
        });
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useTorrentFileSubtitles(torrentId?: number, fileId?: number) {
  return useQuery<SubtitleTrack[]>({
    queryKey: ["torrents", torrentId, "files", fileId, "subtitles"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/files/${fileId}/subtitles`),
    enabled: typeof torrentId === "number" && torrentId > 0 && typeof fileId === "number" && fileId > 0,
  });
}

export function usePieceMap(torrentId?: number) {
  const interval = useRefetchInterval();
  return useQuery<PieceMapResource>({
    queryKey: ["torrents", torrentId, "piecemap"],
    queryFn: () => apiClient.get(`/torrents/${torrentId}/piecemap`),
    enabled: typeof torrentId === "number" && torrentId > 0,
    refetchInterval: interval,
  });
}

export function useTorrentTrackers(torrentId: number) {
  const interval = useRefetchInterval();
  return useQuery<TrackerEntry[]>({
    queryKey: ["torrents", torrentId, "trackers"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/trackers`),
    enabled: torrentId > 0,
    refetchInterval: interval,
  });
}

export function useDeleteTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, { torrentId: number; trackerId: number }>({
    mutationFn: ({ torrentId, trackerId }) =>
      apiClient.delete(`/torrent/${torrentId}/trackers/${trackerId}`),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "trackers"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useAddTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<
    TrackerEntry,
    Error,
    { torrentId: number; url: string; tier?: number }
  >({
    mutationFn: ({ torrentId, url, tier }) =>
      apiClient.post(`/torrent/${torrentId}/trackers`, {
        url,
        tier: tier ?? 1,
      }),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "trackers"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useUpdateTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<
    TrackerEntry,
    Error,
    { torrentId: number; trackerId: number; tier?: number; enabled?: boolean }
  >({
    mutationFn: ({ torrentId, trackerId, tier, enabled }) =>
      apiClient.put(`/torrent/${torrentId}/trackers/${trackerId}`, {
        tier,
        enabled,
      }),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", vars.torrentId, "trackers"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}


export function useTorrentLogs(
  torrentId: number,
  options?: { polling?: boolean },
) {
  return useQuery<TorrentEventLogEntry[]>({
    queryKey: ["torrents", torrentId, "logs"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/logs?count=100`),
    enabled: torrentId > 0,
    refetchInterval: options?.polling === false ? false : 3000,
  });
}

export function useAnnounceTorrentTracker() {
  const queryClient = useQueryClient();
  return useMutation<
    { success: boolean; message: string },
    Error,
    { torrentId: number; trackerId: number }
  >({
    mutationFn: ({ torrentId, trackerId }) =>
      apiClient.post(
        `/torrent/${torrentId}/trackers/${trackerId}/announce`,
        {},
      ),
    onSuccess: (_, { torrentId }) => {
      queryClient.invalidateQueries({
        queryKey: ["torrents", torrentId, "trackers"],
      });
      queryClient.invalidateQueries({
        queryKey: ["torrents", torrentId, "logs"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents", torrentId] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useAddTorrent() {
  const queryClient = useQueryClient();
  return useMutation<AddTorrentResult, Error, AddTorrentInput>({
    mutationFn: async (input) => {
      if (input.files && input.files.length > 0) {
        const formData = new FormData();
        input.files.forEach((file) => formData.append("file", file));
        if (input.category) formData.append("category", input.category);
        if (input.savePath) formData.append("savePath", input.savePath);
        if (input.paused !== undefined)
          formData.append("paused", String(input.paused));
        if (input.sequentialDownload !== undefined)
          formData.append(
            "sequentialDownload",
            String(input.sequentialDownload),
          );
        if (input.firstLastPiecePrio !== undefined)
          formData.append(
            "firstLastPiecePrio",
            String(input.firstLastPiecePrio),
          );
        return apiClient.postForm<AddTorrentResult>(
          "/torrent/upload",
          formData,
        );
      }
      return apiClient.post("/torrent", {
        magnetLink: input.magnetLink,
        category: input.category,
        savePath: input.savePath,
        sequentialDownload: input.sequentialDownload,
        firstLastPiecePrio: input.firstLastPiecePrio,
        status: input.paused ? "Paused" : undefined,
      });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["torrents"] }),
  });
}

export function useUpdateTorrent() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, Torrent>({
    mutationFn: (torrent) => apiClient.put(`/torrent/${torrent.id}`, torrent),
    onSuccess: (_, torrent) => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["torrents", torrent.id] });
    },
  });
}

export function useDeleteTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      deleteFiles = false,
    }: {
      id: number;
      deleteFiles?: boolean;
    }) =>
      apiClient.delete(
        `/torrent/${id}${deleteFiles ? "?deleteFiles=true" : ""}`,
      ),
    onSuccess: (_, variables) => {
      trackTorrentAction("delete", variables.id, variables.deleteFiles);
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useAnnounceTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/torrent/${id}/announce`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useRecheckTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/torrent/${id}/recheck`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useMoveTorrentQueue() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, position }: { id: number; position: string }) =>
      apiClient.put(`/torrent/${id}/queue`, { position }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useStartSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/start/${id}`),
    onSuccess: (_, id) => {
      trackTorrentAction("start_seeding", id);
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useStopSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.post(`/seeding/stop/${id}`),
    onSuccess: (_, id) => {
      trackTorrentAction("stop_seeding", id);
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
    },
  });
}

export function useStartAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post("/seeding/start-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });
}

export function useStopAllSeeding() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post("/seeding/stop-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });
}

export function useBulkTorrentAction() {
  const queryClient = useQueryClient();
  return useMutation<BulkActionResult, Error, BulkTorrentActionResource>({
    mutationFn: (data: BulkTorrentActionResource) =>
      apiClient.post<BulkActionResult>("/torrent/bulk", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["seeding"] });
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });
}

export function useSeedingStats() {
  const interval = useRefetchInterval();
  return useQuery<SeedingStats>({
    queryKey: ["seeding", "stats"],
    queryFn: () => apiClient.get("/seeding/stats"),
    refetchInterval: interval,
  });
}

export function useSpeedHistory() {
  return useQuery<SpeedSnapshot[]>({
    queryKey: ["seeding", "history"],
    queryFn: () => apiClient.get("/seeding/history"),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useTorrentSpeedHistory(torrentId: number) {
  return useQuery<TorrentSpeedSnapshot[]>({
    queryKey: ["seeding", "history", torrentId],
    queryFn: () => apiClient.get(`/seeding/history/${torrentId}`),
    staleTime: Infinity,
    refetchOnWindowFocus: false,
    enabled: torrentId > 0,
  });
}

export function useSystemStatus() {
  return useQuery<SystemStatus>({
    queryKey: ["system", "status"],
    queryFn: () => apiClient.get("/system/status"),
  });
}

export function useHealthChecks() {
  return useQuery<HealthCheckResult[]>({
    queryKey: ["health"],
    queryFn: () => apiClient.get("/health"),
    refetchInterval: 30000,
  });
}

export function useDiskSpace() {
  return useQuery<DiskSpaceInfo[]>({
    queryKey: ["diskspace"],
    queryFn: () => apiClient.get("/diskspace"),
  });
}

export function useFileSystem(path?: string, includeFiles = false) {
  return useQuery<FileSystemResource>({
    queryKey: ["filesystem", path, includeFiles],
    queryFn: () => apiClient.getFileSystem(path, includeFiles),
  });
}

export function useNetworkStatus() {
  return useQuery<NetworkStatus>({
    queryKey: ["network", "status"],
    queryFn: () => apiClient.get("/network/status"),
  });
}

export function useNetworkInterfaces() {
  return useQuery<NetworkInterfaceResource[]>({
    queryKey: ["network", "interfaces"],
    queryFn: () => apiClient.get("/network/interfaces"),
  });
}

export function usePeers(torrentId: number) {
  const interval = useRefetchInterval();
  return useQuery<Peer[]>({
    queryKey: ["torrents", torrentId, "peers"],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/peers`),
    enabled: torrentId > 0,
    refetchInterval: interval,
  });
}

export function useArrSync() {
  const queryClient = useQueryClient();
  return useMutation<SyncResult, Error>({
    mutationFn: () => apiClient.post("/arrsync/sync"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["arrconnections"] });
    },
  });
}

export function useDownloadClientSync() {
  const queryClient = useQueryClient();
  return useMutation<SyncResult, Error>({
    mutationFn: () => apiClient.post("/downloadclientsync/sync"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["torrents"] }),
  });
}

function useConfigQuery<T>(section: string) {
  return useQuery<T>({
    queryKey: ["config", section],
    queryFn: () => apiClient.get(`/config/${section}`),
  });
}

function useConfigMutation<T>(section: string) {
  const queryClient = useQueryClient();
  return useMutation<T, Error, T>({
    mutationFn: (config) => apiClient.put(`/config/${section}/1`, config),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["config", section] }),
  });
}

export function useGeneralConfig() {
  return useConfigQuery<GeneralConfig>("general");
}

export function useSaveGeneralConfig() {
  return useConfigMutation<GeneralConfig>("general");
}

export function useSeedingConfig() {
  return useConfigQuery<SeedingConfig>("seeding");
}

export function useSaveSeedingConfig() {
  const queryClient = useQueryClient();
  return useMutation<
    SeedingConfig,
    Error,
    SeedingConfig,
    { previousConfig?: SeedingConfig }
  >({
    mutationFn: (config) => apiClient.put(`/config/seeding/1`, config),
    onMutate: async (newConfig) => {
      await queryClient.cancelQueries({ queryKey: ["config", "seeding"] });
      const previousConfig = queryClient.getQueryData<SeedingConfig>([
        "config",
        "seeding",
      ]);
      queryClient.setQueryData(["config", "seeding"], newConfig);
      return { previousConfig };
    },
    onError: (_err, _newConfig, context) => {
      if (context?.previousConfig) {
        queryClient.setQueryData(["config", "seeding"], context.previousConfig);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["config", "seeding"] });
      queryClient.invalidateQueries({ queryKey: ["speedschedule", "active"] });
    },
  });
}

export function useNetworkConfig() {
  return useConfigQuery<NetworkConfig>("network");
}

export function useSaveNetworkConfig() {
  return useConfigMutation<NetworkConfig>("network");
}

export function useBitTorrentConfig() {
  return useConfigQuery<BitTorrentConfig>("bittorrent");
}

export function useSaveBitTorrentConfig() {
  return useConfigMutation<BitTorrentConfig>("bittorrent");
}

export function usePeerProtocolConfig() {
  return useConfigQuery<PeerProtocolConfig>("peerprotocol");
}

export function useSavePeerProtocolConfig() {
  return useConfigMutation<PeerProtocolConfig>("peerprotocol");
}

export function useProtocolsConfig() {
  return useConfigQuery<ProtocolsConfig>("protocols");
}

export function useSaveProtocolsConfig() {
  return useConfigMutation<ProtocolsConfig>("protocols");
}

export function useSimulationConfig() {
  return useConfigQuery<SimulationConfig>("simulation");
}

export function useSaveSimulationConfig() {
  return useConfigMutation<SimulationConfig>("simulation");
}

export function useTrackerServerConfig() {
  return useConfigQuery<TrackerServerConfig>("trackerserver");
}

export function useSaveTrackerServerConfig() {
  return useConfigMutation<TrackerServerConfig>("trackerserver");
}

export function useTrackerServerStats() {
  const interval = useRefetchInterval();
  return useQuery<TrackerServerStats>({
    queryKey: ["trackerserver", "stats"],
    queryFn: () => apiClient.get("/trackerserver/stats"),
    refetchInterval: interval,
  });
}

export function useTrackerServerTorrents() {
  const interval = useRefetchInterval();
  return useQuery<TrackerServerTorrent[]>({
    queryKey: ["trackerserver", "torrents"],
    queryFn: () => apiClient.get("/trackerserver/torrents"),
    refetchInterval: interval,
  });
}

export function useSchedulerConfig() {
  return useConfigQuery<SchedulerConfig>("scheduler");
}

export function useSaveSchedulerConfig() {
  return useConfigMutation<SchedulerConfig>("scheduler");
}

export function useAdvancedConfig() {
  return useConfigQuery<AdvancedConfig>("advanced");
}

export function useSaveAdvancedConfig() {
  return useConfigMutation<AdvancedConfig>("advanced");
}

export function useCategories() {
  return useQuery<Category[]>({
    queryKey: ["categories"],
    queryFn: () => apiClient.getCategories(),
  });
}

export function useCategory(id: number) {
  return useQuery<Category>({
    queryKey: ["categories", id],
    queryFn: () => apiClient.getCategory(id),
    enabled: id > 0,
  });
}

export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation<Category, Error, Partial<Category>>({
    mutationFn: (category) => apiClient.createCategory(category),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useUpdateCategory() {
  const queryClient = useQueryClient();
  return useMutation<Category, Error, { id: number; data: Partial<Category> }>({
    mutationFn: ({ id, data }) => apiClient.updateCategory(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useDeleteCategory() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.deleteCategory(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["categories"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useArrConnections() {
  return useQuery<ArrConnection[]>({
    queryKey: ["arrconnections"],
    queryFn: () => apiClient.get("/arrconnections"),
  });
}

export function useCreateArrConnection() {
  const queryClient = useQueryClient();
  return useMutation<ArrConnection, Error, Partial<ArrConnection>>({
    mutationFn: (connection) => apiClient.post("/arrconnections", connection),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useUpdateArrConnection() {
  const queryClient = useQueryClient();
  return useMutation<ArrConnection, Error, ArrConnection>({
    mutationFn: (connection) =>
      apiClient.put(`/arrconnections/${connection.id}`, connection),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useDeleteArrConnection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/arrconnections/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["arrconnections"] }),
  });
}

export function useTestArrConnection() {
  return useMutation<ArrTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/arrconnections/${id}/test`),
  });
}

export function useTestDirectArrConnection() {
  return useMutation<ArrTestResult, Error, Partial<ArrConnection>>({
    mutationFn: (connection) =>
      apiClient.post("/arrconnections/test", connection),
  });
}

export function useDownloadClients() {
  return useQuery<DownloadClientDefinition[]>({
    queryKey: ["downloadclients"],
    queryFn: () => apiClient.get("/downloadclients"),
  });
}

export function useCreateDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation<
    DownloadClientDefinition,
    Error,
    Partial<DownloadClientDefinition>
  >({
    mutationFn: (client) => apiClient.post("/downloadclients", client),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
  });
}

export function useUpdateDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation<DownloadClientDefinition, Error, DownloadClientDefinition>(
    {
      mutationFn: (client) =>
        apiClient.put(`/downloadclients/${client.id}`, client),
      onSuccess: () =>
        queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
    },
  );
}

export function useDeleteDownloadClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/downloadclients/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["downloadclients"] }),
  });
}

export function useTestDownloadClient() {
  return useMutation<DownloadClientTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/downloadclients/${id}/test`),
  });
}

export function useTestDirectDownloadClient() {
  return useMutation<
    DownloadClientTestResult,
    Error,
    Partial<DownloadClientDefinition>
  >({
    mutationFn: (client) => apiClient.post("/downloadclients/test", client),
  });
}

export function useRemotePathMappings() {
  return useQuery<RemotePathMapping[]>({
    queryKey: ["remotepathmapping"],
    queryFn: () => apiClient.get("/remotepathmapping"),
  });
}

export function useCreateRemotePathMapping() {
  const queryClient = useQueryClient();
  return useMutation<
    RemotePathMapping,
    Error,
    Partial<RemotePathMapping>
  >({
    mutationFn: (mapping) => apiClient.post("/remotepathmapping", mapping),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["remotepathmapping"] }),
  });
}

export function useUpdateRemotePathMapping() {
  const queryClient = useQueryClient();
  return useMutation<RemotePathMapping, Error, RemotePathMapping>({
    mutationFn: (mapping) =>
      apiClient.put(`/remotepathmapping/${mapping.id}`, mapping),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["remotepathmapping"] }),
  });
}

export function useDeleteRemotePathMapping() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/remotepathmapping/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["remotepathmapping"] }),
  });
}

export function useTestRemotePathMapping() {
  return useMutation<
    RemotePathMappingTestResult,
    Error,
    RemotePathMappingTestRequest
  >({
    mutationFn: (request) =>
      apiClient.post("/remotepathmapping/test", request),
  });
}

export function useDownloadClientItems(clientId: number | string) {
  const interval = useRefetchInterval();
  const isAll = clientId === "all";
  const numId = typeof clientId === "number" ? clientId : parseInt(clientId, 10);
  const isValid = isAll || (!isNaN(numId) && numId > 0);

  return useQuery<DownloadClientRemoteItem[]>({
    queryKey: ["downloadclients", isAll ? "all" : numId, "items"],
    queryFn: () =>
      isAll
        ? apiClient.get("/downloadclients/items")
        : apiClient.get(`/downloadclients/${numId}/items`),
    enabled: isValid,
    refetchInterval: interval,
  });
}

export function usePauseRemoteTorrent(clientId?: number) {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, { clientId?: number; infoHash: string }>({
    mutationFn: ({ clientId: targetId, infoHash }) => {
      const id = targetId ?? clientId;
      return apiClient.post(`/downloadclients/${id}/torrents/${infoHash}/pause`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadclients"] });
    },
  });
}

export function useResumeRemoteTorrent(clientId?: number) {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, { clientId?: number; infoHash: string }>({
    mutationFn: ({ clientId: targetId, infoHash }) => {
      const id = targetId ?? clientId;
      return apiClient.post(`/downloadclients/${id}/torrents/${infoHash}/resume`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadclients"] });
    },
  });
}

export function useDeleteRemoteTorrent(clientId?: number) {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, { clientId?: number; infoHash: string; deleteData?: boolean }>({
    mutationFn: ({ clientId: targetId, infoHash, deleteData }) => {
      const id = targetId ?? clientId;
      return apiClient.delete(`/downloadclients/${id}/torrents/${infoHash}?deleteData=${deleteData ?? false}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadclients"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useImportDownloadClientTorrent(clientId?: number) {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, { infoHash: string; clientId?: number } | string>({
    mutationFn: (param) => {
      const hash = typeof param === "string" ? param : param.infoHash;
      const targetId = typeof param === "object" && param.clientId ? param.clientId : clientId;
      return apiClient.post(`/downloadclients/${targetId}/import/${hash}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["downloadclients"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useImportDownloadClientTorrents(clientId?: number) {
  const queryClient = useQueryClient();
  return useMutation<BatchImportResponse, Error, { infoHashes: string[]; clientId?: number } | string[]>({
    mutationFn: (param) => {
      const hashes = Array.isArray(param) ? param : param.infoHashes;
      const targetId = !Array.isArray(param) && param.clientId ? param.clientId : clientId;
      return apiClient.post(`/downloadclients/${targetId}/import-torrents`, { infoHashes: hashes });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["downloadclients"],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useIndexers() {
  return useQuery<IndexerDefinition[]>({
    queryKey: ["indexers"],
    queryFn: () => apiClient.get("/indexers"),
  });
}

export function useCreateIndexer() {
  const queryClient = useQueryClient();
  return useMutation<IndexerDefinition, Error, Partial<IndexerDefinition>>({
    mutationFn: (indexer) => apiClient.post("/indexers", indexer),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useUpdateIndexer() {
  const queryClient = useQueryClient();
  return useMutation<IndexerDefinition, Error, IndexerDefinition>({
    mutationFn: (indexer) => apiClient.put(`/indexers/${indexer.id}`, indexer),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useDeleteIndexer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/indexers/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useTestIndexer() {
  return useMutation<IndexerTestResult, Error, number>({
    mutationFn: (id) => apiClient.post(`/indexers/${id}/test`),
  });
}

export function useTestDirectIndexer() {
  return useMutation<IndexerTestResult, Error, Partial<IndexerDefinition>>({
    mutationFn: (indexer) => apiClient.post("/indexers/test", indexer),
  });
}

export function useSyncProwlarrIndexers() {
  const queryClient = useQueryClient();
  return useMutation<ProwlarrSyncResult, Error, { prowlarrIndexerId?: number } | undefined>({
    mutationFn: (req) => apiClient.post("/indexer/prowlarr/sync", req ?? {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["indexers"] }),
  });
}

export function useIndexerCaps(id?: number) {
  return useQuery<TorznabCapabilities>({
    queryKey: ["indexer", id, "caps"],
    queryFn: () => apiClient.get(`/indexers/${id}/caps`),
    enabled: !!id && id > 0,
  });
}

export function useBackups() {
  return useQuery<Backup[]>({
    queryKey: ["backups"],
    queryFn: () => apiClient.get("/backup"),
  });
}

export function useCreateBackup() {
  const queryClient = useQueryClient();
  return useMutation<Backup, Error, void>({
    mutationFn: () => apiClient.post("/backup"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backups"] }),
  });
}

export function useDeleteBackup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/backup/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["backups"] }),
  });
}

export function useRestoreBackup() {
  return useMutation({
    mutationFn: (fileName: string) =>
      apiClient.post("/backup/restore", { fileName }),
  });
}

export function useUpdates() {
  return useQuery<UpdateEntry[]>({
    queryKey: ["updates"],
    queryFn: () => apiClient.get("/update"),
    staleTime: 60_000,
  });
}

export function useLogFiles() {
  return useQuery<LogFile[]>({
    queryKey: ["logfiles"],
    queryFn: () => apiClient.get("/logfile"),
  });
}

export function useClearLogFiles() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete("/logfile"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["logfiles"] }),
  });
}

export function usePeerGraph(start?: string, end?: string) {
  const interval = useRefetchInterval();
  const params = new URLSearchParams();
  if (start) params.set("start", start);
  if (end) params.set("end", end);
  const query = params.toString();
  return useQuery<PeerGraphData>({
    queryKey: ["peerlog", "graph", start, end],
    queryFn: () => apiClient.get(`/peerlog/graph${query ? `?${query}` : ""}`),
    refetchInterval: interval,
  });
}

export function useSpeedSchedules() {
  return useQuery<SpeedScheduleEntry[]>({
    queryKey: ["speedschedule"],
    queryFn: () => apiClient.get("/speedschedule"),
  });
}

export function useActiveSpeedLimits() {
  const interval = useRefetchInterval();
  return useQuery<SpeedLimits>({
    queryKey: ["speedschedule", "active"],
    queryFn: () => apiClient.get("/speedschedule/active"),
    refetchInterval: interval,
  });
}

export function useCreateSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation<SpeedScheduleEntry, Error, Partial<SpeedScheduleEntry>>({
    mutationFn: (schedule) => apiClient.post("/speedschedule", schedule),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useUpdateSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation<SpeedScheduleEntry, Error, SpeedScheduleEntry>({
    mutationFn: (schedule) =>
      apiClient.put(`/speedschedule/${schedule.id}`, schedule),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useDeleteSpeedSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/speedschedule/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["speedschedule"] }),
  });
}

export function useTags() {
  return useQuery<Tag[]>({
    queryKey: ["tags"],
    queryFn: () => apiClient.get("/tag"),
  });
}

export function useCreateTag() {
  const queryClient = useQueryClient();
  return useMutation<Tag, Error, Partial<Tag>>({
    mutationFn: (tag) => apiClient.post("/tag", tag),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tags"] }),
  });
}

export function useUpdateTag() {
  const queryClient = useQueryClient();
  return useMutation<Tag, Error, Tag>({
    mutationFn: (tag) => apiClient.put("/tag", tag),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["tags"] }),
  });
}

export function useDeleteTag() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/tag/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useBulkAssignTags() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: { tagIds: number[]; torrentIds: number[] }) =>
      apiClient.post("/tag/bulk-assign", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useBulkRemoveTags() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: { tagIds: number[]; torrentIds: number[] }) =>
      apiClient.post("/tag/bulk-remove", data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function usePeerConnectionLog(params?: {
  start?: string;
  end?: string;
  infoHash?: string;
}) {
  const searchParams = new URLSearchParams();
  if (params?.start) searchParams.set("start", params.start);
  if (params?.end) searchParams.set("end", params.end);
  if (params?.infoHash) searchParams.set("infoHash", params.infoHash);
  const query = searchParams.toString();
  return useQuery<PeerConnectionLogEntry[]>({
    queryKey: ["peerlog", params?.start, params?.end, params?.infoHash],
    queryFn: () => apiClient.get(`/peerlog${query ? `?${query}` : ""}`),
  });
}

export function useNetworkDiagnostics() {
  const interval = useRefetchInterval();
  return useQuery<NetworkDiagnostics>({
    queryKey: ["network", "diagnostics"],
    queryFn: () => apiClient.get("/network/diagnostics"),
    refetchInterval: interval,
  });
}

export function useTestPort() {
  return useMutation<PortTestResult, Error, number | undefined>({
    mutationFn: (port?: number) =>
      apiClient.post<PortTestResult>(`/network/test-port${port ? `?port=${port}` : ""}`),
  });
}

export function useActivePeers() {
  const interval = useRefetchInterval();
  return useQuery<PeerConnectionLogEntry[]>({
    queryKey: ["peerlog", "active"],
    queryFn: () => apiClient.get("/peerlog/active"),
    refetchInterval: interval,
  });
}

export function useDownloadHistory(params?: {
  query?: string;
  status?: string;
  limit?: number;
}) {
  const interval = useRefetchInterval();
  const searchParams = new URLSearchParams();
  if (params?.query) searchParams.set("query", params.query);
  if (params?.status) searchParams.set("status", params.status);
  if (params?.limit) searchParams.set("limit", String(params.limit));
  const queryString = searchParams.toString();

  return useQuery<DownloadHistoryEntry[]>({
    queryKey: ["downloadhistory", params?.query, params?.status, params?.limit],
    queryFn: () =>
      apiClient.get(`/downloadhistory${queryString ? `?${queryString}` : ""}`),
    refetchInterval: interval,
  });
}

export function useReAddHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/downloadhistory/${id}/readd`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useDeleteHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/downloadhistory/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useClearDownloadHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete("/downloadhistory"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useEnrichHistoryTorrent() {
  const queryClient = useQueryClient();
  return useMutation<DownloadHistoryEntry, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/downloadhistory/${id}/enrich`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useEnrichAllHistory() {
  const queryClient = useQueryClient();
  return useMutation<{ message: string }, Error, void>({
    mutationFn: () => apiClient.post("/downloadhistory/enrich-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

export function useReconcileDownloadHistory() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; processedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/downloadhistory/reconcile"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
        queryClient.invalidateQueries({ queryKey: ["torrents"] });
      },
    },
  );
}

export function useIndexerSearch(
  params: { query: string; category?: string; indexerId?: number },
  enabled = true,
) {
  const searchParams = new URLSearchParams();
  searchParams.set("query", params.query);
  if (params.category) searchParams.set("category", params.category);
  if (params.indexerId) searchParams.set("indexerId", String(params.indexerId));
  const queryString = searchParams.toString();

  return useQuery<ReleaseInfo[]>({
    queryKey: [
      "indexers",
      "search",
      params.query,
      params.category,
      params.indexerId,
    ],
    queryFn: () => apiClient.get(`/indexers/search?${queryString}`),
    enabled: enabled && Boolean(params.query?.trim()),
    staleTime: 30_000,
  });
}

export function useDownloadIndexerRelease() {
  const queryClient = useQueryClient();
  return useMutation<Torrent, Error, DownloadReleaseRequest>({
    mutationFn: (req) => apiClient.post("/indexers/download", req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["downloadhistory"] });
    },
  });
}

// TrackerBoost Hooks

export function useTrackerBoostStatus() {
  return useQuery<TrackerBoostStatusSummary>({
    queryKey: ["trackerboost", "status"],
    queryFn: () => apiClient.get("/trackerboost/status"),
    refetchInterval: 10_000,
  });
}

export function useTrackerBoostTrackers() {
  return useQuery<TrackerBoostTracker[]>({
    queryKey: ["trackerboost", "trackers"],
    queryFn: () => apiClient.get("/trackerboost/trackers"),
  });
}

export function useInspectTorrentTrackers(torrentId: number, enabled = true) {
  return useQuery<TorrentTrackerInspectionResult>({
    queryKey: ["trackerboost", "check", torrentId],
    queryFn: () => apiClient.get(`/trackerboost/check/${torrentId}`),
    enabled: enabled && torrentId > 0,
  });
}

export function useInspectHashTrackers(
  infoHash: string,
  name = "",
  enabled = true,
) {
  return useQuery<TorrentTrackerInspectionResult>({
    queryKey: ["trackerboost", "check-hash", infoHash],
    queryFn: () => {
      const search = name ? `?name=${encodeURIComponent(name)}` : "";
      return apiClient.get(`/trackerboost/check-hash/${infoHash}${search}`);
    },
    enabled: enabled && Boolean(infoHash),
  });
}

export function useTrackerBoostSettings() {
  return useQuery<TrackerBoostSettings>({
    queryKey: ["trackerboost", "settings"],
    queryFn: () => apiClient.get("/trackerboost/settings"),
  });
}

export function useUpdateTrackerBoostSettings() {
  const queryClient = useQueryClient();
  return useMutation<TrackerBoostSettings, Error, TrackerBoostSettings>({
    mutationFn: (settings) => apiClient.put("/trackerboost/settings", settings),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useTrackerBoostMatrix() {
  return useQuery<TrackerCrossMatrixResult>({
    queryKey: ["trackerboost", "matrix"],
    queryFn: () => apiClient.get("/trackerboost/matrix"),
    refetchInterval: 15_000,
  });
}

export function useScanTrackerBoostTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; testedCount: number }, Error, void>({
    mutationFn: () => apiClient.post("/trackerboost/scan"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useHarvestDownloadTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/downloads"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useHarvestProwlarrTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/prowlarr"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useHarvestFeedTrackers() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; harvestedCount: number }, Error, void>(
    {
      mutationFn: () => apiClient.post("/trackerboost/harvest/feeds"),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      },
    },
  );
}

export function useBoostTorrent() {
  const queryClient = useQueryClient();
  return useMutation<SwarmBoostResult, Error, number>({
    mutationFn: (torrentId) =>
      apiClient.post(`/trackerboost/boost/${torrentId}`),
    onSuccess: (_, torrentId) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({
        queryKey: ["trackerboost", "check", torrentId],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({
        queryKey: ["torrents", torrentId, "trackers"],
      });
    },
  });
}

export function useBoostHash() {
  const queryClient = useQueryClient();
  return useMutation<
    SwarmBoostResult,
    Error,
    { infoHash: string; name?: string }
  >({
    mutationFn: (vars) => {
      const search = vars.name ? `?name=${encodeURIComponent(vars.name)}` : "";
      return apiClient.post(
        `/trackerboost/boost-hash/${vars.infoHash}${search}`,
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({
        queryKey: ["trackerboost", "check-hash", vars.infoHash],
      });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useInjectTrackerToTorrent() {
  const queryClient = useQueryClient();
  return useMutation<
    SwarmBoostResult,
    Error,
    { torrentId?: number; infoHash?: string; trackerUrl: string; force?: boolean }
  >({
    mutationFn: (payload) => apiClient.post("/trackerboost/inject", payload),
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      if (vars.torrentId) {
        queryClient.invalidateQueries({
          queryKey: ["trackerboost", "check", vars.torrentId],
        });
      }
      if (vars.infoHash) {
        queryClient.invalidateQueries({
          queryKey: ["trackerboost", "check-hash", vars.infoHash],
        });
      }
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useBoostAllTorrents() {
  const queryClient = useQueryClient();
  return useMutation<SwarmBoostResult[], Error, void>({
    mutationFn: () => apiClient.post("/trackerboost/boost-all"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useAddTrackerBoostTracker() {
  const queryClient = useQueryClient();
  return useMutation<TrackerBoostTracker, Error, { url: string }>({
    mutationFn: (payload) => apiClient.post("/trackerboost/trackers", payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useDeleteTrackerBoostTracker() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error, number>({
    mutationFn: (id) => apiClient.delete(`/trackerboost/trackers/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost"] });
    },
  });
}

export function useTrackerBoostLogs(
  limit = 150,
  category?: string,
  level?: string,
  refetchInterval?: number | false,
) {
  return useQuery<TrackerBoostLogEntry[]>({
    queryKey: ["trackerboost", "logs", limit, category, level],
    queryFn: () => {
      const params = new URLSearchParams();
      if (limit) params.set("limit", limit.toString());
      if (category && category !== "all") params.set("category", category);
      if (level && level !== "all") params.set("level", level);
      const queryStr = params.toString();
      return apiClient.get(
        `/trackerboost/logs${queryStr ? `?${queryStr}` : ""}`,
      );
    },
    refetchInterval: refetchInterval ?? 3000,
  });
}

export function useClearTrackerBoostLogs() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean }, Error>({
    mutationFn: () => apiClient.delete("/trackerboost/logs"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackerboost", "logs"] });
    },
  });
}

// Aliases for backward compatibility
export const useDownloadPlusPlusStatus = useTrackerBoostStatus;
export const useDownloadPlusPlusTrackers = useTrackerBoostTrackers;
export const useScanDownloadPlusPlusTrackers = useScanTrackerBoostTrackers;
export const useAddDownloadPlusPlusTracker = useAddTrackerBoostTracker;
export const useDeleteDownloadPlusPlusTracker = useDeleteTrackerBoostTracker;

// Tracker Metrics Hooks
export function useTrackerMetrics(refetchInterval: number | false = 4000) {
  return useQuery<TrackerMetric[]>({
    queryKey: ["trackermetrics"],
    queryFn: () => apiClient.get("/trackermetrics"),
    refetchInterval,
  });
}

export function useTrackerMetricsSummary(
  refetchInterval: number | false = 4000,
) {
  return useQuery<TrackerMetricsSummary>({
    queryKey: ["trackermetrics", "summary"],
    queryFn: () => apiClient.get("/trackermetrics/summary"),
    refetchInterval,
  });
}

export function useTrackerMetric(id: number) {
  return useQuery<TrackerMetric>({
    queryKey: ["trackermetrics", id],
    queryFn: () => apiClient.get(`/trackermetrics/${id}`),
    enabled: id > 0,
  });
}

export function useTrackerMetricHistory(id: number, hours = 24, limit = 500) {
  return useQuery<TrackerMetricSnapshot[]>({
    queryKey: ["trackermetrics", id, "history", hours, limit],
    queryFn: () =>
      apiClient.get(`/trackermetrics/${id}/history?hours=${hours}&limit=${limit}`),
    enabled: id > 0,
  });
}

export function useResetTrackerMetric() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; message: string }, Error, number>({
    mutationFn: (id: number) =>
      apiClient.post(`/trackermetrics/${id}/reset`, {}),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackermetrics"] });
    },
  });
}

export function useDeleteTrackerMetric() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; message: string }, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/trackermetrics/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["trackermetrics"] });
    },
  });
}

export function useTorrentMedia(torrentId?: number | null) {
  return useQuery<MediaMetadata>({
    queryKey: ["torrentMedia", torrentId],
    queryFn: () => apiClient.get(`/torrent/${torrentId}/media`),
    enabled: Boolean(torrentId && torrentId > 0),
  });
}

export function useAllMediaMetadata() {
  return useQuery<MediaMetadata[]>({
    queryKey: ["mediacover"],
    queryFn: () => apiClient.get("/mediacover"),
  });
}

export function useNotifications() {
  return useQuery<NotificationResource[]>({
    queryKey: ["notifications"],
    queryFn: () => apiClient.get("/notifications"),
  });
}

export function useCreateNotification() {
  const queryClient = useQueryClient();
  return useMutation<
    NotificationResource,
    Error,
    Partial<NotificationResource>
  >({
    mutationFn: (notification) =>
      apiClient.post("/notifications", notification),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useUpdateNotification() {
  const queryClient = useQueryClient();
  return useMutation<NotificationResource, Error, NotificationResource>({
    mutationFn: (notification) =>
      apiClient.put(`/notifications/${notification.id}`, notification),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useDeleteNotification() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/notifications/${id}`),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ["notifications"] }),
  });
}

export function useTestNotification() {
  return useMutation<NotificationTestResult, Error, number>({
    mutationFn: (id: number) => apiClient.post(`/notifications/${id}/test`),
  });
}

export function useTestDirectNotification() {
  return useMutation<
    NotificationTestResult,
    Error,
    Partial<NotificationResource>
  >({
    mutationFn: (notification) =>
      apiClient.post("/notifications/test", notification),
  });
}

export function useRssRules() {
  return useQuery<RssRule[]>({
    queryKey: ["rssrules"],
    queryFn: () => apiClient.get("/rssrules"),
  });
}

export function useCreateRssRule() {
  const queryClient = useQueryClient();
  return useMutation<RssRule, Error, Partial<RssRule>>({
    mutationFn: (rule) => apiClient.post("/rssrules", rule),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useUpdateRssRule() {
  const queryClient = useQueryClient();
  return useMutation<RssRule, Error, RssRule>({
    mutationFn: (rule) => apiClient.put(`/rssrules/${rule.id}`, rule),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useDeleteRssRule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => apiClient.delete(`/rssrules/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules"] }),
  });
}

export function useSyncRss() {
  const queryClient = useQueryClient();
  return useMutation<{ success: boolean; grabbedCount: number }, Error, void>({
    mutationFn: () => apiClient.post("/rssrules/sync"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
      queryClient.invalidateQueries({ queryKey: ["rssrules-history"] });
    },
  });
}

export function useRssGrabHistory(ruleId?: number, status?: string) {
  return useQuery<RssGrabHistory[]>({
    queryKey: ["rssrules-history", ruleId, status],
    queryFn: () => {
      const params = new URLSearchParams();
      if (ruleId) params.append("ruleId", ruleId.toString());
      if (status && status !== "all") params.append("status", status);
      const queryStr = params.toString();
      return apiClient.get(queryStr ? `/rssrules/history?${queryStr}` : "/rssrules/history");
    },
  });
}

export function useClearRssGrabHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete("/rssrules/history"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["rssrules-history"] }),
  });
}

export function useAutomationScripts() {
  return useQuery<AutomationScript[]>({
    queryKey: ["automation", "scripts"],
    queryFn: () => apiClient.get("/automation"),
  });
}

export function useAutomationScript(id: number) {
  return useQuery<AutomationScript>({
    queryKey: ["automation", "scripts", id],
    queryFn: () => apiClient.get(`/automation/${id}`),
    enabled: id > 0,
  });
}

export function useCreateAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, Partial<AutomationScript>>({
    mutationFn: (script) => apiClient.post("/automation", script),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useUpdateAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, AutomationScript>({
    mutationFn: (script) => apiClient.put("/automation", script),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useDeleteAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, number>({
    mutationFn: (id: number) => apiClient.delete(`/automation/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useRunAutomationScript() {
  const queryClient = useQueryClient();
  return useMutation<AutomationExecutionResult, Error, { id: number; torrentId?: number }>({
    mutationFn: ({ id, torrentId }) =>
      apiClient.post(`/automation/${id}/run${torrentId ? `?torrentId=${torrentId}` : ""}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
      queryClient.invalidateQueries({ queryKey: ["torrents"] });
    },
  });
}

export function useTestAutomationScript() {
  return useMutation<AutomationExecutionResult, Error, AutomationTestRequest>({
    mutationFn: (req) => apiClient.post("/automation/test", req),
  });
}

export function useAutomationMarketplace() {
  return useQuery<AutomationMarketplaceTemplate[]>({
    queryKey: ["automation", "marketplace"],
    queryFn: () => apiClient.get("/automation/marketplace"),
  });
}

export function useInstallMarketplaceTemplate() {
  const queryClient = useQueryClient();
  return useMutation<AutomationScript, Error, InstallMarketplaceTemplateRequest>({
    mutationFn: (req) => apiClient.post("/automation/marketplace/install", req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["automation", "scripts"] });
    },
  });
}

export function useTestCustomScript() {
  return useMutation<CustomScriptTestResult, Error, CustomScriptTestRequest>({
    mutationFn: (req) => apiClient.testCustomScript(req),
  });
}



export interface Torrent {
  id: number;
  name: string;
  infoHash: string;
  totalSize: number;
  pieceCount: number;
  pieceLength: number;
  comment: string | null;
  createdBy: string | null;
  creationDate: string | null;
  isPrivate: boolean;
  status: string;
  uploaded: number;
  downloaded: number;
  ratio: number;
  progress: number;
  seeders: number;
  leechers: number;
  trackerUrl: string | null;
  sourcePath: string | null;
  dateAdded: string;
  lastActive: string | null;
  priority: number;
  uploadLimit: number;
  downloadLimit: number;
  superSeeding: boolean;
  forceStart: boolean;
  label: string | null;
  sequentialDownload: boolean;
  announceInterval: number;
  nextUpdate: number;
  sessionUploaded: number;
  sessionDownloaded: number;
  smallTorrentLimit: number;
  threshold: number;
  uploadSpeed: number;
  downloadSpeed: number;
  active: boolean;
  availability: number;
  eta: number;
  sortOrder: number;
  forceCompleted: boolean;
  seedingTime: number;
}

export interface TorrentFileInfo {
  id: number;
  torrentId: number;
  path: string;
  size: number;
  pieceOffset: number;
  pieceCount: number;
}

export interface SeedingStats {
  activeTorrents: number;
  totalUploaded: number;
  totalDownloaded: number;
  averageRatio: number;
}

export interface SpeedSnapshot {
  timestamp: string;
  uploadSpeed: number;
  downloadSpeed: number;
  activeTorrents: number;
  totalPeers: number;
  averageRatio: number;
  totalUploaded: number;
  totalDownloaded: number;
}

export interface TorrentSpeedSnapshot {
  timestamp: string;
  uploadSpeed: number;
  downloadSpeed: number;
}

export interface SystemStatus {
  appName: string;
  version: string;
  buildTime: string;
  isDebug: boolean;
  isProduction: boolean;
  startTime: string;
  osName: string;
  osVersion: string;
  runtimeVersion: string;
  runtimeName: string;
  isDocker: boolean;
  branch: string;
  startupPath: string;
  appDataPath: string;
  databaseVersion: string;
  databaseMigration: string;
  uptimeSeconds: number;
}

export interface DiskSpaceInfo {
  path: string;
  label: string;
  freeSpace: number;
  totalSpace: number;
}

export interface HealthCheckResult {
  type: "Ok" | "Notice" | "Warning" | "Error";
  source: string;
  message: string | null;
}

export interface NetworkStatus {
  localIp: string;
  externalIp: string;
  upnpAvailable: boolean;
  proxyEnabled: boolean;
  portMappings: PortMapping[];
}

export interface PortMapping {
  internalPort: number;
  externalPort: number;
  protocol: string;
  description: string;
  isActive: boolean;
}

export interface Peer {
  id: number;
  ip: string;
  port: number;
  client: string;
  uploadSpeed: number;
  downloadSpeed: number;
  uploaded: number;
  downloaded: number;
  progress: number;
  flags: string;
}

export interface TrackerServerStats {
  totalTorrents: number;
  internalTorrents: number;
  totalPeers: number;
  totalAnnounces: number;
  totalScrapes: number;
  uptime: number;
}

export interface GeneralConfig {
  id: number;
  autoStart: boolean;
  themeStyle: string;
  colorScheme: string;
  watchFolderEnabled: boolean;
  watchFolderPath: string;
  watchFolderScanIntervalSeconds: number;
  watchFolderAutoStartTorrents: boolean;
  watchFolderDeleteAddedTorrents: boolean;
  port: number;
  bindAddress: string;
  urlBase: string;
  authenticationEnabled: boolean;
  apiKey: string;
}

export interface SeedingConfig {
  id: number;
  maxUploadSpeedKbps: number;
  maxDownloadSpeedKbps: number;
  alternativeSpeedEnabled: boolean;
  altUploadSpeedKbps: number;
  altDownloadSpeedKbps: number;
  globalSeedRatioLimit: number;
  uploadDistributionAlgorithm: string;
  uploadDistributionSpreadPercentage: number;
  uploadRedistributionMode: string;
  uploadCustomIntervalMinutes: number;
  uploadStoppedMinPercentage: number;
  uploadStoppedMaxPercentage: number;
  downloadDistributionAlgorithm: string;
  downloadDistributionSpreadPercentage: number;
  downloadRedistributionMode: string;
  downloadCustomIntervalMinutes: number;
  downloadStoppedMinPercentage: number;
  downloadStoppedMaxPercentage: number;
  speedVariationMin: number;
  speedVariationMax: number;
}

export interface NetworkConfig {
  id: number;
  listeningPort: number;
  upnpEnabled: boolean;
  maxGlobalConnections: number;
  maxPerTorrentConnections: number;
  maxUploadSlots: number;
  proxyType: string;
  proxyHost: string;
  proxyPort: number;
  proxyAuthEnabled: boolean;
  proxyUsername: string;
  proxyPassword: string;
}

export interface BitTorrentConfig {
  id: number;
  enableDht: boolean;
  enablePex: boolean;
  enableLpd: boolean;
  encryptionMode: string;
  bitTorrentUserAgent: string;
  peerIdPrefix: string;
  announceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  scrapeIntervalSeconds: number;
}

export interface PeerProtocolConfig {
  id: number;
  handshakeTimeoutSeconds: number;
  messageReadTimeoutSeconds: number;
  keepAliveIntervalSeconds: number;
  peerContactIntervalSeconds: number;
  udpTrackerTimeoutSeconds: number;
  httpTrackerTimeoutSeconds: number;
  peerRequestCount: number;
  seederUploadActivityProbability: number;
  peerIdleChance: number;
  peerDropoutProbability: number;
  connectionRotationPercentage: number;
}

export interface ProtocolsConfig {
  id: number;
  extensionUtMetadata: boolean;
  extensionUtPex: boolean;
  extensionLtDontHave: boolean;
  extensionFastExtension: boolean;
  utpEnabled: boolean;
  tcpFallback: boolean;
  transportConnectionTimeoutSeconds: number;
  pexInterval: number;
  pexMaxPeersPerMessage: number;
  multiTrackerEnabled: boolean;
  multiTrackerFailoverEnabled: boolean;
  announceToAllTiers: boolean;
  announceToAllInTier: boolean;
  failoverMaxConsecutiveFailures: number;
  failoverBackoffBaseSeconds: number;
  failoverMaxBackoffSeconds: number;
  dhtRoutingTableSize: number;
  dhtAnnouncementInterval: number;
  dhtBootstrapTimeout: number;
  dhtQueryTimeout: number;
  dhtMaxNodes: number;
  dhtBucketSize: number;
  dhtConcurrentQueries: number;
  dhtAutoBootstrap: boolean;
  dhtRateLimitEnabled: boolean;
  dhtMaxQueriesPerSecond: number;
}

export interface SimulationConfig {
  id: number;
  clientBehaviorEngineEnabled: boolean;
  primaryClient: string;
  behaviorVariation: number;
  clientProfileSwitching: boolean;
  switchClientProbability: number;
  trafficPatternProfile: string;
  realisticVariations: boolean;
  timeBasedPatterns: boolean;
  swarmIntelligenceEnabled: boolean;
  swarmAdaptationRate: number;
  swarmPeerAnalysisDepth: number;
}

export interface TrackerServerConfig {
  id: number;
  trackerServerEnabled: boolean;
  trackerHttpEnabled: boolean;
  trackerHttpPort: number;
  trackerUdpEnabled: boolean;
  trackerUdpPort: number;
  trackerBindAddress: string;
  trackerAnnounceInterval: number;
  trackerMaxPeersPerAnnounce: number;
  trackerEnableScrape: boolean;
  trackerPrivateMode: boolean;
  trackerLogAnnounces: boolean;
  trackerRateLimitPerMinute: number;
}

export interface SchedulerConfig {
  id: number;
  schedulerEnabled: boolean;
  schedulerStartHour: number;
  schedulerStartMinute: number;
  schedulerEndHour: number;
  schedulerEndMinute: number;
  schedulerMonday: boolean;
  schedulerTuesday: boolean;
  schedulerWednesday: boolean;
  schedulerThursday: boolean;
  schedulerFriday: boolean;
  schedulerSaturday: boolean;
  schedulerSunday: boolean;
}

export interface AdvancedConfig {
  id: number;
  logToFile: boolean;
  fileLogLevel: string;
  debugMode: boolean;
  uiRefreshRateSec: number;
}

export interface NotificationSettings {
  enabled: boolean;
  position: string;
  autoDismissSeconds: number;
  showInfo: boolean;
  showSuccess: boolean;
  showWarning: boolean;
  showError: boolean;
}

export interface ArrTestResult {
  success: boolean;
  message?: string;
}

export interface DownloadClientTestResult {
  success: boolean;
  message?: string;
}

export interface ArrConnection {
  id: number;
  name: string;
  arrType: string;
  url: string;
  apiKey: string;
  enable?: boolean;
  syncEnabled: boolean;
  enableAutomaticAdd: boolean;
  webhookEnabled: boolean;
  webhookHost: string;
  implementation: string;
  configContract: string;
}

export interface DownloadClientDefinition {
  id: number;
  name: string;
  clientType: string;
  host: string;
  port: number;
  useSsl: boolean;
  username: string;
  password: string;
  category: string;
  implementation: string;
  configContract: string;
  enable: boolean;
}

export interface DownloadClientRemoteItem {
  downloadId: string;
  title: string;
  infoHash: string;
  totalSize: number;
  remainingSize: number;
  progress: number;
  status: string;
  outputPath: string;
  category: string;
  isInLibrary: boolean;
  libraryTorrentId?: number | null;
}

export interface IndexerDefinition {
  id: number;
  name: string;
  indexerType: string;
  url: string;
  apiKey: string;
  apiPath: string;
  enableRss: boolean;
  enableSearch: boolean;
  categories: string;
  downloadClientId: number;
  implementation: string;
  configContract: string;
  enable: boolean;
}

export interface IndexerTestResult {
  success: boolean;
  message?: string;
}

export interface TrackerEntry {
  id: number;
  torrentId: number;
  url: string;
  tier: number;
  status: string;
  enabled: boolean;
  seeders: number;
  leechers: number;
  downloaded: number;
  totalAnnounces: number;
  successfulAnnounces: number;
  consecutiveFailures: number;
  lastResponseTime: number;
  averageResponseTime: number;
  announceInterval: number;
  minAnnounceInterval: number;
  lastAnnounce: string | null;
  lastScrape: string | null;
  nextAnnounce: string | null;
  errorMessage: string | null;
  warningMessage: string | null;
}

export interface TrackerServerTorrent {
  infoHash: string;
  name: string;
  peerCount: number;
  seeders: number;
  leechers: number;
  completed: number;
  uploaded: number;
  downloaded: number;
  isInternal: boolean;
  lastActivity: string | null;
}

export interface UpdateChanges {
  new: string[];
  fixed: string[];
}

export interface UpdateEntry {
  version: string;
  releaseDate: string;
  installed: boolean;
  latest: boolean;
  changes: UpdateChanges;
}

export interface LogFile {
  filename: string;
  lastWriteTime: string;
  size: number;
}

export interface Backup {
  id: number;
  name: string;
  size: number;
  time: string;
}

export interface PeerGraphNode {
  id: string;
  label: string;
  type: "center" | "torrent" | "peer";
  infoHash?: string;
  isEncrypted?: boolean;
}

export interface PeerGraphLink {
  source: string;
  target: string;
  type: string;
}

export interface PeerGraphData {
  nodes: PeerGraphNode[];
  links: PeerGraphLink[];
}

export interface SpeedScheduleEntry {
  id: number;
  name: string;
  days: number;
  startTime: string;
  endTime: string;
  maxUploadSpeed: number;
  maxDownloadSpeed: number;
  isEnabled: boolean;
  priority: number;
}

export interface SpeedLimits {
  maxUploadSpeed: number;
  maxDownloadSpeed: number;
  isScheduleActive: boolean;
  activeScheduleName: string;
}

export interface Tag {
  id: number;
  label: string;
}

export interface NetworkDiagnostics {
  localIp: string;
  externalIp: string;
  localAddresses: string[];
  upnpAvailable: boolean;
  proxyEnabled: boolean;
  portMappings: PortMapping[];
  listeningPort: number;
  activeConnections: number;
  uploadSlots: number;
  dhtEnabled: boolean;
  dhtNodeCount: number;
  encryptionMode: string;
  encryptedConnections: number;
  plaintextConnections: number;
  encryptionPercentage: number;
}

export interface PeerConnectionLogEntry {
  id: number;
  remoteIp: string;
  remotePort: number;
  infoHash: string;
  torrentName: string;
  peerId: string;
  isEncrypted: boolean;
  eventType: string;
  timestamp: string;
}

export interface TorrentEventLogEntry {
  id: number;
  torrentId: number;
  timeStamp: string;
  level: string;
  source: string;
  message: string;
}

export interface SyncResult {
  added: number;
  skipped: number;
  failed: number;
}

export interface MediaActor {
  name: string;
  character?: string | null;
  imageUrl?: string | null;
}

export interface MediaMetadata {
  mediaType?: string | null;
  mediaId?: number | null;
  title?: string | null;
  year?: number | null;
  overview?: string | null;
  posterUrl?: string | null;
  fanartUrl?: string | null;
  bannerUrl?: string | null;
  genres?: string[];
  actors?: MediaActor[];
  studioOrNetwork?: string | null;
  rating?: number | null;
  imdbId?: string | null;
  tmdbId?: number | null;
  tvdbId?: number | null;
}

export interface DownloadHistoryEntry {
  id: number;
  torrentId: number | null;
  title: string;
  infoHash: string;
  totalSize: number;
  dateAdded: string;
  dateCompleted: string | null;
  dateRemoved: string | null;
  uploaded: number;
  downloaded: number;
  ratio: number;
  seedingTime: number;
  primaryTracker: string | null;
  indexerName: string | null;
  source: string | null;
  magnetUrl: string | null;
  downloadUrl: string | null;
  status: string;
  removalReason: string | null;
  dataJson: string | null;
  metadata?: MediaMetadata | null;
}

export interface ReleaseInfo {
  guid?: string;
  title: string;
  indexerId?: number;
  indexer?: string;
  size: number;
  seeders?: number | null;
  leechers?: number | null;
  publishDate?: string | null;
  downloadUrl?: string | null;
  magnetUrl?: string | null;
  infoHash?: string | null;
  categories?: string[];
  protocol?: string;
}

export interface DownloadReleaseRequest {
  title?: string;
  downloadUrl?: string;
  magnetUrl?: string;
  infoHash?: string;
  indexerId?: number;
  indexerName?: string;
}

export type TrackerProtocol = "Udp" | "Http" | "Https" | number;
export type TrackerHealthStatus = "Untested" | "Alive" | "Slow" | "Offline" | number;
export type TrackerSourceType = "PublicList" | "Prowlarr" | "ReleaseMagnet" | "Manual" | number;

export interface DownloadPlusPlusTracker {
  id: number;
  url: string;
  host: string;
  port: number;
  protocol: TrackerProtocol;
  status: TrackerHealthStatus;
  source: TrackerSourceType;
  sourceName: string;
  latencyMs: number;
  lastScraped: string | null;
  lastSuccess: string | null;
  successfulScrapes: number;
  failedScrapes: number;
  totalSwarmsFound: number;
  enabled: boolean;
}

export interface SwarmBoostResult {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  boosted: boolean;
  addedTrackersCount: number;
  addedTrackers: string[];
  totalSeedersFound: number;
  totalLeechersFound: number;
  message: string;
}

export interface DownloadPlusPlusStatusSummary {
  totalTrackersMonitored: number;
  aliveTrackersCount: number;
  slowTrackersCount: number;
  offlineTrackersCount: number;
  untestedTrackersCount: number;
  prowlarrTrackersCount: number;
  publicListTrackersCount: number;
  torrentsBoostedCount: number;
  extraTrackersInjectedCount: number;
  lastScanTime: string | null;
  lastProwlarrHarvestTime: string | null;
}

export interface TorrentTrackerDetection {
  trackerId: number;
  trackerUrl: string;
  trackerHost: string;
  protocol: TrackerProtocol;
  source: TrackerSourceType;
  sourceName: string;
  isAttached: boolean;
  isDetected: boolean;
  seeders: number;
  leechers: number;
  latencyMs: number;
  healthStatus: TrackerHealthStatus;
  detectionStatus: string;
}

export interface TorrentTrackerInspectionResult {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  totalTrackersChecked: number;
  attachedTrackersCount: number;
  detectedTrackersCount: number;
  detections: TorrentTrackerDetection[];
}



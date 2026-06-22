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
  seeders: number;
  leechers: number;
  trackerUrl: string | null;
  sourcePath: string | null;
  dateAdded: string;
  lastActive: string | null;
}

export interface SeedingStats {
  activeTorrents: number;
  totalUploaded: number;
  totalDownloaded: number;
  averageRatio: number;
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
}

export interface HealthCheckResult {
  type: 'Ok' | 'Notice' | 'Warning' | 'Error';
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
  totalPeers: number;
  totalAnnounces: number;
  totalScrapes: number;
  uptime: number;
}

export interface GeneralConfig {
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
}

export interface NetworkConfig {
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
  logToFile: boolean;
  fileLogLevel: string;
  debugMode: boolean;
  uiRefreshRateSec: number;
}

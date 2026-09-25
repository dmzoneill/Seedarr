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
  category?: string | null;
  savePath?: string | null;
  downloadPath?: string | null;
  sequentialDownload: boolean;
  firstLastPiecePrio?: boolean;
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
  posterUrl?: string | null;
  fanartUrl?: string | null;
  bannerUrl?: string | null;
  mediaTitle?: string | null;
  year?: number | null;
  overview?: string | null;
  rating?: number | null;
  genres?: string[];
  trackers?: string[];
  tagIds?: number[];
  source?: string | null;
  isVpnPaused?: boolean;
  errorMessage?: string | null;
  magnetLink?: string | null;
  resolution?: string | null;
  hdrFormat?: string | null;
  videoCodec?: string | null;
  audioCodec?: string | null;
  audioChannels?: string | null;
}

export interface TrackerMetric {
  id: number;
  trackerUrl: string;
  host: string;
  domain: string;
  protocol: string;
  port: number;
  status: string;
  firstSeen: string;
  lastAnnounce: string | null;
  lastScrape: string | null;
  lastSuccess: string | null;
  lastErrorTime: string | null;
  lastErrorMessage: string | null;
  totalAnnounces: number;
  successfulAnnounces: number;
  failedAnnounces: number;
  announceSuccessRate: number;
  totalScrapes: number;
  successfulScrapes: number;
  failedScrapes: number;
  totalUploaded: number;
  totalDownloaded: number;
  ratio: number;
  totalLeft: number;
  sessionUploaded: number;
  sessionDownloaded: number;
  totalTorrentsTracked: number;
  lastSeeders: number;
  lastLeechers: number;
  lastPeers: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  lastResponseTimeMs: number;
  minResponseTimeMs: number;
  maxResponseTimeMs: number;
  consecutiveFailures: number;
  latencyP50Ms?: number;
  latencyP95Ms?: number;
  latencyP99Ms?: number;
  p50LatencyMs?: number;
  p95LatencyMs?: number;
  p99LatencyMs?: number;
}

export interface TrackerMetricSnapshot {
  id: number;
  trackerMetricId: number;
  trackerUrl: string;
  timestamp: string;
  responseTimeMs: number;
  uploaded: number;
  downloaded: number;
  seeders: number;
  leechers: number;
  peersDiscovered: number;
  isSuccess: boolean;
  operation: string;
}

export interface HourlyTrafficPoint {
  timeLabel: string;
  timestamp: string;
  uploaded: number;
  downloaded: number;
  announces: number;
  peersDiscovered: number;
  avgLatencyMs: number;
}

export interface TrackerMetricItemSummary {
  id: number;
  trackerUrl: string;
  domain: string;
  protocol: string;
  status: string;
  totalUploaded: number;
  totalDownloaded: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  successRate: number;
}

export interface TrackerMetricsSummary {
  totalTrackers: number;
  healthyTrackers: number;
  degradedTrackers: number;
  offlineTrackers: number;
  totalUploaded: number;
  totalDownloaded: number;
  globalRatio: number;
  totalAnnounces: number;
  successfulAnnounces: number;
  failedAnnounces: number;
  announceSuccessRate: number;
  totalScrapes: number;
  successfulScrapes: number;
  totalPeersDiscovered: number;
  avgResponseTimeMs: number;
  latencyP50Ms?: number;
  latencyP95Ms?: number;
  latencyP99Ms?: number;
  p50LatencyMs?: number;
  p95LatencyMs?: number;
  p99LatencyMs?: number;
  protocolDistribution: Record<string, number>;
  healthDistribution: Record<string, number>;
  topUploadTrackers: TrackerMetricItemSummary[];
  topPeerTrackers: TrackerMetricItemSummary[];
  hourlyHistory: HourlyTrafficPoint[];
}

export interface TorrentFileInfo {
  id: number;
  torrentId: number;
  path: string;
  size: number;
  pieceOffset: number;
  pieceCount: number;
  isPaddingFile?: boolean;
  wanted?: boolean;
  priority?: number;
  bytesCompleted?: number;
  progress?: number;
}

export interface SubtitleTrack {
  trackId: number;
  fileId?: number;
  title: string;
  language: string;
  twoLetterCode: string;
  format: string;
  path: string;
  isExternal: boolean;
  isForced: boolean;
  isHearingImpaired: boolean;
  isDefault: boolean;
  url: string;
}

export interface PieceMapSpan {
  count: number;
  state: number;
}

export interface PieceMapResource {
  torrentId: number;
  infoHash: string;
  totalPieces: number;
  pieceLength: number;
  spans: PieceMapSpan[];
  rleSpans: [number, number][];
  rarity?: number[];
  raritySpans?: [number, number][];
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
  instanceUuid?: string;
  buildTime: string;
  isDebug: boolean;
  isProduction: boolean;
  startTime: string;
  osName: string;
  osVersion: string;
  osArchitecture?: string;
  commitHash?: string;
  runtimeVersion: string;
  runtimeName: string;
  isDocker: boolean;
  branch: string;
  startupPath: string;
  AppDataPath?: string;
  appDataPath: string;
  databaseVersion: string;
  databaseMigration: string;
  uptimeSeconds: number;
  gcGen0Collections?: number;
  gcGen1Collections?: number;
  gcGen2Collections?: number;
  gcTotalAllocatedBytes?: number;
  gcHeapSizeBytes?: number;
  gcPauseTimePercentage?: number;
  openFileDescriptors?: number;
  maxFileDescriptors?: number;
  fileDescriptorUsagePercentage?: number;
  cpuUsagePercentage?: number;
  processorCount?: number;
  threadCount?: number;
  availableWorkerThreads?: number;
  availableCompletionPortThreads?: number;
  workingSetBytes?: number;
  gcTotalMemoryBytes?: number;
  containerMemoryLimitBytes?: number;
  memoryUsagePercentage?: number;
}

export interface DiskSpaceInfo {
  path: string;
  label: string;
  freeSpace: number;
  totalSpace: number;
  fileSystemType?: string;
  isReadOnly?: boolean;
}

export interface HealthCheckResult {
  type:
    | "Ok"
    | "Notice"
    | "Warning"
    | "Error"
    | "ok"
    | "notice"
    | "warning"
    | "error";
  source: string;
  message: string | null;
}

export interface NetworkStatus {
  localIp: string;
  boundInterface?: string;
  boundIp?: string;
  physicalIp?: string;
  externalIp: string;
  isVpnKillSwitchActive?: boolean;
  upnpAvailable: boolean;
  proxyEnabled: boolean;
  portMappings: PortMapping[];
}

export interface NetworkInterfaceResource {
  name: string;
  description: string;
  type: string; // Physical, Wireless, Tunnel, Virtual
  status: string; // Up, Down, Testing
  addresses: string[];
  isVpn: boolean;
}

export interface PortMapping {
  internalPort: number;
  externalPort: number;
  protocol: string;
  description: string;
  isActive: boolean;
  errorMessage?: string | null;
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
  countryCode?: string;
  countryName?: string;
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
  instanceUuid?: string;
  autoStart: boolean;
  themeStyle: string;
  colorScheme: string;
  uiTheme?: string;
  uiAccent?: string;
  uiLanguage?: string;
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
  csrfProtectionEnabled?: boolean;
  hostHeaderValidationEnabled?: boolean;
  allowedHosts?: string;
  allowedOrigins?: string;
  terminalAccessEnabled?: boolean;
  enableSsl?: boolean;
  sslPort?: number;
  sslCertPath?: string;
  sslKeyPath?: string;
  sslCertPassword?: string;
  redirectHttpToHttps?: boolean;
  tmdbApiKey?: string;
}

export interface SslTestRequest {
  enableSsl: boolean;
  sslPort: number;
  sslCertPath?: string;
  sslKeyPath?: string;
  sslCertPassword?: string;
  bindAddress?: string;
}

export interface SslCertificateValidationResult {
  isValid: boolean;
  subject: string;
  issuer: string;
  validFrom: string;
  validTo: string;
  thumbprint: string;
  hasPrivateKey: boolean;
  subjectAlternativeNames: string[];
  handshakeSucceeded: boolean;
  message: string;
}

export enum IdentityProviderType {
  Oidc = 0,
  Saml = 1,
  Social = 2,
  ForwardAuth = 3,
}

export interface IdentityProviderDefinition {
  id: number;
  providerId: string;
  name: string;
  providerType: IdentityProviderType;
  isEnabled: boolean;
  clientId?: string | null;
  clientSecret?: string | null;
  issuerUrl?: string | null;
  metadataUrl?: string | null;
  scopes?: string | null;
  certificate?: string | null;
  roleMappingRules?: string | null;
  iconUrl?: string | null;
  buttonText?: string | null;
}

export interface ApiKeyResource {
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
  seedGoalReachedAction?: string;
  preserveSeedingOnArrDelete?: boolean;
  maxActiveDownloads?: number;
  maxActiveSeeds?: number;
  maxActiveTorrents?: number;
  ignoreSlowTorrents?: boolean;
  slowTorrentThresholdKbps?: number;
}

export interface NetworkConfig {
  id: number;
  listeningPort: number;
  upnpEnabled: boolean;
  enableIPv6: boolean;
  bindInterface: string;
  enableVpnKillSwitch: boolean;
  vpnStabilizationDelaySeconds?: number;
  maxGlobalConnections: number;
  maxPerTorrentConnections: number;
  maxUploadSlots: number;
  maxConnectionsPerIp: number;
  maximumHalfOpenConnections: number;
  anonymousMode: boolean;
  forceProxy: boolean;
  peerDscp: number;
  peerTos: number;
  proxyType: string;
  proxyHost: string;
  proxyPort: number;
  proxyAuthEnabled: boolean;
  proxyUsername: string;
  proxyPassword: string;
}

export interface Category {
  id: number;
  name: string;
  savePath?: string;
  defaultUploadLimit?: number;
  defaultDownloadLimit?: number;
  targetRatio?: number;
  targetSeedTimeMinutes?: number;
  autoStop?: boolean;
  isDefault?: boolean;
  maxActiveDownloads?: number | null;
  maxActiveUploads?: number | null;
  reservedDownloadSlots?: number;
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

  // Swarm & Scripts
  onDownloadCompleteScript?: string;
  onSeedGoalReachedScript?: string;
  scriptTorrentDoneFilename?: string;
  scriptTorrentAddedFilename?: string;
  scriptTorrentDoneSeedingFilename?: string;
  customScriptTimeoutSeconds?: number;
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

export interface NotificationTestResult {
  success: boolean;
  message?: string;
}

export interface NotificationResource {
  id: number;
  name: string;
  implementation: string;
  configContract?: string;
  settings: string;
  enable: boolean;
  onGrab: boolean;
  onDownloadComplete: boolean;
  onMediaInspected: boolean;
  onExtractComplete: boolean;
  onSeedGoalReached: boolean;
  onTorrentDeleted: boolean;
  onHealthIssue: boolean;
  onHealthRestored: boolean;
  onManualInteractionRequired: boolean;
  onApplicationUpdate: boolean;
  tags?: number[];
  categories?: string[];
}

export type NotificationDefinition = NotificationResource;

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
  syncIntervalMinutes?: number;
  syncEnabled: boolean;
  enableAutomaticAdd: boolean;
  webhookEnabled: boolean;
  webhookHost: string;
  implementation: string;
  configContract: string;
  acceptInvalidCertificates?: boolean;
}

export interface DownloadClientDefinition {
  id: number;
  name: string;
  clientType: string;
  host: string;
  port: number;
  useSsl: boolean;
  urlBase?: string;
  username: string;
  password: string;
  category: string;
  implementation: string;
  configContract: string;
  enable: boolean;
  tags?: number[];
  isOnline?: boolean | null;
  version?: string | null;
  lastSyncTime?: string | null;
  lastErrorMessage?: string | null;
  consecutiveFailures?: number;
  backoffUntil?: string | null;
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
  isPrivate?: boolean;
  isInLibrary: boolean;
  libraryTorrentId?: number | null;
  downloadSpeed?: number | null;
  uploadSpeed?: number | null;
  clientId?: number;
  clientName?: string;
}

export interface TorznabSubcategory {
  id: number;
  name: string;
  description?: string;
}

export interface TorznabCategory {
  id: number;
  name: string;
  description?: string;
  subcategories?: TorznabSubcategory[];
}

export interface TorznabSearchCapability {
  available: boolean;
  supportedParams: string[];
}

export interface TorznabCapabilities {
  serverTitle?: string;
  serverVersion?: string;
  categories?: TorznabCategory[];
  searching?: Record<string, TorznabSearchCapability>;
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
  tags?: number[];
  implementation: string;
  configContract: string;
  enable: boolean;
  prowlarrIndexerId?: number | null;
  capabilities?: TorznabCapabilities | null;
}

export interface ProwlarrSyncResult {
  success: boolean;
  message?: string;
  added: number;
  updated: number;
  removed: number;
  totalFound: number;
  syncedIndexers?: string[];
  errors?: string[];
}

export interface IndexerTestResult {
  success: boolean;
  message?: string;
  responseTimeMs?: number;
  statusCode?: number | null;
  capabilities?: TorznabCapabilities | null;
}

export interface RssRule {
  id: number;
  name: string;
  isEnabled: boolean;
  mustContain: string;
  mustNotContain: string;
  minSeeders: number;
  allowUnknownSeeders?: boolean;
  priority?: number;
  minSizeBytes: number;
  maxSizeBytes: number;
  maxAgeDays?: number;
  freeleechOnly: boolean;
  categoryId: number;
  indexerIds: number[];
  tags?: number[];
  tagIds?: number[];
  allowedResolutions?: string[];
  allowedSources?: string[];
  allowedCodecs?: string[];
  savePath?: string;
  sequentialDownload?: boolean;
  initialStatus?: string;
}

export interface RssGrabHistory {
  id: number;
  releaseTitle: string;
  indexerName?: string;
  ruleId?: number;
  ruleName?: string;
  infoHash?: string;
  size: number;
  grabTimestamp: string;
  status: string;
  errorMessage?: string;
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
  lastAnnouncedUploaded: number;
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
  posterUrl?: string | null;
  fanartUrl?: string | null;
  mediaTitle?: string | null;
  year?: number | null;
  rating?: number | null;
  genres?: string[];
  source?: string | null;
  totalSize?: number;
  ratio?: number;
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
  isContainerized?: boolean;
}

export type UpdateInstallStage =
  | "Idle"
  | "Downloading"
  | "Verifying"
  | "Extracting"
  | "Installing"
  | "RestartRequired"
  | "Failed";

export interface UpdateInstallProgress {
  stage: UpdateInstallStage;
  percentage: number;
  errorMessage?: string | null;
  targetVersion?: string | null;
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
  isActive?: boolean;
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
  color?: string;
  uploadLimitKbps?: number;
  downloadLimitKbps?: number;
  minSeedRatio?: number;
  minSeedTimeSeconds?: number;
  torrentCount?: number;
}

export interface NetworkDiagnostics {
  localIp: string;
  boundInterface?: string;
  boundIp?: string;
  physicalIp?: string;
  externalIp: string;
  isVpnKillSwitchActive?: boolean;
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

export interface PortTestResult {
  port: number;
  externalIp: string;
  isOpen: boolean;
  errorMessage?: string | null;
  responseTime: string;
  responseTimeMs?: number;
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
  updated?: number;
  failed: number;
}

export interface BatchImportItemResult {
  infoHash: string;
  title: string;
  success: boolean;
  errorMessage?: string | null;
}

export interface BatchImportResponse {
  added: number;
  skipped: number;
  failed: number;
  items: BatchImportItemResult[];
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
  musicBrainzId?: string | null;
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
  savePath?: string | null;
  category?: string | null;
  downloadClientId?: number | null;
  sourcePath?: string | null;
  isPrivate?: boolean;
}

export interface DownloadHistoryResponse extends Array<DownloadHistoryEntry> {
  records?: DownloadHistoryEntry[];
  totalCount?: number;
  page?: number;
  pageSize?: number;
  totalPages?: number;
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
  imdbId?: string | null;
  tmdbId?: number | null;
  tvdbId?: number | null;
  resolution?: string | null;
  videoCodec?: string | null;
  audioCodec?: string | null;
  minimumRatio?: number | null;
  minimumSeedTime?: number | null;
}

export interface DownloadReleaseRequest {
  title?: string;
  downloadUrl?: string;
  magnetUrl?: string;
  infoHash?: string;
  indexerId?: number;
  indexerName?: string;
  imdbId?: string | null;
  tmdbId?: number | null;
  tvdbId?: number | null;
  minimumRatio?: number | null;
  minimumSeedTime?: number | null;
}

export type TrackerProtocol = "Udp" | "Http" | "Https" | number;
export type TrackerHealthStatus =
  "Untested" | "Alive" | "Slow" | "Offline" | number;
export type TrackerSourceType =
  | "PublicList"
  | "Prowlarr"
  | "ReleaseMagnet"
  | "Manual"
  | "ActiveTorrent"
  | number;

export interface TrackerBoostTracker {
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
  totalVerifiedTorrents?: number;
  enabled: boolean;
}

export type DownloadPlusPlusTracker = TrackerBoostTracker;

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
  verifiedCandidateTrackersCount?: number;
  skippedTrackersCount?: number;
  message: string;
}

export interface TrackerBoostStatusSummary {
  totalTrackersMonitored: number;
  aliveTrackersCount: number;
  slowTrackersCount: number;
  offlineTrackersCount: number;
  untestedTrackersCount: number;
  prowlarrTrackersCount: number;
  publicListTrackersCount: number;
  activeTorrentTrackersCount: number;
  torrentsBoostedCount: number;
  extraTrackersInjectedCount: number;
  totalVerifiedMatchesCount: number;
  autoBoostEnabled: boolean;
  autoHarvestEnabled: boolean;
  lastScanTime: string | null;
  lastHarvestTime: string | null;
  lastProwlarrHarvestTime: string | null;
  lastAutoBoostTime: string | null;
}

export type DownloadPlusPlusStatusSummary = TrackerBoostStatusSummary;

export interface TorrentTrackerDetection {
  trackerId: number;
  trackerUrl: string;
  trackerHost: string;
  protocol: TrackerProtocol;
  source: TrackerSourceType;
  sourceName: string;
  isAttached: boolean;
  isDetected: boolean;
  isVerified: boolean;
  seeders: number;
  leechers: number;
  downloaded?: number;
  latencyMs: number;
  healthStatus: TrackerHealthStatus;
  detectionStatus: string;
}

export interface TorrentTrackerInspectionResult {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  isBoosted?: boolean;
  boostedAt?: string | null;
  injectedTrackersCount?: number;
  totalTrackersChecked: number;
  attachedTrackersCount: number;
  detectedTrackersCount: number;
  verifiedTrackersCount: number;
  detections: TorrentTrackerDetection[];
}

export interface TrackerBoostSettings {
  autoBoostEnabled: boolean;
  autoHarvestEnabled: boolean;
  intervalMinutes: number;
  maxTrackersPerTorrent: number;
  onlyVerified: boolean;
}

export interface TorrentMatrixItem {
  torrentId: number;
  torrentName: string;
  infoHash: string;
  isPrivate: boolean;
  isBoosted: boolean;
  attachedTrackersCount: number;
  verifiedTrackersCount: number;
  trackers: TorrentTrackerDetection[];
}

export interface TrackerMatrixItem {
  trackerId: number;
  trackerUrl: string;
  host: string;
  protocol: TrackerProtocol;
  status: TrackerHealthStatus;
  latencyMs: number;
  registeredTorrentsCount: number;
  registeredTorrentNames: string[];
}

export interface TrackerCrossMatrixResult {
  torrents: TorrentMatrixItem[];
  trackers: TrackerMatrixItem[];
}

export interface TrackerBoostLogEntry {
  id: number;
  timestamp: string;
  level: "Info" | "Success" | "Warn" | "Error" | "Debug" | string;
  category:
    | "General"
    | "Scrape"
    | "Health"
    | "Discovery"
    | "Inject"
    | "Announce"
    | "Cycle"
    | string;
  trackerUrl: string;
  infoHash: string;
  message: string;
}

export type AutomationTrigger =
  | "TorrentAdded"
  | "TorrentCompleted"
  | "RatioReached"
  | "TorrentError"
  | "Manual"
  | "Scheduled"
  | "TorrentDeleted"
  | "TorrentStatusChanged"
  | "MediaEnriched"
  | "ArchiveExtracted"
  | "ExtractionFailed"
  | "VpnDisconnected"
  | "VpnRestored"
  | "HealthRestored"
  | "CategoryChanged"
  | "ApplicationStarted"
  | "TorrentStarted"
  | "TorrentPaused"
  | "TorrentStalled"
  | "TorrentStallResolved"
  | "SeedingTimeReached"
  | "HashCheckCompleted"
  | "ProgressMilestone"
  | "SpeedThresholdExceeded"
  | "SpeedThresholdDropped"
  | "BandwidthQuotaApproaching"
  | "PortForwardingFailed"
  | "PeerBanned"
  | "TrackerUnreachable"
  | "TrackerBoostApplied"
  | "DiskSpaceLow"
  | "DiskSpaceCritical"
  | "FileMoveFailed"
  | "MediaInspectionFailed"
  | "ArrImportCompleted"
  | "ApplicationUpdated"
  | "BackupCompleted"
  | "BackupFailed"
  | "TaskFailed"
  | number;

export type AutomationLanguage = "JavaScript" | "Yaml" | number;

export interface AutomationScript {
  id: number;
  name: string;
  description?: string | null;
  trigger: AutomationTrigger;
  language: AutomationLanguage;
  code: string;
  inputsJson?: string | null;
  isEnabled: boolean;
  targetCategories: string[];
  targetTagIds: number[];
  createdAt: string;
  lastExecutedAt?: string | null;
  lastExecutionStatus?: string | null;
  lastExecutionLog?: string | null;
}

export interface AutomationExecutionResult {
  success: boolean;
  outputLog?: string | null;
  error?: string | null;
  executionTimeMs: number;
  tagsToAdd: string[];
  tagsToRemove: string[];
  newCategory?: string | null;
  shouldPause: boolean;
  shouldResume: boolean;
  shouldRemove: boolean;
  deleteDataOnRemove: boolean;
  shouldRecheck: boolean;
  shouldReannounce: boolean;
  shouldBoostTracker: boolean;
  newUploadLimitKbps?: number | null;
  newDownloadLimitKbps?: number | null;
}

export interface TemplateInputField {
  key: string;
  label: string;
  type: "text" | "password" | "number" | "select" | "boolean" | "url" | string;
  defaultValue: string;
  description: string;
  required: boolean;
  regexPattern?: string | null;
  allowedValues?: string[] | null;
  maxLength?: number;
}

export interface AutomationMarketplaceTemplate {
  id: string;
  name: string;
  description: string;
  author: string;
  version: string;
  category: string;
  trigger: AutomationTrigger;
  language: AutomationLanguage;
  code: string;
  defaultInputs: Record<string, string>;
  inputFields: TemplateInputField[];
  sha256?: string;
  signature?: string;
  publisher?: string;
  isVerified?: boolean;
  capabilities?: string[];
}

export interface AutomationTestRequest {
  script: Partial<AutomationScript>;
  torrentId?: number | null;
  customInputs?: Record<string, unknown>;
}

export interface InstallMarketplaceTemplateRequest {
  templateId: string;
  customName?: string;
  customInputs?: Record<string, string>;
}

export interface FileSystemEntryResource {
  name: string;
  path: string;
  type: "folder" | "file" | "drive" | "symlink" | string;
  size?: number | null;
  freeSpace?: number | null;
  lastModified?: string | null;
}

export interface FileSystemResource {
  parent?: string | null;
  current?: string;
  directories: FileSystemEntryResource[];
  files?: FileSystemEntryResource[];
  totalDirectories?: number;
  totalFiles?: number;
  isTruncated?: boolean;
}

export interface TorrentCreationRequest {
  path: string;
  name?: string;
  comment?: string;
  createdBy?: string;
  isPrivate?: boolean;
  pieceLength?: number;
  trackers?: string[];
  trackerTiers?: string[][];
  webSeeds?: string[];
  source?: string;
  outputPath?: string;
}

export interface TorrentCreationResult {
  success: boolean;
  errorMessage?: string;
  outputPath?: string;
  infoHash?: string;
  totalSize: number;
  pieceCount: number;
  pieceLength: number;
}

export interface CurrentUser {
  id?: number;
  identifier?: string;
  username: string;
  email?: string | null;
  displayName?: string | null;
  roles: string[];
  avatarUrl?: string | null;
  isAuthenticated: boolean;
  requiresPassword?: boolean;
  authenticationEnabled?: boolean;
  returnUrl?: string;
}

export interface AuthProvider {
  id: number;
  providerId: string;
  name: string;
  providerType: IdentityProviderType;
  iconUrl?: string | null;
  buttonText?: string | null;
  loginUrl: string;
}

export interface LoginRequest {
  username?: string;
  password?: string;
  rememberMe?: boolean;
  returnUrl?: string;
}

export interface BulkTorrentActionResource {
  torrentIds: number[];
  action: string;
  deleteFiles?: boolean;
  categoryId?: number;
  category?: string;
  tagIds?: number[];
  priority?: number;
  uploadLimit?: number;
  downloadLimit?: number;
}

export interface BulkActionResult {
  successCount: number;
  failedCount: number;
  errors: string[];
  succeededIds: number[];
  failedIds: Record<number, string>;
}

export interface CustomScriptTestRequest {
  scriptPath: string;
  arguments?: string | null;
  eventType?: string;
}

export interface CustomScriptTestResult {
  success: boolean;
  exitCode: number;
  stdout: string;
  stderr: string;
  executionTimeMs: number;
  timedOut: boolean;
  resolvedInterpreter?: string;
  workingDirectory?: string;
}

export interface RemotePathMapping {
  id?: number;
  host: string;
  remotePath: string;
  localPath: string;
}

export interface RemotePathMappingTestResult {
  inputPath: string;
  mappedPath: string;
  ruleApplied: boolean;
  matchedRuleId?: number | null;
  matchedRuleHost?: string | null;
  matchedRemotePrefix?: string | null;
  matchedLocalPrefix?: string | null;
  localPathExists: boolean;
}

export interface RemotePathMappingTestRequest {
  host: string;
  path: string;
  direction?: string;
}

export interface SetupStatus {
  isSetupCompleted: boolean;
  isAuthEnabled: boolean;
  hasAdminUser: boolean;
}

export interface SetupCompleteRequest {
  username?: string;
  password?: string;
}

export interface ScheduledTaskResource {
  id?: number;
  typeName: string;
  name?: string;
  interval: number;
  isEnabled?: boolean;
  lastExecution: string | null;
  lastStartTime: string | null;
  lastDuration: string | null;
  nextExecution: string | null;
  isRunning?: boolean;
  lastStatus?: "None" | "Success" | "Failed" | string;
  lastErrorMessage?: string | null;
}

export interface UpdateScheduledTaskRequest {
  interval: number;
  isEnabled: boolean;
}

export interface PackageImportTorrentSummary {
  id: number;
  name: string;
  infoHash: string;
  category?: string;
  tags?: string[];
  totalSize: number;
  isDuplicate: boolean;
  status?: string;
  savePath?: string;
}

export interface PackageImportResult {
  success: boolean;
  importedTorrentsCount: number;
  skippedDuplicatesCount: number;
  torrents: PackageImportTorrentSummary[];
  skippedDuplicates: string[];
  extractedFiles: string[];
  totalBytesExtracted: number;
  message?: string;
}

export interface DiskMountPointMetrics {
  mountPoint: string;
  driveType: string;
  totalSpaceBytes: number;
  freeSpaceBytes: number;
  usedSpaceBytes: number;
  usedPercent: number;
}

export interface HostProcessResourceMetrics {
  cpuProcessPercent: number;
  cpuCores: number;
  workingSetBytes: number;
  privateMemoryBytes: number;
  virtualMemoryBytes: number;
  managedHeapBytes: number;
  gcGen0Collections: number;
  gcGen1Collections: number;
  gcGen2Collections: number;
  threadCount: number;
  threadPoolWorkerThreads: number;
  threadPoolCompletionPortThreads: number;
  handleCount: number;
  uptimeSeconds: number;
  diskDrives: DiskMountPointMetrics[];
  timestamp: string;
}

export interface TorrentEngineMetrics {
  engineId: string;
  displayName: string;
  version: string;
  isRunning: boolean;
  activeTorrents: number;
  downloadingTorrents: number;
  seedingTorrents: number;
  pausedTorrents: number;
  totalDownloadSpeed: number;
  totalUploadSpeed: number;
  totalProtocolDownloadSpeed: number;
  totalProtocolUploadSpeed: number;
  totalDataDownloaded: number;
  totalDataUploaded: number;
  totalProtocolDownloaded: number;
  totalProtocolUploaded: number;
  protocolOverheadPercentage: number;
  openConnections: number;
  halfOpenConnections: number;
  maxConnections: number;
  connectedSeeds: number;
  connectedLeechers: number;
  totalSwarmPeers: number;
  dhtNodeCount: number;
  dhtState: string;
  diskCacheBytesAllocated: number;
  diskCacheCapacityBytes: number;
  diskCacheHitRatio: number;
  diskCacheHits: number;
  diskCacheMisses: number;
  diskPendingWrites: number;
  diskPendingReads: number;
  diskTotalBytesWritten: number;
  diskTotalBytesRead: number;
  diskWriteRate: number;
  diskReadRate: number;
  piecesHashedPerSec: number;
  hashFailsTotal: number;
  encryptedConnectionsCount: number;
  plaintextConnectionsCount: number;
  utpConnectionsCount: number;
  tcpConnectionsCount: number;
  timestamp: string;
}

export interface TorrentResourceMetrics {
  torrentId: number;
  infoHash: string;
  name: string;
  category?: string | null;
  status?: string | null;
  progress?: number | null;
  totalBytes: number;
  payloadDownloadSpeed: number;
  payloadUploadSpeed: number;
  protocolDownloadSpeed: number;
  protocolUploadSpeed: number;
  downloadedPayload: number;
  uploadedPayload: number;
  protocolDownloaded: number;
  protocolUploaded: number;
  efficiencyRatio: number;
  connectedPeers: number;
  connectedSeeds: number;
  connectedLeechers: number;
  totalAvailablePeers: number;
  tcpPeers: number;
  utpPeers: number;
  encryptedPeers: number;
  plaintextPeers: number;
  totalPieces: number;
  completedPieces: number;
  piecesInFlight: number;
  pieceLength: number;
  hashFails: number;
  wastedBytes: number;
  diskPendingWrites: number;
  estimatedMemoryBufferBytes: number;
  swarmAvailability: number;
  ratio: number;
  etaSeconds?: number | null;
}

export interface SubsystemTelemetryReport {
  subsystemId: string;
  subsystemName: string;
  activeProvider: string;
  status: string;
  resourceLoad: string;
  metrics: Record<string, any>;
}

export interface SystemResourceTelemetrySnapshot {
  host: HostProcessResourceMetrics;
  torrentEngine: TorrentEngineMetrics;
  perTorrent: TorrentResourceMetrics[];
  subsystems: SubsystemTelemetryReport[];
  timestamp: string;
}

export interface SubsystemProvider {
  providerId: string;
  displayName: string;
  version: string;
  description: string;
  isActive: boolean;
  isAvailable: boolean;
  status: string;
  capabilities: Record<string, boolean | string | number>;
}

export interface SubsystemOverview {
  id: string;
  name: string;
  category: string;
  description: string;
  activeProviderId: string;
  providers: SubsystemProvider[];
}

export interface SwitchSubsystemRequest {
  subsystemId: string;
  providerId: string;
}

export interface SwitchSubsystemResult {
  success: boolean;
  subsystemId: string;
  previousProvider: string;
  activeProvider: string;
  message?: string;
  error?: string;
}

export interface SubsystemProbeResult {
  subsystemId: string;
  providerId: string;
  isHealthy: boolean;
  statusMessage: string;
  dependencyChecks?: string[];
  warnings?: string[];
}

export interface AiCapabilities {
  supportsNaturalLanguageSearch: boolean;
  supportsReleaseNameParsing: boolean;
  supportsDiagnosticCopilot: boolean;
  supportsMalwareAnomalyDetection: boolean;
  supportsSwarmOptimization: boolean;
  supportsLocalOfflineInference: boolean;
  supportsCloudLlm: boolean;
}

export interface AiStatus {
  activeProviderId: string;
  displayName: string;
  version: string;
  description: string;
  capabilities: AiCapabilities;
  health: {
    isHealthy: boolean;
    statusMessage: string;
    warnings: string[];
    latencyMs: number;
    modelName: string;
    version: string;
  };
}

export interface AiParsedRelease {
  rawTitle: string;
  cleanTitle: string;
  year?: number | null;
  season?: number | null;
  episode?: number | null;
  episodes: number[];
  resolution?: string;
  quality?: string;
  videoCodec?: string;
  audioCodec?: string;
  audioChannels?: string;
  dynamicRange?: string;
  releaseGroup?: string;
  language?: string;
  languages?: string[];
  edition?: string;
  isProper: boolean;
  isRepack: boolean;
  isRemux: boolean;
  confidenceScore: number;
  additionalTags: Record<string, string>;
}

export interface AiDiagnosticReport {
  torrentId: number;
  torrentName: string;
  overallHealth: string;
  severity: "Low" | "Medium" | "High" | string;
  summary: string;
  issues: string[];
  recommendations: string[];
  suggestedActions: string[];
  swarmAnalysis: string;
  trackerAnalysis: string;
  healthScore: number;
  analyzedAt: string;
}

export interface AiSearchParameters {
  rawQuery: string;
  cleanQuery: string;
  cleanTitle?: string;
  category?: string;
  year?: number | null;
  season?: number | null;
  episode?: number | null;
  resolution?: string;
  quality?: string;
  codec?: string;
  releaseGroup?: string;
  minSeeders: number;
  maxAgeDays?: number | null;
  freeleechOnly: boolean;
  tags: string[];
  confidenceScore: number;
}

export interface AiMalwareRiskAssessment {
  torrentName: string;
  riskScore: number;
  riskLevel: "Low" | "Medium" | "High" | "Critical" | string;
  isSuspicious: boolean;
  analyzedFilesCount: number;
  suspiciousFileNames: string[];
  threatReasons: string[];
  recommendations: string[];
  assessedAt: string;
}

export interface AiChatRequest {
  message: string;
  context?: string;
}

export interface AiChatResponse {
  reply: string;
  provider: string;
  success: boolean;
  error?: string;
}

export interface AiConfig {
  activeAiProvider: string;
  ollamaHost: string;
  ollamaModel: string;
  geminiApiKey: string;
  geminiModel: string;
  onnxModelPath: string;
  enableCopilotButton: boolean;
  enableNaturalSearch: boolean;
  enableSwarmDiagnostics: boolean;
}

export interface DatabaseColumn {
  cid: number;
  name: string;
  type: string;
  notNull: boolean;
  defaultValue?: string | null;
  isPrimaryKey: boolean;
}

export interface DatabaseForeignKey {
  id: number;
  fromColumn: string;
  toTable: string;
  toColumn: string;
  onUpdate: string;
  onDelete: string;
}

export interface DatabaseIndex {
  name: string;
  unique: boolean;
  columns: string[];
}

export interface DatabaseTable {
  name: string;
  rowCount: number;
  columnCount: number;
}

export interface DatabaseTableSchema {
  name: string;
  rowCount: number;
  columns: DatabaseColumn[];
  foreignKeys: DatabaseForeignKey[];
  indexes: DatabaseIndex[];
}

export interface DatabaseSchemaResponse {
  tables: DatabaseTableSchema[];
  mermaidErd: string;
}

export interface DatabaseQueryRequest {
  query: string;
  readOnly: boolean;
}

export interface QueryPlanNode {
  id: number;
  parentId: number;
  detail: string;
}

export interface DatabaseQueryResult {
  success: boolean;
  errorMessage?: string;
  executionTimeMs: number;
  isQuery: boolean;
  columns: string[];
  rows: Array<Array<unknown>>;
  totalRows: number;
  rowsAffected: number;
  message?: string;
  queryPlan?: QueryPlanNode[];
}

export interface DatabaseStorageItem {
  name: string;
  type: "table" | "index" | "free";
  tableName: string;
  bytes: number;
  pageCount: number;
  rowCount: number;
  percentage: number;
}

export interface DatabaseStorageResponse {
  totalSizeBytes: number;
  pageSize: number;
  pageCount: number;
  freeSizeBytes: number;
  items: DatabaseStorageItem[];
}

export interface DatabaseDiagnosticsResponse {
  databasePath: string;
  fileSizeBytes: number;
  pageSize: number;
  pageCount: number;
  freelistCount: number;
  journalMode: string;
  synchronous: string;
  cacheSize: number;
  encoding: string;
  integrityCheck: string;
  gcTotalMemoryBytes: number;
  gcGen0Collections: number;
  gcGen1Collections: number;
  gcGen2Collections: number;
  threadPoolAvailableWorkerThreads: number;
  threadPoolAvailableCompletionPortThreads: number;
  threadPoolMaxWorkerThreads: number;
  processUptimeSeconds: number;
  workingSetBytes: number;
  dotNetVersion: string;
}


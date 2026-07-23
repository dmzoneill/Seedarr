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

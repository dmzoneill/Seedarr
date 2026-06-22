import { useState, useEffect } from 'react';
import {
  useNetworkStatus,
  useTrackerServerConfig,
  useSaveTrackerServerConfig,
  useTrackerServerStats,
  useGeneralConfig,
  useSaveGeneralConfig,
  useSeedingConfig,
  useSaveSeedingConfig,
  useNetworkConfig,
  useSaveNetworkConfig,
  useBitTorrentConfig,
  useSaveBitTorrentConfig,
  usePeerProtocolConfig,
  useSavePeerProtocolConfig,
  useProtocolsConfig,
  useSaveProtocolsConfig,
  useSimulationConfig,
  useSaveSimulationConfig,
  useSchedulerConfig,
  useSaveSchedulerConfig,
  useAdvancedConfig,
  useSaveAdvancedConfig,
} from '../api/hooks';
import type {
  GeneralConfig,
  SeedingConfig,
  NetworkConfig,
  BitTorrentConfig,
  PeerProtocolConfig,
  ProtocolsConfig,
  SimulationConfig,
  TrackerServerConfig,
  SchedulerConfig,
  AdvancedConfig,
} from '../api/types';

type SettingsTab =
  | 'general'
  | 'seeding'
  | 'bittorrent'
  | 'network'
  | 'peer-protocol'
  | 'protocols'
  | 'simulation'
  | 'tracker-server'
  | 'scheduler'
  | 'advanced';

function formatUptime(seconds: number): string {
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  const parts: string[] = [];
  if (days > 0) parts.push(`${days}d`);
  if (hours > 0) parts.push(`${hours}h`);
  parts.push(`${mins}m`);
  return parts.join(' ');
}

function SaveFeedback({
  isPending,
  isError,
  isSuccess,
  error,
  dirty,
}: {
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  dirty: boolean;
}) {
  return (
    <>
      {isError && (
        <span className="error" style={{ marginLeft: '0.75rem', fontSize: '0.85rem' }}>
          Failed to save: {error?.message}
        </span>
      )}
      {isSuccess && !dirty && (
        <span style={{ marginLeft: '0.75rem', fontSize: '0.85rem', color: 'var(--success)' }}>
          Saved
        </span>
      )}
    </>
  );
}

function SaveBar({
  dirty,
  isPending,
  isError,
  isSuccess,
  error,
  onSave,
}: {
  dirty: boolean;
  isPending: boolean;
  isError: boolean;
  isSuccess: boolean;
  error: Error | null;
  onSave: () => void;
}) {
  return (
    <div className="form-actions">
      <button className="btn btn-success" onClick={onSave} disabled={!dirty || isPending}>
        {isPending ? 'Saving...' : 'Save'}
      </button>
      <SaveFeedback
        isPending={isPending}
        isError={isError}
        isSuccess={isSuccess}
        error={error}
        dirty={dirty}
      />
    </div>
  );
}

function NumberInput({
  label,
  value,
  onChange,
  min,
  max,
  step,
  hint,
  disabled,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <div className="form-group">
      <label className="form-label">
        {label}
        {hint && <span className="form-hint">{hint}</span>}
      </label>
      <input
        type="number"
        className="form-input"
        value={value}
        onChange={(e) => onChange(step && step < 1 ? parseFloat(e.target.value) || 0 : parseInt(e.target.value, 10) || 0)}
        min={min}
        max={max}
        step={step}
        disabled={disabled}
      />
    </div>
  );
}

function TextInput({
  label,
  value,
  onChange,
  placeholder,
  hint,
  disabled,
  type,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  hint?: string;
  disabled?: boolean;
  type?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">
        {label}
        {hint && <span className="form-hint">{hint}</span>}
      </label>
      <input
        type={type || 'text'}
        className="form-input"
        style={{ width: '200px', textAlign: 'left' }}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
      />
    </div>
  );
}

function Toggle({
  label,
  checked,
  onChange,
  hint,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
  hint?: string;
}) {
  return (
    <div className="form-group">
      <label className="form-label">
        {label}
        {hint && <span className="form-hint">{hint}</span>}
      </label>
      <label className="toggle-switch">
        <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="toggle-slider" />
      </label>
    </div>
  );
}

function SelectInput({
  label,
  value,
  onChange,
  options,
  hint,
  disabled,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <div className="form-group">
      <label className="form-label">
        {label}
        {hint && <span className="form-hint">{hint}</span>}
      </label>
      <select
        className="form-select"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <div className="form-section-title">{children}</div>;
}

function GeneralTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const save = useSaveGeneralConfig();
  const [form, setForm] = useState<GeneralConfig>({
    autoStart: false,
    themeStyle: 'system',
    colorScheme: 'auto',
    watchFolderEnabled: false,
    watchFolderPath: '',
    watchFolderScanIntervalSeconds: 10,
    watchFolderAutoStartTorrents: true,
    watchFolderDeleteAddedTorrents: false,
    port: 9898,
    bindAddress: '0.0.0.0',
    urlBase: '',
    authenticationEnabled: false,
    apiKey: '',
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const set = <K extends keyof GeneralConfig>(key: K, value: GeneralConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>General Settings</h3>

      <SectionTitle>Application</SectionTitle>
      <Toggle label="Auto Start" checked={form.autoStart} onChange={(v) => set('autoStart', v)} hint="Start seeding on launch" />
      <SelectInput
        label="Theme"
        value={form.themeStyle}
        onChange={(v) => set('themeStyle', v)}
        options={[
          { value: 'system', label: 'System' },
          { value: 'light', label: 'Light' },
          { value: 'dark', label: 'Dark' },
        ]}
      />
      <SelectInput
        label="Color Scheme"
        value={form.colorScheme}
        onChange={(v) => set('colorScheme', v)}
        options={[
          { value: 'auto', label: 'Auto' },
          { value: 'blue', label: 'Blue' },
          { value: 'green', label: 'Green' },
          { value: 'purple', label: 'Purple' },
        ]}
      />

      <SectionTitle>Host</SectionTitle>
      <NumberInput label="Port" value={form.port} onChange={(v) => set('port', v)} min={1} max={65535} />
      <TextInput label="Bind Address" value={form.bindAddress} onChange={(v) => set('bindAddress', v)} placeholder="0.0.0.0" />
      <TextInput label="URL Base" value={form.urlBase} onChange={(v) => set('urlBase', v)} placeholder="/seedarr" />
      <Toggle label="Authentication" checked={form.authenticationEnabled} onChange={(v) => set('authenticationEnabled', v)} />
      <TextInput label="API Key" value={form.apiKey} onChange={(v) => set('apiKey', v)} hint="For external access" />

      <SectionTitle>Watch Folder</SectionTitle>
      <Toggle label="Enabled" checked={form.watchFolderEnabled} onChange={(v) => set('watchFolderEnabled', v)} hint="Auto-add .torrent files" />
      <TextInput
        label="Path"
        value={form.watchFolderPath}
        onChange={(v) => set('watchFolderPath', v)}
        placeholder="/watch"
        disabled={!form.watchFolderEnabled}
      />
      <NumberInput
        label="Scan Interval"
        value={form.watchFolderScanIntervalSeconds}
        onChange={(v) => set('watchFolderScanIntervalSeconds', v)}
        min={1}
        hint="seconds"
        disabled={!form.watchFolderEnabled}
      />
      <Toggle
        label="Auto Start Torrents"
        checked={form.watchFolderAutoStartTorrents}
        onChange={(v) => set('watchFolderAutoStartTorrents', v)}
      />
      <Toggle
        label="Delete After Adding"
        checked={form.watchFolderDeleteAddedTorrents}
        onChange={(v) => set('watchFolderDeleteAddedTorrents', v)}
      />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function SeedingTab() {
  const { data: config, isLoading } = useSeedingConfig();
  const save = useSaveSeedingConfig();
  const [form, setForm] = useState<SeedingConfig>({
    maxUploadSpeedKbps: 0,
    maxDownloadSpeedKbps: 0,
    alternativeSpeedEnabled: false,
    altUploadSpeedKbps: 50,
    altDownloadSpeedKbps: 100,
    globalSeedRatioLimit: 0,
    uploadDistributionAlgorithm: 'Equal',
    uploadDistributionSpreadPercentage: 50,
    uploadRedistributionMode: 'tick',
    uploadCustomIntervalMinutes: 5,
    uploadStoppedMinPercentage: 20,
    uploadStoppedMaxPercentage: 40,
    downloadDistributionAlgorithm: 'Equal',
    downloadDistributionSpreadPercentage: 50,
    downloadRedistributionMode: 'tick',
    downloadCustomIntervalMinutes: 5,
    downloadStoppedMinPercentage: 20,
    downloadStoppedMaxPercentage: 40,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof SeedingConfig>(key: K, value: SeedingConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const distOptions = [
    { value: 'Equal', label: 'Equal' },
    { value: 'Pareto', label: 'Pareto' },
    { value: 'PowerLaw', label: 'Power Law' },
    { value: 'LogNormal', label: 'Log Normal' },
  ];

  const redistOptions = [
    { value: 'tick', label: 'Every Tick' },
    { value: 'custom', label: 'Custom Interval' },
    { value: 'never', label: 'Never' },
  ];

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>Speed &amp; Distribution</h3>

      <SectionTitle>Speed Limits</SectionTitle>
      <NumberInput label="Max Upload Speed" value={form.maxUploadSpeedKbps} onChange={(v) => set('maxUploadSpeedKbps', v)} min={0} hint="KB/s, 0 = unlimited" />
      <NumberInput label="Max Download Speed" value={form.maxDownloadSpeedKbps} onChange={(v) => set('maxDownloadSpeedKbps', v)} min={0} hint="KB/s, 0 = unlimited" />
      <NumberInput label="Global Seed Ratio Limit" value={form.globalSeedRatioLimit} onChange={(v) => set('globalSeedRatioLimit', v)} min={0} step={0.1} hint="0 = no limit" />

      <SectionTitle>Alternative Speeds</SectionTitle>
      <Toggle label="Enable Alt Speeds" checked={form.alternativeSpeedEnabled} onChange={(v) => set('alternativeSpeedEnabled', v)} />
      <NumberInput label="Alt Upload Speed" value={form.altUploadSpeedKbps} onChange={(v) => set('altUploadSpeedKbps', v)} min={0} hint="KB/s" disabled={!form.alternativeSpeedEnabled} />
      <NumberInput label="Alt Download Speed" value={form.altDownloadSpeedKbps} onChange={(v) => set('altDownloadSpeedKbps', v)} min={0} hint="KB/s" disabled={!form.alternativeSpeedEnabled} />

      <SectionTitle>Upload Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.uploadDistributionAlgorithm} onChange={(v) => set('uploadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.uploadDistributionSpreadPercentage} onChange={(v) => set('uploadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.uploadRedistributionMode} onChange={(v) => set('uploadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.uploadCustomIntervalMinutes} onChange={(v) => set('uploadCustomIntervalMinutes', v)} min={1} hint="minutes" disabled={form.uploadRedistributionMode !== 'custom'} />
      <NumberInput label="Stopped Min %" value={form.uploadStoppedMinPercentage} onChange={(v) => set('uploadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.uploadStoppedMaxPercentage} onChange={(v) => set('uploadStoppedMaxPercentage', v)} min={0} max={100} />

      <SectionTitle>Download Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.downloadDistributionAlgorithm} onChange={(v) => set('downloadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.downloadDistributionSpreadPercentage} onChange={(v) => set('downloadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.downloadRedistributionMode} onChange={(v) => set('downloadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.downloadCustomIntervalMinutes} onChange={(v) => set('downloadCustomIntervalMinutes', v)} min={1} hint="minutes" disabled={form.downloadRedistributionMode !== 'custom'} />
      <NumberInput label="Stopped Min %" value={form.downloadStoppedMinPercentage} onChange={(v) => set('downloadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.downloadStoppedMaxPercentage} onChange={(v) => set('downloadStoppedMaxPercentage', v)} min={0} max={100} />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function BitTorrentTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const save = useSaveBitTorrentConfig();
  const [form, setForm] = useState<BitTorrentConfig>({
    enableDht: true,
    enablePex: true,
    enableLpd: true,
    encryptionMode: 'enabled',
    bitTorrentUserAgent: 'qBittorrent/4.4.2',
    peerIdPrefix: '-qB4420-',
    announceIntervalSeconds: 1800,
    minAnnounceIntervalSeconds: 300,
    scrapeIntervalSeconds: 900,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof BitTorrentConfig>(key: K, value: BitTorrentConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>BitTorrent Settings</h3>

      <SectionTitle>Protocol Features</SectionTitle>
      <Toggle label="DHT" checked={form.enableDht} onChange={(v) => set('enableDht', v)} hint="Distributed Hash Table" />
      <Toggle label="PEX" checked={form.enablePex} onChange={(v) => set('enablePex', v)} hint="Peer Exchange" />
      <Toggle label="LPD" checked={form.enableLpd} onChange={(v) => set('enableLpd', v)} hint="Local Peer Discovery" />
      <SelectInput
        label="Encryption"
        value={form.encryptionMode}
        onChange={(v) => set('encryptionMode', v)}
        options={[
          { value: 'disabled', label: 'Disabled' },
          { value: 'enabled', label: 'Enabled' },
          { value: 'forced', label: 'Forced' },
        ]}
      />

      <SectionTitle>Client Identity</SectionTitle>
      <TextInput label="User Agent" value={form.bitTorrentUserAgent} onChange={(v) => set('bitTorrentUserAgent', v)} hint="HTTP tracker header" />
      <TextInput label="Peer ID Prefix" value={form.peerIdPrefix} onChange={(v) => set('peerIdPrefix', v)} hint="8-char Azureus-style" />

      <SectionTitle>Tracker Timing</SectionTitle>
      <NumberInput label="Announce Interval" value={form.announceIntervalSeconds} onChange={(v) => set('announceIntervalSeconds', v)} min={60} hint="seconds" />
      <NumberInput label="Min Announce Interval" value={form.minAnnounceIntervalSeconds} onChange={(v) => set('minAnnounceIntervalSeconds', v)} min={30} hint="seconds" />
      <NumberInput label="Scrape Interval" value={form.scrapeIntervalSeconds} onChange={(v) => set('scrapeIntervalSeconds', v)} min={60} hint="seconds" />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function NetworkTab() {
  const { data: status } = useNetworkStatus();
  const { data: config, isLoading } = useNetworkConfig();
  const save = useSaveNetworkConfig();
  const [form, setForm] = useState<NetworkConfig>({
    listeningPort: 6881,
    upnpEnabled: true,
    maxGlobalConnections: 200,
    maxPerTorrentConnections: 50,
    maxUploadSlots: 4,
    proxyType: 'none',
    proxyHost: '',
    proxyPort: 8080,
    proxyAuthEnabled: false,
    proxyUsername: '',
    proxyPassword: '',
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof NetworkConfig>(key: K, value: NetworkConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <h3>Network Status</h3>
        <div className="status-row">
          <span className="status-label">Local IP</span>
          <span className="status-value">{status?.localIp ?? '-'}</span>
        </div>
        <div className="status-row">
          <span className="status-label">External IP</span>
          <span className="status-value">{status?.externalIp || '-'}</span>
        </div>
      </div>

      <div className="card">
        <h3>Connection Settings</h3>

        <SectionTitle>Listening</SectionTitle>
        <NumberInput label="Port" value={form.listeningPort} onChange={(v) => set('listeningPort', v)} min={1} max={65535} />
        <Toggle label="UPnP" checked={form.upnpEnabled} onChange={(v) => set('upnpEnabled', v)} hint="Auto port mapping" />

        <SectionTitle>Limits</SectionTitle>
        <NumberInput label="Max Global Connections" value={form.maxGlobalConnections} onChange={(v) => set('maxGlobalConnections', v)} min={1} />
        <NumberInput label="Max Per Torrent" value={form.maxPerTorrentConnections} onChange={(v) => set('maxPerTorrentConnections', v)} min={1} />
        <NumberInput label="Max Upload Slots" value={form.maxUploadSlots} onChange={(v) => set('maxUploadSlots', v)} min={1} />

        <SectionTitle>Proxy</SectionTitle>
        <SelectInput
          label="Type"
          value={form.proxyType}
          onChange={(v) => set('proxyType', v)}
          options={[
            { value: 'none', label: 'None' },
            { value: 'http', label: 'HTTP' },
            { value: 'socks4', label: 'SOCKS4' },
            { value: 'socks5', label: 'SOCKS5' },
          ]}
        />
        <TextInput label="Host" value={form.proxyHost} onChange={(v) => set('proxyHost', v)} placeholder="proxy.example.com" disabled={form.proxyType === 'none'} />
        <NumberInput label="Port" value={form.proxyPort} onChange={(v) => set('proxyPort', v)} min={1} max={65535} disabled={form.proxyType === 'none'} />
        <Toggle label="Proxy Auth" checked={form.proxyAuthEnabled} onChange={(v) => set('proxyAuthEnabled', v)} />
        <TextInput label="Username" value={form.proxyUsername} onChange={(v) => set('proxyUsername', v)} disabled={!form.proxyAuthEnabled} />
        <TextInput label="Password" value={form.proxyPassword} onChange={(v) => set('proxyPassword', v)} type="password" disabled={!form.proxyAuthEnabled} />

        <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      </div>
    </>
  );
}

function PeerProtocolTab() {
  const { data: config, isLoading } = usePeerProtocolConfig();
  const save = useSavePeerProtocolConfig();
  const [form, setForm] = useState<PeerProtocolConfig>({
    handshakeTimeoutSeconds: 30,
    messageReadTimeoutSeconds: 60,
    keepAliveIntervalSeconds: 120,
    peerContactIntervalSeconds: 300,
    udpTrackerTimeoutSeconds: 5,
    httpTrackerTimeoutSeconds: 10,
    peerRequestCount: 200,
    seederUploadActivityProbability: 0.3,
    peerIdleChance: 0.3,
    peerDropoutProbability: 0.1,
    connectionRotationPercentage: 0.25,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof PeerProtocolConfig>(key: K, value: PeerProtocolConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>Peer Protocol</h3>

      <SectionTitle>Timeouts</SectionTitle>
      <NumberInput label="Handshake Timeout" value={form.handshakeTimeoutSeconds} onChange={(v) => set('handshakeTimeoutSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="Message Read Timeout" value={form.messageReadTimeoutSeconds} onChange={(v) => set('messageReadTimeoutSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="Keep Alive Interval" value={form.keepAliveIntervalSeconds} onChange={(v) => set('keepAliveIntervalSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="Peer Contact Interval" value={form.peerContactIntervalSeconds} onChange={(v) => set('peerContactIntervalSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="UDP Tracker Timeout" value={form.udpTrackerTimeoutSeconds} onChange={(v) => set('udpTrackerTimeoutSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="HTTP Tracker Timeout" value={form.httpTrackerTimeoutSeconds} onChange={(v) => set('httpTrackerTimeoutSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="Peer Request Count" value={form.peerRequestCount} onChange={(v) => set('peerRequestCount', v)} min={1} hint="peers to contact" />

      <SectionTitle>Peer Behavior</SectionTitle>
      <NumberInput label="Upload Activity Probability" value={form.seederUploadActivityProbability} onChange={(v) => set('seederUploadActivityProbability', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Idle Chance" value={form.peerIdleChance} onChange={(v) => set('peerIdleChance', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Dropout Probability" value={form.peerDropoutProbability} onChange={(v) => set('peerDropoutProbability', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Connection Rotation" value={form.connectionRotationPercentage} onChange={(v) => set('connectionRotationPercentage', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function ProtocolsTab() {
  const { data: config, isLoading } = useProtocolsConfig();
  const save = useSaveProtocolsConfig();
  const [form, setForm] = useState<ProtocolsConfig>({
    extensionUtMetadata: true,
    extensionUtPex: true,
    extensionLtDontHave: true,
    extensionFastExtension: true,
    utpEnabled: false,
    tcpFallback: true,
    transportConnectionTimeoutSeconds: 30,
    pexInterval: 60,
    pexMaxPeersPerMessage: 50,
    multiTrackerEnabled: true,
    multiTrackerFailoverEnabled: true,
    announceToAllTiers: false,
    announceToAllInTier: false,
    failoverMaxConsecutiveFailures: 5,
    failoverBackoffBaseSeconds: 60,
    failoverMaxBackoffSeconds: 3600,
    dhtRoutingTableSize: 160,
    dhtAnnouncementInterval: 1800,
    dhtBootstrapTimeout: 30,
    dhtQueryTimeout: 10,
    dhtMaxNodes: 1000,
    dhtBucketSize: 8,
    dhtConcurrentQueries: 3,
    dhtAutoBootstrap: true,
    dhtRateLimitEnabled: true,
    dhtMaxQueriesPerSecond: 10,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof ProtocolsConfig>(key: K, value: ProtocolsConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>Protocol Extensions</h3>

      <SectionTitle>BEP Extensions</SectionTitle>
      <Toggle label="ut_metadata" checked={form.extensionUtMetadata} onChange={(v) => set('extensionUtMetadata', v)} hint="BEP 9" />
      <Toggle label="ut_pex" checked={form.extensionUtPex} onChange={(v) => set('extensionUtPex', v)} hint="BEP 11" />
      <Toggle label="lt_donthave" checked={form.extensionLtDontHave} onChange={(v) => set('extensionLtDontHave', v)} />
      <Toggle label="Fast Extension" checked={form.extensionFastExtension} onChange={(v) => set('extensionFastExtension', v)} hint="BEP 6" />

      <SectionTitle>Transport</SectionTitle>
      <Toggle label="uTP" checked={form.utpEnabled} onChange={(v) => set('utpEnabled', v)} hint="BEP 29, LEDBAT" />
      <Toggle label="TCP Fallback" checked={form.tcpFallback} onChange={(v) => set('tcpFallback', v)} />
      <NumberInput label="Connection Timeout" value={form.transportConnectionTimeoutSeconds} onChange={(v) => set('transportConnectionTimeoutSeconds', v)} min={1} hint="seconds" />

      <SectionTitle>PEX</SectionTitle>
      <NumberInput label="PEX Interval" value={form.pexInterval} onChange={(v) => set('pexInterval', v)} min={10} hint="seconds" />
      <NumberInput label="Max Peers Per Message" value={form.pexMaxPeersPerMessage} onChange={(v) => set('pexMaxPeersPerMessage', v)} min={1} />

      <SectionTitle>Multi-Tracker</SectionTitle>
      <Toggle label="Enabled" checked={form.multiTrackerEnabled} onChange={(v) => set('multiTrackerEnabled', v)} hint="BEP 12" />
      <Toggle label="Failover" checked={form.multiTrackerFailoverEnabled} onChange={(v) => set('multiTrackerFailoverEnabled', v)} />
      <Toggle label="Announce to All Tiers" checked={form.announceToAllTiers} onChange={(v) => set('announceToAllTiers', v)} />
      <Toggle label="Announce to All in Tier" checked={form.announceToAllInTier} onChange={(v) => set('announceToAllInTier', v)} />
      <NumberInput label="Max Consecutive Failures" value={form.failoverMaxConsecutiveFailures} onChange={(v) => set('failoverMaxConsecutiveFailures', v)} min={1} />
      <NumberInput label="Backoff Base" value={form.failoverBackoffBaseSeconds} onChange={(v) => set('failoverBackoffBaseSeconds', v)} min={1} hint="seconds" />
      <NumberInput label="Max Backoff" value={form.failoverMaxBackoffSeconds} onChange={(v) => set('failoverMaxBackoffSeconds', v)} min={1} hint="seconds" />

      <SectionTitle>DHT</SectionTitle>
      <Toggle label="Auto Bootstrap" checked={form.dhtAutoBootstrap} onChange={(v) => set('dhtAutoBootstrap', v)} />
      <Toggle label="Rate Limiting" checked={form.dhtRateLimitEnabled} onChange={(v) => set('dhtRateLimitEnabled', v)} />
      <NumberInput label="Max Queries/sec" value={form.dhtMaxQueriesPerSecond} onChange={(v) => set('dhtMaxQueriesPerSecond', v)} min={1} disabled={!form.dhtRateLimitEnabled} />
      <NumberInput label="Routing Table Size" value={form.dhtRoutingTableSize} onChange={(v) => set('dhtRoutingTableSize', v)} min={1} />
      <NumberInput label="Announcement Interval" value={form.dhtAnnouncementInterval} onChange={(v) => set('dhtAnnouncementInterval', v)} min={60} hint="seconds" />
      <NumberInput label="Bootstrap Timeout" value={form.dhtBootstrapTimeout} onChange={(v) => set('dhtBootstrapTimeout', v)} min={1} hint="seconds" />
      <NumberInput label="Query Timeout" value={form.dhtQueryTimeout} onChange={(v) => set('dhtQueryTimeout', v)} min={1} hint="seconds" />
      <NumberInput label="Max Nodes" value={form.dhtMaxNodes} onChange={(v) => set('dhtMaxNodes', v)} min={1} />
      <NumberInput label="Bucket Size (K)" value={form.dhtBucketSize} onChange={(v) => set('dhtBucketSize', v)} min={1} />
      <NumberInput label="Concurrent Queries" value={form.dhtConcurrentQueries} onChange={(v) => set('dhtConcurrentQueries', v)} min={1} />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function SimulationTab() {
  const { data: config, isLoading } = useSimulationConfig();
  const save = useSaveSimulationConfig();
  const [form, setForm] = useState<SimulationConfig>({
    clientBehaviorEngineEnabled: true,
    primaryClient: 'qBittorrent',
    behaviorVariation: 0.3,
    clientProfileSwitching: true,
    switchClientProbability: 0.05,
    trafficPatternProfile: 'balanced',
    realisticVariations: true,
    timeBasedPatterns: true,
    swarmIntelligenceEnabled: true,
    swarmAdaptationRate: 0.5,
    swarmPeerAnalysisDepth: 10,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof SimulationConfig>(key: K, value: SimulationConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>Client Simulation</h3>

      <SectionTitle>Behavior Engine</SectionTitle>
      <Toggle label="Enabled" checked={form.clientBehaviorEngineEnabled} onChange={(v) => set('clientBehaviorEngineEnabled', v)} />
      <SelectInput
        label="Primary Client"
        value={form.primaryClient}
        onChange={(v) => set('primaryClient', v)}
        options={[
          { value: 'qBittorrent', label: 'qBittorrent' },
          { value: 'Deluge', label: 'Deluge' },
          { value: 'Transmission', label: 'Transmission' },
          { value: 'uTorrent', label: 'uTorrent' },
          { value: 'BiglyBT', label: 'BiglyBT' },
        ]}
        disabled={!form.clientBehaviorEngineEnabled}
      />
      <NumberInput label="Behavior Variation" value={form.behaviorVariation} onChange={(v) => set('behaviorVariation', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" disabled={!form.clientBehaviorEngineEnabled} />

      <SectionTitle>Profile Switching</SectionTitle>
      <Toggle label="Client Switching" checked={form.clientProfileSwitching} onChange={(v) => set('clientProfileSwitching', v)} hint="Rotate client identity" />
      <NumberInput label="Switch Probability" value={form.switchClientProbability} onChange={(v) => set('switchClientProbability', v)} min={0} max={1} step={0.01} hint="per announce" disabled={!form.clientProfileSwitching} />

      <SectionTitle>Traffic Patterns</SectionTitle>
      <SelectInput
        label="Profile"
        value={form.trafficPatternProfile}
        onChange={(v) => set('trafficPatternProfile', v)}
        options={[
          { value: 'conservative', label: 'Conservative' },
          { value: 'balanced', label: 'Balanced' },
          { value: 'aggressive', label: 'Aggressive' },
        ]}
      />
      <Toggle label="Realistic Variations" checked={form.realisticVariations} onChange={(v) => set('realisticVariations', v)} />
      <Toggle label="Time-Based Patterns" checked={form.timeBasedPatterns} onChange={(v) => set('timeBasedPatterns', v)} hint="Vary by time of day" />

      <SectionTitle>Swarm Intelligence</SectionTitle>
      <Toggle label="Enabled" checked={form.swarmIntelligenceEnabled} onChange={(v) => set('swarmIntelligenceEnabled', v)} />
      <NumberInput label="Adaptation Rate" value={form.swarmAdaptationRate} onChange={(v) => set('swarmAdaptationRate', v)} min={0} max={1} step={0.1} disabled={!form.swarmIntelligenceEnabled} />
      <NumberInput label="Peer Analysis Depth" value={form.swarmPeerAnalysisDepth} onChange={(v) => set('swarmPeerAnalysisDepth', v)} min={1} disabled={!form.swarmIntelligenceEnabled} />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function TrackerServerTab() {
  const { data: config, isLoading: configLoading } = useTrackerServerConfig();
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const save = useSaveTrackerServerConfig();
  const [form, setForm] = useState<TrackerServerConfig>({
    trackerServerEnabled: false,
    trackerHttpEnabled: true,
    trackerHttpPort: 9696,
    trackerUdpEnabled: true,
    trackerUdpPort: 6969,
    trackerBindAddress: '0.0.0.0',
    trackerAnnounceInterval: 1800,
    trackerMaxPeersPerAnnounce: 50,
    trackerEnableScrape: true,
    trackerPrivateMode: false,
    trackerLogAnnounces: false,
    trackerRateLimitPerMinute: 60,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof TrackerServerConfig>(key: K, value: TrackerServerConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (configLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <h3>Tracker Server</h3>

        <Toggle label="Enabled" checked={form.trackerServerEnabled} onChange={(v) => set('trackerServerEnabled', v)} hint="Built-in tracker" />
        <TextInput label="Bind Address" value={form.trackerBindAddress} onChange={(v) => set('trackerBindAddress', v)} placeholder="0.0.0.0" disabled={!form.trackerServerEnabled} />

        <SectionTitle>HTTP Tracker</SectionTitle>
        <Toggle label="HTTP Enabled" checked={form.trackerHttpEnabled} onChange={(v) => set('trackerHttpEnabled', v)} />
        <NumberInput label="HTTP Port" value={form.trackerHttpPort} onChange={(v) => set('trackerHttpPort', v)} min={1} max={65535} disabled={!form.trackerHttpEnabled} />

        <SectionTitle>UDP Tracker</SectionTitle>
        <Toggle label="UDP Enabled" checked={form.trackerUdpEnabled} onChange={(v) => set('trackerUdpEnabled', v)} />
        <NumberInput label="UDP Port" value={form.trackerUdpPort} onChange={(v) => set('trackerUdpPort', v)} min={1} max={65535} disabled={!form.trackerUdpEnabled} />

        <SectionTitle>Behavior</SectionTitle>
        <NumberInput label="Announce Interval" value={form.trackerAnnounceInterval} onChange={(v) => set('trackerAnnounceInterval', v)} min={60} hint="seconds" />
        <NumberInput label="Max Peers Per Announce" value={form.trackerMaxPeersPerAnnounce} onChange={(v) => set('trackerMaxPeersPerAnnounce', v)} min={1} />
        <Toggle label="Enable Scrape" checked={form.trackerEnableScrape} onChange={(v) => set('trackerEnableScrape', v)} />
        <Toggle label="Private Mode" checked={form.trackerPrivateMode} onChange={(v) => set('trackerPrivateMode', v)} />
        <Toggle label="Log Announces" checked={form.trackerLogAnnounces} onChange={(v) => set('trackerLogAnnounces', v)} />
        <NumberInput label="Rate Limit" value={form.trackerRateLimitPerMinute} onChange={(v) => set('trackerRateLimitPerMinute', v)} min={1} hint="per minute" />

        <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      </div>

      <div className="card">
        <h3>Tracker Statistics</h3>
        {statsLoading ? (
          <div className="loading">Loading stats...</div>
        ) : stats ? (
          <>
            <div className="stats-grid">
              <div className="stat-card">
                <div className="stat-value">{stats.totalTorrents.toLocaleString()}</div>
                <div className="stat-label">Torrents</div>
              </div>
              <div className="stat-card">
                <div className="stat-value">{stats.totalPeers.toLocaleString()}</div>
                <div className="stat-label">Peers</div>
              </div>
              <div className="stat-card">
                <div className="stat-value">{stats.totalAnnounces.toLocaleString()}</div>
                <div className="stat-label">Announces</div>
              </div>
              <div className="stat-card">
                <div className="stat-value">{stats.totalScrapes.toLocaleString()}</div>
                <div className="stat-label">Scrapes</div>
              </div>
            </div>
            <div className="status-row">
              <span className="status-label">Uptime</span>
              <span className="status-value">{formatUptime(stats.uptime)}</span>
            </div>
          </>
        ) : (
          <div className="loading">No stats available</div>
        )}
      </div>
    </>
  );
}

function SchedulerTab() {
  const { data: config, isLoading } = useSchedulerConfig();
  const save = useSaveSchedulerConfig();
  const [form, setForm] = useState<SchedulerConfig>({
    schedulerEnabled: false,
    schedulerStartHour: 22,
    schedulerStartMinute: 0,
    schedulerEndHour: 6,
    schedulerEndMinute: 0,
    schedulerMonday: true,
    schedulerTuesday: true,
    schedulerWednesday: true,
    schedulerThursday: true,
    schedulerFriday: true,
    schedulerSaturday: true,
    schedulerSunday: true,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof SchedulerConfig>(key: K, value: SchedulerConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  const days: { key: keyof SchedulerConfig; label: string }[] = [
    { key: 'schedulerMonday', label: 'Monday' },
    { key: 'schedulerTuesday', label: 'Tuesday' },
    { key: 'schedulerWednesday', label: 'Wednesday' },
    { key: 'schedulerThursday', label: 'Thursday' },
    { key: 'schedulerFriday', label: 'Friday' },
    { key: 'schedulerSaturday', label: 'Saturday' },
    { key: 'schedulerSunday', label: 'Sunday' },
  ];

  return (
    <div className="card">
      <h3>Speed Scheduler</h3>

      <Toggle label="Enabled" checked={form.schedulerEnabled} onChange={(v) => set('schedulerEnabled', v)} hint="Use alt speeds on schedule" />

      <SectionTitle>Time Window</SectionTitle>
      <NumberInput label="Start Hour" value={form.schedulerStartHour} onChange={(v) => set('schedulerStartHour', v)} min={0} max={23} disabled={!form.schedulerEnabled} />
      <NumberInput label="Start Minute" value={form.schedulerStartMinute} onChange={(v) => set('schedulerStartMinute', v)} min={0} max={59} disabled={!form.schedulerEnabled} />
      <NumberInput label="End Hour" value={form.schedulerEndHour} onChange={(v) => set('schedulerEndHour', v)} min={0} max={23} disabled={!form.schedulerEnabled} />
      <NumberInput label="End Minute" value={form.schedulerEndMinute} onChange={(v) => set('schedulerEndMinute', v)} min={0} max={59} disabled={!form.schedulerEnabled} />

      <SectionTitle>Active Days</SectionTitle>
      {days.map((day) => (
        <Toggle
          key={day.key}
          label={day.label}
          checked={form[day.key] as boolean}
          onChange={(v) => set(day.key, v as never)}
        />
      ))}

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function AdvancedTab() {
  const { data: config, isLoading } = useAdvancedConfig();
  const save = useSaveAdvancedConfig();
  const [form, setForm] = useState<AdvancedConfig>({
    logToFile: true,
    fileLogLevel: 'Info',
    debugMode: false,
    uiRefreshRateSec: 9,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) { setForm(config); setDirty(false); }
  }, [config]);

  const set = <K extends keyof AdvancedConfig>(key: K, value: AdvancedConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <div className="card">
      <h3>Advanced / Logging</h3>

      <SectionTitle>Logging</SectionTitle>
      <Toggle label="Log to File" checked={form.logToFile} onChange={(v) => set('logToFile', v)} />
      <SelectInput
        label="Log Level"
        value={form.fileLogLevel}
        onChange={(v) => set('fileLogLevel', v)}
        options={[
          { value: 'Trace', label: 'Trace' },
          { value: 'Debug', label: 'Debug' },
          { value: 'Info', label: 'Info' },
          { value: 'Warn', label: 'Warn' },
          { value: 'Error', label: 'Error' },
        ]}
        disabled={!form.logToFile}
      />
      <Toggle label="Debug Mode" checked={form.debugMode} onChange={(v) => set('debugMode', v)} />

      <SectionTitle>UI</SectionTitle>
      <NumberInput label="Refresh Rate" value={form.uiRefreshRateSec} onChange={(v) => set('uiRefreshRateSec', v)} min={1} max={60} hint="seconds" />

      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
    </div>
  );
}

function Settings() {
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');

  const tabs: { key: SettingsTab; label: string }[] = [
    { key: 'general', label: 'General' },
    { key: 'seeding', label: 'Seeding' },
    { key: 'bittorrent', label: 'BitTorrent' },
    { key: 'network', label: 'Network' },
    { key: 'peer-protocol', label: 'Peer Protocol' },
    { key: 'protocols', label: 'Protocols' },
    { key: 'simulation', label: 'Simulation' },
    { key: 'tracker-server', label: 'Tracker Server' },
    { key: 'scheduler', label: 'Scheduler' },
    { key: 'advanced', label: 'Advanced' },
  ];

  return (
    <div>
      <h1 className="page-heading">Settings</h1>

      <div className="tab-nav">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            className={`tab-btn ${activeTab === tab.key ? 'tab-btn-active' : ''}`}
            onClick={() => setActiveTab(tab.key)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'general' && <GeneralTab />}
      {activeTab === 'seeding' && <SeedingTab />}
      {activeTab === 'bittorrent' && <BitTorrentTab />}
      {activeTab === 'network' && <NetworkTab />}
      {activeTab === 'peer-protocol' && <PeerProtocolTab />}
      {activeTab === 'protocols' && <ProtocolsTab />}
      {activeTab === 'simulation' && <SimulationTab />}
      {activeTab === 'tracker-server' && <TrackerServerTab />}
      {activeTab === 'scheduler' && <SchedulerTab />}
      {activeTab === 'advanced' && <AdvancedTab />}
    </div>
  );
}

export default Settings;

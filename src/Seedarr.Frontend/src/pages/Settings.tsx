import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
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
  useArrConnections,
  useCreateArrConnection,
  useUpdateArrConnection,
  useDeleteArrConnection,
  useTestArrConnection,
  useArrSync,
  useDownloadClients,
  useCreateDownloadClient,
  useUpdateDownloadClient,
  useDeleteDownloadClient,
  useTestDownloadClient,
  useIndexers,
  useCreateIndexer,
  useUpdateIndexer,
  useDeleteIndexer,
  useTestIndexer,
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
  ArrConnection,
  IndexerDefinition,
  DownloadClientDefinition,
  NotificationSettings,
} from '../api/types';

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
    <div className="settings-toolbar">
      <button className="btn btn-success" onClick={onSave} disabled={!dirty || isPending}>
        {isPending ? 'Saving...' : dirty ? 'Save Changes' : 'No Changes'}
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
  suffix,
  disabled,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  hint?: string;
  suffix?: string;
  disabled?: boolean;
}) {
  const inputEl = (
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
  );

  return (
    <div className="form-group">
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        {suffix ? (
          <div className="form-input-with-suffix">
            {inputEl}
            <span className="form-input-suffix">{suffix}</span>
          </div>
        ) : (
          inputEl
        )}
        {hint && <span className="form-hint">{hint}</span>}
      </div>
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
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <input
          type={type || 'text'}
          className="form-input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
        />
        {hint && <span className="form-hint">{hint}</span>}
      </div>
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
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
        <div className="form-toggle-row">
          <label className="toggle-switch">
            <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
            <span className="toggle-slider" />
          </label>
          {hint && <span className="form-toggle-description">{hint}</span>}
        </div>
      </div>
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
      <label className="form-label">{label}</label>
      <div className="form-input-wrapper">
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
        {hint && <span className="form-hint">{hint}</span>}
      </div>
    </div>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <div className="form-section-title">{children}</div>;
}

const NOTIFICATION_SETTINGS_KEY = 'seedarr-notification-settings';

const defaultNotificationSettings: NotificationSettings = {
  enabled: true,
  position: 'top-right',
  autoDismissSeconds: 5,
  showInfo: true,
  showSuccess: true,
  showWarning: true,
  showError: true,
};

function useNotificationSettings(): [NotificationSettings, (settings: NotificationSettings) => void] {
  const [settings, setSettings] = useState<NotificationSettings>(() => {
    try {
      const stored = localStorage.getItem(NOTIFICATION_SETTINGS_KEY);
      return stored ? { ...defaultNotificationSettings, ...JSON.parse(stored) } : defaultNotificationSettings;
    } catch {
      return defaultNotificationSettings;
    }
  });

  const saveSettings = (newSettings: NotificationSettings) => {
    setSettings(newSettings);
    localStorage.setItem(NOTIFICATION_SETTINGS_KEY, JSON.stringify(newSettings));
  };

  return [settings, saveSettings];
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
    httpsEnabled: false,
    username: '',
    password: '',
    sessionTimeoutMinutes: 30,
    localhostOnly: false,
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

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
        suffix="seconds"
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

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>Quick Profiles</SectionTitle>
      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 100,
              maxDownloadSpeedKbps: 200,
              uploadDistributionAlgorithm: 'Equal',
              uploadDistributionSpreadPercentage: 30,
              downloadDistributionAlgorithm: 'Equal',
              downloadDistributionSpreadPercentage: 30,
              globalSeedRatioLimit: 1.5,
            }));
            setDirty(true);
          }}
        >
          Conservative
        </button>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 500,
              maxDownloadSpeedKbps: 1000,
              uploadDistributionAlgorithm: 'Pareto',
              uploadDistributionSpreadPercentage: 50,
              downloadDistributionAlgorithm: 'Pareto',
              downloadDistributionSpreadPercentage: 50,
              globalSeedRatioLimit: 2.0,
            }));
            setDirty(true);
          }}
        >
          Balanced
        </button>
        <button
          className="btn"
          onClick={() => {
            setForm((prev) => ({
              ...prev,
              maxUploadSpeedKbps: 0,
              maxDownloadSpeedKbps: 0,
              uploadDistributionAlgorithm: 'PowerLaw',
              uploadDistributionSpreadPercentage: 80,
              downloadDistributionAlgorithm: 'PowerLaw',
              downloadDistributionSpreadPercentage: 80,
              globalSeedRatioLimit: 0,
            }));
            setDirty(true);
          }}
        >
          Aggressive
        </button>
      </div>
      <div className="form-hint" style={{ marginBottom: '1rem', fontSize: '0.8rem', color: 'var(--text-muted)' }}>
        Conservative: low speeds, equal distribution, 1.5 ratio limit.
        Balanced: medium speeds, Pareto distribution, 2.0 ratio limit.
        Aggressive: unlimited speeds, power law distribution, no ratio limit.
      </div>

      <SectionTitle>Speed Limits</SectionTitle>
      <NumberInput label="Max Upload Speed" value={form.maxUploadSpeedKbps} onChange={(v) => set('maxUploadSpeedKbps', v)} min={0} suffix="KB/s" hint="Set to 0 for unlimited speed" />
      <NumberInput label="Max Download Speed" value={form.maxDownloadSpeedKbps} onChange={(v) => set('maxDownloadSpeedKbps', v)} min={0} suffix="KB/s" hint="Set to 0 for unlimited speed" />
      <NumberInput label="Global Seed Ratio Limit" value={form.globalSeedRatioLimit} onChange={(v) => set('globalSeedRatioLimit', v)} min={0} step={0.1} hint="Stop seeding when ratio reaches this value. Set to 0 to disable." />

      <SectionTitle>Alternative Speeds</SectionTitle>
      <Toggle label="Enable Alt Speeds" checked={form.alternativeSpeedEnabled} onChange={(v) => set('alternativeSpeedEnabled', v)} />
      <NumberInput label="Alt Upload Speed" value={form.altUploadSpeedKbps} onChange={(v) => set('altUploadSpeedKbps', v)} min={0} suffix="KB/s" disabled={!form.alternativeSpeedEnabled} />
      <NumberInput label="Alt Download Speed" value={form.altDownloadSpeedKbps} onChange={(v) => set('altDownloadSpeedKbps', v)} min={0} suffix="KB/s" disabled={!form.alternativeSpeedEnabled} />

      <SectionTitle>Upload Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.uploadDistributionAlgorithm} onChange={(v) => set('uploadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.uploadDistributionSpreadPercentage} onChange={(v) => set('uploadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.uploadRedistributionMode} onChange={(v) => set('uploadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.uploadCustomIntervalMinutes} onChange={(v) => set('uploadCustomIntervalMinutes', v)} min={1} suffix="minutes" disabled={form.uploadRedistributionMode !== 'custom'} />
      <NumberInput label="Stopped Min %" value={form.uploadStoppedMinPercentage} onChange={(v) => set('uploadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.uploadStoppedMaxPercentage} onChange={(v) => set('uploadStoppedMaxPercentage', v)} min={0} max={100} />

      <SectionTitle>Download Distribution</SectionTitle>
      <SelectInput label="Algorithm" value={form.downloadDistributionAlgorithm} onChange={(v) => set('downloadDistributionAlgorithm', v)} options={distOptions} />
      <NumberInput label="Spread %" value={form.downloadDistributionSpreadPercentage} onChange={(v) => set('downloadDistributionSpreadPercentage', v)} min={0} max={100} />
      <SelectInput label="Redistribution" value={form.downloadRedistributionMode} onChange={(v) => set('downloadRedistributionMode', v)} options={redistOptions} />
      <NumberInput label="Custom Interval" value={form.downloadCustomIntervalMinutes} onChange={(v) => set('downloadCustomIntervalMinutes', v)} min={1} suffix="minutes" disabled={form.downloadRedistributionMode !== 'custom'} />
      <NumberInput label="Stopped Min %" value={form.downloadStoppedMinPercentage} onChange={(v) => set('downloadStoppedMinPercentage', v)} min={0} max={100} />
      <NumberInput label="Stopped Max %" value={form.downloadStoppedMaxPercentage} onChange={(v) => set('downloadStoppedMaxPercentage', v)} min={0} max={100} />

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

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
      <NumberInput label="Announce Interval" value={form.announceIntervalSeconds} onChange={(v) => set('announceIntervalSeconds', v)} min={60} suffix="seconds" hint="Time between tracker announces" />
      <NumberInput label="Min Announce Interval" value={form.minAnnounceIntervalSeconds} onChange={(v) => set('minAnnounceIntervalSeconds', v)} min={30} suffix="seconds" />
      <NumberInput label="Scrape Interval" value={form.scrapeIntervalSeconds} onChange={(v) => set('scrapeIntervalSeconds', v)} min={60} suffix="seconds" />

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
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

        <SectionTitle>Listening</SectionTitle>
        <NumberInput label="Port" value={form.listeningPort} onChange={(v) => set('listeningPort', v)} min={1} max={65535} hint="Port used for incoming peer connections" />
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

      </div>
    </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>Timeouts</SectionTitle>
      <NumberInput label="Handshake Timeout" value={form.handshakeTimeoutSeconds} onChange={(v) => set('handshakeTimeoutSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="Message Read Timeout" value={form.messageReadTimeoutSeconds} onChange={(v) => set('messageReadTimeoutSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="Keep Alive Interval" value={form.keepAliveIntervalSeconds} onChange={(v) => set('keepAliveIntervalSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="Peer Contact Interval" value={form.peerContactIntervalSeconds} onChange={(v) => set('peerContactIntervalSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="UDP Tracker Timeout" value={form.udpTrackerTimeoutSeconds} onChange={(v) => set('udpTrackerTimeoutSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="HTTP Tracker Timeout" value={form.httpTrackerTimeoutSeconds} onChange={(v) => set('httpTrackerTimeoutSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="Peer Request Count" value={form.peerRequestCount} onChange={(v) => set('peerRequestCount', v)} min={1} suffix="peers" />

      <SectionTitle>Peer Behavior</SectionTitle>
      <NumberInput label="Upload Activity Probability" value={form.seederUploadActivityProbability} onChange={(v) => set('seederUploadActivityProbability', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Idle Chance" value={form.peerIdleChance} onChange={(v) => set('peerIdleChance', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Dropout Probability" value={form.peerDropoutProbability} onChange={(v) => set('peerDropoutProbability', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />
      <NumberInput label="Connection Rotation" value={form.connectionRotationPercentage} onChange={(v) => set('connectionRotationPercentage', v)} min={0} max={1} step={0.05} hint="0.0 - 1.0" />

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>BEP Extensions</SectionTitle>
      <Toggle label="ut_metadata" checked={form.extensionUtMetadata} onChange={(v) => set('extensionUtMetadata', v)} hint="BEP 9" />
      <Toggle label="ut_pex" checked={form.extensionUtPex} onChange={(v) => set('extensionUtPex', v)} hint="BEP 11" />
      <Toggle label="lt_donthave" checked={form.extensionLtDontHave} onChange={(v) => set('extensionLtDontHave', v)} />
      <Toggle label="Fast Extension" checked={form.extensionFastExtension} onChange={(v) => set('extensionFastExtension', v)} hint="BEP 6" />

      <SectionTitle>Transport</SectionTitle>
      <Toggle label="uTP" checked={form.utpEnabled} onChange={(v) => set('utpEnabled', v)} hint="BEP 29, LEDBAT" />
      <Toggle label="TCP Fallback" checked={form.tcpFallback} onChange={(v) => set('tcpFallback', v)} />
      <NumberInput label="Connection Timeout" value={form.transportConnectionTimeoutSeconds} onChange={(v) => set('transportConnectionTimeoutSeconds', v)} min={1} suffix="seconds" />

      <SectionTitle>PEX</SectionTitle>
      <NumberInput label="PEX Interval" value={form.pexInterval} onChange={(v) => set('pexInterval', v)} min={10} suffix="seconds" />
      <NumberInput label="Max Peers Per Message" value={form.pexMaxPeersPerMessage} onChange={(v) => set('pexMaxPeersPerMessage', v)} min={1} />

      <SectionTitle>Multi-Tracker</SectionTitle>
      <Toggle label="Enabled" checked={form.multiTrackerEnabled} onChange={(v) => set('multiTrackerEnabled', v)} hint="BEP 12" />
      <Toggle label="Failover" checked={form.multiTrackerFailoverEnabled} onChange={(v) => set('multiTrackerFailoverEnabled', v)} />
      <Toggle label="Announce to All Tiers" checked={form.announceToAllTiers} onChange={(v) => set('announceToAllTiers', v)} />
      <Toggle label="Announce to All in Tier" checked={form.announceToAllInTier} onChange={(v) => set('announceToAllInTier', v)} />
      <NumberInput label="Max Consecutive Failures" value={form.failoverMaxConsecutiveFailures} onChange={(v) => set('failoverMaxConsecutiveFailures', v)} min={1} />
      <NumberInput label="Backoff Base" value={form.failoverBackoffBaseSeconds} onChange={(v) => set('failoverBackoffBaseSeconds', v)} min={1} suffix="seconds" />
      <NumberInput label="Max Backoff" value={form.failoverMaxBackoffSeconds} onChange={(v) => set('failoverMaxBackoffSeconds', v)} min={1} suffix="seconds" />

      <SectionTitle>DHT</SectionTitle>
      <Toggle label="Auto Bootstrap" checked={form.dhtAutoBootstrap} onChange={(v) => set('dhtAutoBootstrap', v)} />
      <Toggle label="Rate Limiting" checked={form.dhtRateLimitEnabled} onChange={(v) => set('dhtRateLimitEnabled', v)} />
      <NumberInput label="Max Queries/sec" value={form.dhtMaxQueriesPerSecond} onChange={(v) => set('dhtMaxQueriesPerSecond', v)} min={1} disabled={!form.dhtRateLimitEnabled} />
      <NumberInput label="Routing Table Size" value={form.dhtRoutingTableSize} onChange={(v) => set('dhtRoutingTableSize', v)} min={1} />
      <NumberInput label="Announcement Interval" value={form.dhtAnnouncementInterval} onChange={(v) => set('dhtAnnouncementInterval', v)} min={60} suffix="seconds" />
      <NumberInput label="Bootstrap Timeout" value={form.dhtBootstrapTimeout} onChange={(v) => set('dhtBootstrapTimeout', v)} min={1} suffix="seconds" />
      <NumberInput label="Query Timeout" value={form.dhtQueryTimeout} onChange={(v) => set('dhtQueryTimeout', v)} min={1} suffix="seconds" />
      <NumberInput label="Max Nodes" value={form.dhtMaxNodes} onChange={(v) => set('dhtMaxNodes', v)} min={1} />
      <NumberInput label="Bucket Size (K)" value={form.dhtBucketSize} onChange={(v) => set('dhtBucketSize', v)} min={1} />
      <NumberInput label="Concurrent Queries" value={form.dhtConcurrentQueries} onChange={(v) => set('dhtConcurrentQueries', v)} min={1} />

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

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
      <NumberInput label="Switch Probability" value={form.switchClientProbability} onChange={(v) => set('switchClientProbability', v)} min={0} max={1} step={0.01} suffix="/ announce" hint="0.0 - 1.0" disabled={!form.clientProfileSwitching} />

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
      <NumberInput label="Adaptation Rate" value={form.swarmAdaptationRate} onChange={(v) => set('swarmAdaptationRate', v)} min={0} max={1} step={0.1} hint="0.0 - 1.0" disabled={!form.swarmIntelligenceEnabled} />
      <NumberInput label="Peer Analysis Depth" value={form.swarmPeerAnalysisDepth} onChange={(v) => set('swarmPeerAnalysisDepth', v)} min={1} disabled={!form.swarmIntelligenceEnabled} />

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

        <Toggle label="Enabled" checked={form.trackerServerEnabled} onChange={(v) => set('trackerServerEnabled', v)} hint="Built-in tracker" />
        <TextInput label="Bind Address" value={form.trackerBindAddress} onChange={(v) => set('trackerBindAddress', v)} placeholder="0.0.0.0" disabled={!form.trackerServerEnabled} />

        <SectionTitle>HTTP Tracker</SectionTitle>
        <Toggle label="HTTP Enabled" checked={form.trackerHttpEnabled} onChange={(v) => set('trackerHttpEnabled', v)} />
        <NumberInput label="HTTP Port" value={form.trackerHttpPort} onChange={(v) => set('trackerHttpPort', v)} min={1} max={65535} disabled={!form.trackerHttpEnabled} />

        <SectionTitle>UDP Tracker</SectionTitle>
        <Toggle label="UDP Enabled" checked={form.trackerUdpEnabled} onChange={(v) => set('trackerUdpEnabled', v)} />
        <NumberInput label="UDP Port" value={form.trackerUdpPort} onChange={(v) => set('trackerUdpPort', v)} min={1} max={65535} disabled={!form.trackerUdpEnabled} />

        <SectionTitle>Behavior</SectionTitle>
        <NumberInput label="Announce Interval" value={form.trackerAnnounceInterval} onChange={(v) => set('trackerAnnounceInterval', v)} min={60} suffix="seconds" hint="Time between tracker announces" />
        <NumberInput label="Max Peers Per Announce" value={form.trackerMaxPeersPerAnnounce} onChange={(v) => set('trackerMaxPeersPerAnnounce', v)} min={1} />
        <Toggle label="Enable Scrape" checked={form.trackerEnableScrape} onChange={(v) => set('trackerEnableScrape', v)} />
        <Toggle label="Private Mode" checked={form.trackerPrivateMode} onChange={(v) => set('trackerPrivateMode', v)} />
        <Toggle label="Log Announces" checked={form.trackerLogAnnounces} onChange={(v) => set('trackerLogAnnounces', v)} />
        <NumberInput label="Rate Limit" value={form.trackerRateLimitPerMinute} onChange={(v) => set('trackerRateLimitPerMinute', v)} min={1} suffix="/ min" />

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
    </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

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

      </div>
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

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
      <NumberInput label="Refresh Rate" value={form.uiRefreshRateSec} onChange={(v) => set('uiRefreshRateSec', v)} min={1} max={60} suffix="seconds" />

      </div>
    </div>
  );
}

function IndexersTab() {
  const { data: indexers, isLoading } = useIndexers();
  const createMutation = useCreateIndexer();
  const updateMutation = useUpdateIndexer();
  const deleteMutation = useDeleteIndexer();
  const testMutation = useTestIndexer();
  const [editing, setEditing] = useState<Partial<IndexerDefinition> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultIndexer: Partial<IndexerDefinition> = {
    name: '',
    indexerType: 'Prowlarr',
    url: 'http://localhost:9696',
    apiKey: '',
    apiPath: '/api',
    enableRss: true,
    enableSearch: true,
    categories: '',
    downloadClientId: 0,
  };

  const handleSave = () => {
    if (!editing) return;
    const payload = {
      ...editing,
      implementation: `${editing.indexerType || 'Prowlarr'}Indexer`,
      configContract: 'IndexerDefinition',
    };
    if (editing.id) {
      updateMutation.mutate(payload as IndexerDefinition, { onSuccess: () => setEditing(null) });
    } else {
      createMutation.mutate(payload, { onSuccess: () => setEditing(null) });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data.success })),
      onError: () => setTestResults((prev) => ({ ...prev, [id]: false })),
    });
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <div className="provider-section-header">
          <h3>Indexers</h3>
        </div>

        <div className="provider-cards">
          {indexers?.map((idx) => (
            <div key={idx.id} className="provider-card" onClick={() => setEditing({ ...idx })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(idx.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(idx.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{idx.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{idx.indexerType}</span>
                {idx.enableRss && <span className="provider-card-badge provider-card-badge-blue">RSS</span>}
                {idx.enableSearch && <span className="provider-card-badge provider-card-badge-blue">Search</span>}
              </div>
              <div className="provider-card-info">{idx.url}</div>
              {testResults[idx.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[idx.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[idx.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultIndexer })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Indexer' : 'Add Indexer'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My Prowlarr" />
            <SelectInput
              label="Type"
              value={editing.indexerType || 'Prowlarr'}
              onChange={(v) => {
                const defaults: Record<string, string> = { Prowlarr: 'http://localhost:9696', Torznab: 'http://localhost:9117', Newznab: 'http://localhost:5076' };
                setEditing({ ...editing, indexerType: v, url: defaults[v] || editing.url || '' });
              }}
              options={[
                { value: 'Prowlarr', label: 'Prowlarr' },
                { value: 'Torznab', label: 'Torznab' },
                { value: 'Newznab', label: 'Newznab' },
              ]}
            />
            <TextInput label="URL" value={editing.url || ''} onChange={(v) => setEditing({ ...editing, url: v })} placeholder="http://localhost:9696" />
            <TextInput label="API Key" value={editing.apiKey || ''} onChange={(v) => setEditing({ ...editing, apiKey: v })} type="password" />
            <TextInput label="API Path" value={editing.apiPath || '/api'} onChange={(v) => setEditing({ ...editing, apiPath: v })} placeholder="/api" />
            <TextInput label="Categories" value={editing.categories || ''} onChange={(v) => setEditing({ ...editing, categories: v })} placeholder="2000,5000" />
            <Toggle label="RSS" checked={editing.enableRss ?? true} onChange={(v) => setEditing({ ...editing, enableRss: v })} />
            <Toggle label="Search" checked={editing.enableSearch ?? true} onChange={(v) => setEditing({ ...editing, enableSearch: v })} />
            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">{(createMutation.error || updateMutation.error)?.message}</div>
            )}
            <div className="modal-actions">
              <button className="btn" onClick={() => setEditing(null)}>Cancel</button>
              <button className="btn btn-success" onClick={handleSave} disabled={createMutation.isPending || updateMutation.isPending}>
                {createMutation.isPending || updateMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function ConnectionsTab() {
  const { data: connections, isLoading } = useArrConnections();
  const createMutation = useCreateArrConnection();
  const updateMutation = useUpdateArrConnection();
  const deleteMutation = useDeleteArrConnection();
  const testMutation = useTestArrConnection();
  const syncMutation = useArrSync();
  const [editing, setEditing] = useState<Partial<ArrConnection> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultConnection: Partial<ArrConnection> = {
    name: '',
    arrType: 'Sonarr',
    url: 'http://localhost:8989',
    apiKey: '',
    syncEnabled: true,
    enableAutomaticAdd: true,
    webhookEnabled: true,
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as ArrConnection, { onSuccess: () => setEditing(null) });
    } else {
      createMutation.mutate(editing, { onSuccess: () => setEditing(null) });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data.success })),
      onError: () => setTestResults((prev) => ({ ...prev, [id]: false })),
    });
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <div className="provider-section-header">
          <h3>Arr Connections</h3>
          <button
            className="btn btn-small"
            onClick={() => syncMutation.mutate()}
            disabled={syncMutation.isPending}
          >
            {syncMutation.isPending ? 'Syncing...' : 'Sync Now'}
          </button>
          {syncMutation.isError && (
            <span style={{ color: 'var(--danger)', fontSize: '0.85rem' }}>
              Sync failed: {syncMutation.error?.message}
            </span>
          )}
          {syncMutation.isSuccess && (
            <span style={{ color: 'var(--success)', fontSize: '0.85rem' }}>
              Sync complete
            </span>
          )}
        </div>

        <div className="provider-cards">
          {connections?.map((conn) => (
            <div key={conn.id} className="provider-card" onClick={() => setEditing({ ...conn })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(conn.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(conn.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{conn.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{conn.arrType}</span>
                {conn.syncEnabled && <span className="provider-card-badge provider-card-badge-blue">Sync</span>}
                {conn.enableAutomaticAdd && <span className="provider-card-badge provider-card-badge-blue">Auto Add</span>}
                {conn.webhookEnabled && <span className="provider-card-badge provider-card-badge-blue">Webhook</span>}
              </div>
              <div className="provider-card-info">{conn.url}</div>
              {testResults[conn.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[conn.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[conn.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultConnection })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Connection' : 'Add Connection'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My Sonarr" />
            <SelectInput
              label="Type"
              value={editing.arrType || 'Sonarr'}
              onChange={(v) => {
                const defaults: Record<string, string> = { Sonarr: 'http://localhost:8989', Radarr: 'http://localhost:7878', Lidarr: 'http://localhost:8686' };
                setEditing({ ...editing, arrType: v, url: defaults[v] || editing.url || '' });
              }}
              options={[
                { value: 'Sonarr', label: 'Sonarr' },
                { value: 'Radarr', label: 'Radarr' },
                { value: 'Lidarr', label: 'Lidarr' },
              ]}
            />
            <TextInput label="URL" value={editing.url || ''} onChange={(v) => setEditing({ ...editing, url: v })} placeholder="http://localhost:8989" />
            <TextInput label="API Key" value={editing.apiKey || ''} onChange={(v) => setEditing({ ...editing, apiKey: v })} type="password" />
            <Toggle label="Sync Enabled" checked={editing.syncEnabled ?? true} onChange={(v) => setEditing({ ...editing, syncEnabled: v })} />
            <Toggle label="Auto Add" checked={editing.enableAutomaticAdd ?? true} onChange={(v) => setEditing({ ...editing, enableAutomaticAdd: v })} />
            <Toggle label="Webhook" checked={editing.webhookEnabled ?? true} onChange={(v) => setEditing({ ...editing, webhookEnabled: v })} />
            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">{(createMutation.error || updateMutation.error)?.message}</div>
            )}
            <div className="modal-actions">
              <button className="btn" onClick={() => setEditing(null)}>Cancel</button>
              <button className="btn btn-success" onClick={handleSave} disabled={createMutation.isPending || updateMutation.isPending}>
                {createMutation.isPending || updateMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function DownloadClientsTab() {
  const { data: clients, isLoading } = useDownloadClients();
  const createMutation = useCreateDownloadClient();
  const updateMutation = useUpdateDownloadClient();
  const deleteMutation = useDeleteDownloadClient();
  const testMutation = useTestDownloadClient();
  const [editing, setEditing] = useState<Partial<DownloadClientDefinition> | null>(null);
  const [testResults, setTestResults] = useState<Record<number, boolean | null>>({});

  const defaultClient: Partial<DownloadClientDefinition> = {
    name: '',
    clientType: 'QBitTorrent',
    host: 'localhost',
    port: 8080,
    useSsl: false,
    username: '',
    password: '',
    category: '',
    enable: true,
  };

  const clientDefaults: Record<string, { port: number }> = {
    QBitTorrent: { port: 8080 },
    Transmission: { port: 9091 },
    Deluge: { port: 8112 },
  };

  const handleSave = () => {
    if (!editing) return;
    if (editing.id) {
      updateMutation.mutate(editing as DownloadClientDefinition, { onSuccess: () => setEditing(null) });
    } else {
      createMutation.mutate(editing, { onSuccess: () => setEditing(null) });
    }
  };

  const handleTest = (id: number) => {
    setTestResults((prev) => ({ ...prev, [id]: null }));
    testMutation.mutate(id, {
      onSuccess: (data) => setTestResults((prev) => ({ ...prev, [id]: data.success })),
      onError: () => setTestResults((prev) => ({ ...prev, [id]: false })),
    });
  };

  if (isLoading) return <div className="loading">Loading...</div>;

  return (
    <>
      <div className="card">
        <h3>Download Clients</h3>

        <div className="provider-cards">
          {clients?.map((client) => (
            <div key={client.id} className="provider-card" onClick={() => setEditing({ ...client })}>
              <div className="provider-card-actions">
                <button
                  className="provider-card-action"
                  title="Test"
                  onClick={(e) => { e.stopPropagation(); handleTest(client.id); }}
                >
                  &#x2713;
                </button>
                <button
                  className="provider-card-action provider-card-action-danger"
                  title="Delete"
                  onClick={(e) => { e.stopPropagation(); deleteMutation.mutate(client.id); }}
                >
                  &#x2715;
                </button>
              </div>
              <div className="provider-card-name">{client.name}</div>
              <div className="provider-card-badges">
                <span className="provider-card-badge provider-card-badge-green">{client.clientType}</span>
                {client.enable && <span className="provider-card-badge provider-card-badge-blue">Enabled</span>}
                {!client.enable && <span className="provider-card-badge provider-card-badge-gray">Disabled</span>}
                {client.useSsl && <span className="provider-card-badge provider-card-badge-amber">SSL</span>}
              </div>
              <div className="provider-card-info">{client.host}:{client.port}</div>
              {testResults[client.id] === true && <div className="provider-card-test provider-card-test-ok">Test passed</div>}
              {testResults[client.id] === false && <div className="provider-card-test provider-card-test-fail">Test failed</div>}
              {testResults[client.id] === null && <div className="provider-card-test provider-card-test-pending">Testing...</div>}
            </div>
          ))}
          <div className="provider-card-add" onClick={() => setEditing({ ...defaultClient })}>
            <span className="provider-card-add-icon">+</span>
          </div>
        </div>
      </div>

      {editing && (
        <div className="modal-overlay" onClick={() => setEditing(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-title">{editing.id ? 'Edit Download Client' : 'Add Download Client'}</div>
            <TextInput label="Name" value={editing.name || ''} onChange={(v) => setEditing({ ...editing, name: v })} placeholder="My qBittorrent" />
            <SelectInput
              label="Client Type"
              value={editing.clientType || 'QBitTorrent'}
              onChange={(v) => setEditing({ ...editing, clientType: v, port: clientDefaults[v]?.port || editing.port || 8080 })}
              options={[
                { value: 'QBitTorrent', label: 'qBittorrent' },
                { value: 'Transmission', label: 'Transmission' },
                { value: 'Deluge', label: 'Deluge' },
              ]}
            />
            <TextInput label="Host" value={editing.host || ''} onChange={(v) => setEditing({ ...editing, host: v })} placeholder="localhost" />
            <NumberInput label="Port" value={editing.port || 8080} onChange={(v) => setEditing({ ...editing, port: v })} min={1} max={65535} />
            <Toggle label="Use SSL" checked={editing.useSsl ?? false} onChange={(v) => setEditing({ ...editing, useSsl: v })} />
            <TextInput label="Username" value={editing.username || ''} onChange={(v) => setEditing({ ...editing, username: v })} />
            <TextInput label="Password" value={editing.password || ''} onChange={(v) => setEditing({ ...editing, password: v })} type="password" />
            <TextInput label="Category" value={editing.category || ''} onChange={(v) => setEditing({ ...editing, category: v })} hint="Filter by category" />
            <Toggle label="Enabled" checked={editing.enable ?? true} onChange={(v) => setEditing({ ...editing, enable: v })} />
            {(createMutation.isError || updateMutation.isError) && (
              <div className="modal-error">{(createMutation.error || updateMutation.error)?.message}</div>
            )}
            <div className="modal-actions">
              <button className="btn" onClick={() => setEditing(null)}>Cancel</button>
              <button className="btn btn-success" onClick={handleSave} disabled={createMutation.isPending || updateMutation.isPending}>
                {createMutation.isPending || updateMutation.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function NotificationsTab() {
  const [settings, saveSettings] = useNotificationSettings();
  const [form, setForm] = useState<NotificationSettings>(settings);
  const [dirty, setDirty] = useState(false);
  const [saved, setSaved] = useState(false);

  const set = <K extends keyof NotificationSettings>(key: K, value: NotificationSettings[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
    setSaved(false);
  };

  const handleSave = () => {
    saveSettings(form);
    setDirty(false);
    setSaved(true);
  };

  return (
    <div>
      <div className="settings-toolbar">
        <button className="btn btn-success" onClick={handleSave} disabled={!dirty}>
          {dirty ? 'Save Changes' : 'No Changes'}
        </button>
        {saved && !dirty && (
          <span style={{ marginLeft: '0.75rem', fontSize: '0.85rem', color: 'var(--success)' }}>
            Saved
          </span>
        )}
      </div>
      <div className="card">

      <SectionTitle>General</SectionTitle>
      <Toggle label="Enable Notifications" checked={form.enabled} onChange={(v) => set('enabled', v)} hint="Show toast notifications" />
      <SelectInput
        label="Position"
        value={form.position}
        onChange={(v) => set('position', v)}
        options={[
          { value: 'top-right', label: 'Top Right' },
          { value: 'top-left', label: 'Top Left' },
          { value: 'bottom-right', label: 'Bottom Right' },
          { value: 'bottom-left', label: 'Bottom Left' },
        ]}
        disabled={!form.enabled}
      />
      <NumberInput
        label="Auto-Dismiss Timeout"
        value={form.autoDismissSeconds}
        onChange={(v) => set('autoDismissSeconds', v)}
        min={1}
        max={60}
        suffix="seconds"
        disabled={!form.enabled}
      />

      <SectionTitle>Notification Types</SectionTitle>
      <Toggle label="Info" checked={form.showInfo} onChange={(v) => set('showInfo', v)} hint="General information" />
      <Toggle label="Success" checked={form.showSuccess} onChange={(v) => set('showSuccess', v)} hint="Successful operations" />
      <Toggle label="Warning" checked={form.showWarning} onChange={(v) => set('showWarning', v)} hint="Warnings and cautions" />
      <Toggle label="Error" checked={form.showError} onChange={(v) => set('showError', v)} hint="Errors and failures" />

      </div>
    </div>
  );
}

function WebUITab() {
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
    httpsEnabled: false,
    username: '',
    password: '',
    sessionTimeoutMinutes: 30,
    localhostOnly: false,
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
    <div>
      <SaveBar dirty={dirty} isPending={save.isPending} isError={save.isError} isSuccess={save.isSuccess} error={save.error} onSave={() => save.mutate(form, { onSuccess: () => setDirty(false) })} />
      <div className="card">

      <SectionTitle>Connection</SectionTitle>
      <Toggle label="HTTPS Enabled" checked={form.httpsEnabled} onChange={(v) => set('httpsEnabled', v)} hint="Serve over HTTPS" />
      <NumberInput label="Port" value={form.port} onChange={(v) => set('port', v)} min={1} max={65535} />
      <TextInput label="Bind Address" value={form.bindAddress} onChange={(v) => set('bindAddress', v)} placeholder="0.0.0.0" />
      <Toggle label="Localhost Only" checked={form.localhostOnly} onChange={(v) => set('localhostOnly', v)} hint="Only allow local connections" />

      <SectionTitle>Authentication</SectionTitle>
      <Toggle label="Authentication Enabled" checked={form.authenticationEnabled} onChange={(v) => set('authenticationEnabled', v)} hint="Require login" />
      <TextInput
        label="Username"
        value={form.username}
        onChange={(v) => set('username', v)}
        placeholder="admin"
        disabled={!form.authenticationEnabled}
      />
      <TextInput
        label="Password"
        value={form.password}
        onChange={(v) => set('password', v)}
        type="password"
        disabled={!form.authenticationEnabled}
      />
      <NumberInput
        label="Session Timeout"
        value={form.sessionTimeoutMinutes}
        onChange={(v) => set('sessionTimeoutMinutes', v)}
        min={1}
        max={1440}
        suffix="minutes"
        disabled={!form.authenticationEnabled}
      />

      </div>
    </div>
  );
}

const sectionTitles: Record<string, string> = {
  general: 'General',
  webui: 'Web UI',
  notifications: 'Notifications',
  seeding: 'Seeding',
  bittorrent: 'BitTorrent',
  network: 'Network',
  'peer-protocol': 'Peer Protocol',
  protocols: 'Protocols',
  simulation: 'Simulation',
  'tracker-server': 'Tracker Server',
  scheduler: 'Scheduler',
  indexers: 'Indexers',
  connections: 'Connections',
  'download-clients': 'Download Clients',
  advanced: 'Advanced',
};

function Settings() {
  const { section } = useParams<{ section?: string }>();
  const activeSection = section || 'general';
  const title = sectionTitles[activeSection] || 'Settings';

  return (
    <div>
      <h1 className="page-heading">{title}</h1>

      {activeSection === 'general' && <GeneralTab />}
      {activeSection === 'webui' && <WebUITab />}
      {activeSection === 'notifications' && <NotificationsTab />}
      {activeSection === 'seeding' && <SeedingTab />}
      {activeSection === 'bittorrent' && <BitTorrentTab />}
      {activeSection === 'network' && <NetworkTab />}
      {activeSection === 'peer-protocol' && <PeerProtocolTab />}
      {activeSection === 'protocols' && <ProtocolsTab />}
      {activeSection === 'simulation' && <SimulationTab />}
      {activeSection === 'tracker-server' && <TrackerServerTab />}
      {activeSection === 'scheduler' && <SchedulerTab />}
      {activeSection === 'indexers' && <IndexersTab />}
      {activeSection === 'connections' && <ConnectionsTab />}
      {activeSection === 'download-clients' && <DownloadClientsTab />}
      {activeSection === 'advanced' && <AdvancedTab />}
    </div>
  );
}

export default Settings;

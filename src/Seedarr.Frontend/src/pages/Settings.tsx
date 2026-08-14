import { useState, useEffect } from 'react';
import {
  useNetworkStatus,
  useTrackerServerConfig,
  useUpdateTrackerServerConfig,
  useTrackerServerStats,
  useGeneralConfig,
  useSaveGeneralConfig,
  useSeedingConfig,
  useSaveSeedingConfig,
  useNetworkConfig,
  useSaveNetworkConfig,
} from '../api/hooks';
import type { TrackerServerConfig, GeneralConfig, SeedingConfig, NetworkConfig } from '../api/types';

type SettingsTab = 'general' | 'seeding' | 'network' | 'tracker-server';

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

function GeneralTab() {
  const { data: config, isLoading } = useGeneralConfig();
  const saveConfig = useSaveGeneralConfig();

  const [form, setForm] = useState<GeneralConfig>({
    instanceName: '',
    port: 9898,
    urlBase: '',
    authEnabled: false,
    username: '',
    password: '',
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const updateField = <K extends keyof GeneralConfig>(key: K, value: GeneralConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const handleSave = () => {
    saveConfig.mutate(form, {
      onSuccess: () => setDirty(false),
    });
  };

  if (isLoading) {
    return <div className="loading">Loading general configuration...</div>;
  }

  return (
    <div className="card">
      <h3>General Settings</h3>

      <div className="form-group">
        <label className="form-label">Instance Name</label>
        <input
          type="text"
          className="form-input"
          style={{ width: '200px', textAlign: 'left' }}
          value={form.instanceName}
          onChange={(e) => updateField('instanceName', e.target.value)}
          placeholder="Seedarr"
        />
      </div>

      <div className="form-group">
        <label className="form-label">Port</label>
        <input
          type="number"
          className="form-input"
          value={form.port}
          onChange={(e) => updateField('port', parseInt(e.target.value, 10) || 0)}
          min={1}
          max={65535}
        />
      </div>

      <div className="form-group">
        <label className="form-label">URL Base</label>
        <input
          type="text"
          className="form-input"
          style={{ width: '200px', textAlign: 'left' }}
          value={form.urlBase}
          onChange={(e) => updateField('urlBase', e.target.value)}
          placeholder="/seedarr"
        />
      </div>

      <div className="form-group">
        <label className="form-label">Authentication</label>
        <label className="toggle-switch">
          <input
            type="checkbox"
            checked={form.authEnabled}
            onChange={(e) => updateField('authEnabled', e.target.checked)}
          />
          <span className="toggle-slider" />
        </label>
      </div>

      <div className="form-group">
        <label className="form-label">Username</label>
        <input
          type="text"
          className="form-input"
          style={{ width: '200px', textAlign: 'left' }}
          value={form.username}
          onChange={(e) => updateField('username', e.target.value)}
          disabled={!form.authEnabled}
          placeholder="admin"
        />
      </div>

      <div className="form-group">
        <label className="form-label">Password</label>
        <input
          type="password"
          className="form-input"
          style={{ width: '200px', textAlign: 'left' }}
          value={form.password}
          onChange={(e) => updateField('password', e.target.value)}
          disabled={!form.authEnabled}
        />
      </div>

      <div className="form-actions">
        <button
          className="btn btn-success"
          onClick={handleSave}
          disabled={!dirty || saveConfig.isPending}
        >
          {saveConfig.isPending ? 'Saving...' : 'Save'}
        </button>
        <SaveFeedback
          isPending={saveConfig.isPending}
          isError={saveConfig.isError}
          isSuccess={saveConfig.isSuccess}
          error={saveConfig.error}
          dirty={dirty}
        />
      </div>
    </div>
  );
}

function SeedingTab() {
  const { data: config, isLoading } = useSeedingConfig();
  const saveConfig = useSaveSeedingConfig();

  const [form, setForm] = useState<SeedingConfig>({
    maxUploadSpeed: 0,
    maxDownloadSpeed: 0,
    distributionType: 'Equal',
    globalSeedRatioLimit: 0,
    listenPort: 6881,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const updateField = <K extends keyof SeedingConfig>(key: K, value: SeedingConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const handleSave = () => {
    saveConfig.mutate(form, {
      onSuccess: () => setDirty(false),
    });
  };

  if (isLoading) {
    return <div className="loading">Loading seeding configuration...</div>;
  }

  return (
    <div className="card">
      <h3>Seeding Configuration</h3>

      <div className="form-group">
        <label className="form-label">Max Upload Speed (KB/s)</label>
        <input
          type="number"
          className="form-input"
          value={form.maxUploadSpeed}
          onChange={(e) => updateField('maxUploadSpeed', parseInt(e.target.value, 10) || 0)}
          min={0}
        />
      </div>

      <div className="form-group">
        <label className="form-label">Max Download Speed (KB/s)</label>
        <input
          type="number"
          className="form-input"
          value={form.maxDownloadSpeed}
          onChange={(e) => updateField('maxDownloadSpeed', parseInt(e.target.value, 10) || 0)}
          min={0}
        />
      </div>

      <div className="form-group">
        <label className="form-label">Distribution Type</label>
        <select
          className="form-select"
          value={form.distributionType}
          onChange={(e) => updateField('distributionType', e.target.value)}
        >
          <option value="Pareto">Pareto</option>
          <option value="PowerLaw">Power Law</option>
          <option value="LogNormal">Log Normal</option>
          <option value="Equal">Equal</option>
        </select>
      </div>

      <div className="form-group">
        <label className="form-label">Global Seed Ratio Limit</label>
        <input
          type="number"
          className="form-input"
          value={form.globalSeedRatioLimit}
          onChange={(e) => updateField('globalSeedRatioLimit', parseFloat(e.target.value) || 0)}
          min={0}
          step={0.1}
        />
      </div>

      <div className="form-group">
        <label className="form-label">Listen Port</label>
        <input
          type="number"
          className="form-input"
          value={form.listenPort}
          onChange={(e) => updateField('listenPort', parseInt(e.target.value, 10) || 0)}
          min={1}
          max={65535}
        />
      </div>

      <div className="form-actions">
        <button
          className="btn btn-success"
          onClick={handleSave}
          disabled={!dirty || saveConfig.isPending}
        >
          {saveConfig.isPending ? 'Saving...' : 'Save'}
        </button>
        <SaveFeedback
          isPending={saveConfig.isPending}
          isError={saveConfig.isError}
          isSuccess={saveConfig.isSuccess}
          error={saveConfig.error}
          dirty={dirty}
        />
      </div>
    </div>
  );
}

function NetworkTab() {
  const { data: status } = useNetworkStatus();
  const { data: config, isLoading } = useNetworkConfig();
  const saveConfig = useSaveNetworkConfig();

  const [form, setForm] = useState<NetworkConfig>({
    proxyEnabled: false,
    proxyType: 'HTTP',
    proxyHost: '',
    proxyPort: 8080,
    proxyUsername: '',
    proxyPassword: '',
    upnpEnabled: false,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const updateField = <K extends keyof NetworkConfig>(key: K, value: NetworkConfig[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const handleSave = () => {
    saveConfig.mutate(form, {
      onSuccess: () => setDirty(false),
    });
  };

  if (isLoading) {
    return <div className="loading">Loading network configuration...</div>;
  }

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
        <h3>Network Configuration</h3>

        <div className="form-group">
          <label className="form-label">UPnP</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={form.upnpEnabled}
              onChange={(e) => updateField('upnpEnabled', e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>

        <div className="form-group">
          <label className="form-label">Proxy</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={form.proxyEnabled}
              onChange={(e) => updateField('proxyEnabled', e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>

        <div className="form-group">
          <label className="form-label">Proxy Type</label>
          <select
            className="form-select"
            value={form.proxyType}
            onChange={(e) => updateField('proxyType', e.target.value)}
            disabled={!form.proxyEnabled}
          >
            <option value="HTTP">HTTP</option>
            <option value="HTTPS">HTTPS</option>
            <option value="SOCKS4">SOCKS4</option>
            <option value="SOCKS5">SOCKS5</option>
          </select>
        </div>

        <div className="form-group">
          <label className="form-label">Proxy Host</label>
          <input
            type="text"
            className="form-input"
            style={{ width: '200px', textAlign: 'left' }}
            value={form.proxyHost}
            onChange={(e) => updateField('proxyHost', e.target.value)}
            disabled={!form.proxyEnabled}
            placeholder="proxy.example.com"
          />
        </div>

        <div className="form-group">
          <label className="form-label">Proxy Port</label>
          <input
            type="number"
            className="form-input"
            value={form.proxyPort}
            onChange={(e) => updateField('proxyPort', parseInt(e.target.value, 10) || 0)}
            disabled={!form.proxyEnabled}
            min={1}
            max={65535}
          />
        </div>

        <div className="form-group">
          <label className="form-label">Proxy Username</label>
          <input
            type="text"
            className="form-input"
            style={{ width: '200px', textAlign: 'left' }}
            value={form.proxyUsername}
            onChange={(e) => updateField('proxyUsername', e.target.value)}
            disabled={!form.proxyEnabled}
          />
        </div>

        <div className="form-group">
          <label className="form-label">Proxy Password</label>
          <input
            type="password"
            className="form-input"
            style={{ width: '200px', textAlign: 'left' }}
            value={form.proxyPassword}
            onChange={(e) => updateField('proxyPassword', e.target.value)}
            disabled={!form.proxyEnabled}
          />
        </div>

        <div className="form-actions">
          <button
            className="btn btn-success"
            onClick={handleSave}
            disabled={!dirty || saveConfig.isPending}
          >
            {saveConfig.isPending ? 'Saving...' : 'Save'}
          </button>
          <SaveFeedback
            isPending={saveConfig.isPending}
            isError={saveConfig.isError}
            isSuccess={saveConfig.isSuccess}
            error={saveConfig.error}
            dirty={dirty}
          />
        </div>
      </div>
    </>
  );
}

function TrackerServerTab() {
  const { data: config, isLoading: configLoading } = useTrackerServerConfig();
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const updateConfig = useUpdateTrackerServerConfig();

  const [form, setForm] = useState<TrackerServerConfig>({
    httpEnabled: false,
    httpPort: 8080,
    udpEnabled: false,
    udpPort: 8081,
    maxPeersPerTorrent: 200,
    announceInterval: 1800,
  });
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (config) {
      setForm(config);
      setDirty(false);
    }
  }, [config]);

  const updateField = <K extends keyof TrackerServerConfig>(
    key: K,
    value: TrackerServerConfig[K]
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setDirty(true);
  };

  const handleSave = () => {
    updateConfig.mutate(form, {
      onSuccess: () => setDirty(false),
    });
  };

  if (configLoading) {
    return <div className="loading">Loading tracker server configuration...</div>;
  }

  return (
    <>
      <div className="card">
        <h3>Tracker Server Configuration</h3>

        <div className="form-group">
          <label className="form-label">HTTP Enabled</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={form.httpEnabled}
              onChange={(e) => updateField('httpEnabled', e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>

        <div className="form-group">
          <label className="form-label">HTTP Port</label>
          <input
            type="number"
            className="form-input"
            value={form.httpPort}
            onChange={(e) => updateField('httpPort', parseInt(e.target.value, 10) || 0)}
            disabled={!form.httpEnabled}
            min={1}
            max={65535}
          />
        </div>

        <div className="form-group">
          <label className="form-label">UDP Enabled</label>
          <label className="toggle-switch">
            <input
              type="checkbox"
              checked={form.udpEnabled}
              onChange={(e) => updateField('udpEnabled', e.target.checked)}
            />
            <span className="toggle-slider" />
          </label>
        </div>

        <div className="form-group">
          <label className="form-label">UDP Port</label>
          <input
            type="number"
            className="form-input"
            value={form.udpPort}
            onChange={(e) => updateField('udpPort', parseInt(e.target.value, 10) || 0)}
            disabled={!form.udpEnabled}
            min={1}
            max={65535}
          />
        </div>

        <div className="form-group">
          <label className="form-label">Max Peers Per Torrent</label>
          <input
            type="number"
            className="form-input"
            value={form.maxPeersPerTorrent}
            onChange={(e) => updateField('maxPeersPerTorrent', parseInt(e.target.value, 10) || 0)}
            min={1}
          />
        </div>

        <div className="form-group">
          <label className="form-label">Announce Interval (seconds)</label>
          <input
            type="number"
            className="form-input"
            value={form.announceInterval}
            onChange={(e) => updateField('announceInterval', parseInt(e.target.value, 10) || 0)}
            min={60}
          />
        </div>

        <div className="form-actions">
          <button
            className="btn btn-success"
            onClick={handleSave}
            disabled={!dirty || updateConfig.isPending}
          >
            {updateConfig.isPending ? 'Saving...' : 'Save'}
          </button>
          <SaveFeedback
            isPending={updateConfig.isPending}
            isError={updateConfig.isError}
            isSuccess={updateConfig.isSuccess}
            error={updateConfig.error}
            dirty={dirty}
          />
        </div>
      </div>

      <div className="card">
        <h3>Tracker Server Statistics</h3>
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

function Settings() {
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');

  const tabs: { key: SettingsTab; label: string }[] = [
    { key: 'general', label: 'General' },
    { key: 'seeding', label: 'Seeding' },
    { key: 'network', label: 'Network' },
    { key: 'tracker-server', label: 'Tracker Server' },
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
      {activeTab === 'network' && <NetworkTab />}
      {activeTab === 'tracker-server' && <TrackerServerTab />}
    </div>
  );
}

export default Settings;

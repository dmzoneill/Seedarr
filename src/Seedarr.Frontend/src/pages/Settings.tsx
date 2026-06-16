import { useState } from 'react';
import { useNetworkStatus } from '../api/hooks';

type SettingsTab = 'general' | 'seeding' | 'network';

function Settings() {
  const [activeTab, setActiveTab] = useState<SettingsTab>('general');
  const { data: network } = useNetworkStatus();

  const tabs: { key: SettingsTab; label: string }[] = [
    { key: 'general', label: 'General' },
    { key: 'seeding', label: 'Seeding' },
    { key: 'network', label: 'Network' },
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

      {activeTab === 'general' && (
        <div className="card">
          <h3>General Settings</h3>
          <div className="status-row">
            <span className="status-label">API Port</span>
            <span className="status-value">9898</span>
          </div>
          <div className="status-row">
            <span className="status-label">Watch Folder</span>
            <span className="status-value">Not configured</span>
          </div>
        </div>
      )}

      {activeTab === 'seeding' && (
        <div className="card">
          <h3>Seeding Configuration</h3>
          <div className="status-row">
            <span className="status-label">Max Upload Speed</span>
            <span className="status-value">1 MB/s</span>
          </div>
          <div className="status-row">
            <span className="status-label">Distribution Strategy</span>
            <span className="status-value">Equal</span>
          </div>
          <div className="status-row">
            <span className="status-label">Client Profile</span>
            <span className="status-value">qBittorrent 4.4.2</span>
          </div>
        </div>
      )}

      {activeTab === 'network' && (
        <div className="card">
          <h3>Network Status</h3>
          <div className="status-row">
            <span className="status-label">Local IP</span>
            <span className="status-value">{network?.localIp ?? '-'}</span>
          </div>
          <div className="status-row">
            <span className="status-label">External IP</span>
            <span className="status-value">{network?.externalIp || '-'}</span>
          </div>
          <div className="status-row">
            <span className="status-label">UPnP</span>
            <span className="status-value">{network?.upnpAvailable ? 'Available' : 'Unavailable'}</span>
          </div>
          <div className="status-row">
            <span className="status-label">Proxy</span>
            <span className="status-value">{network?.proxyEnabled ? 'Enabled' : 'Disabled'}</span>
          </div>
        </div>
      )}
    </div>
  );
}

export default Settings;

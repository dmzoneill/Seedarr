import { useState, useEffect } from 'react';
import { useTrackerServerConfig, useSaveTrackerServerConfig, useTrackerServerStats } from '../../api/hooks';
import type { TrackerServerConfig } from '../../api/types';
import { formatUptime } from '../../utils/formatters';
import { SaveBar, Toggle, TextInput, NumberInput, SectionTitle } from './shared';

export function TrackerServerTab() {
  const { data: config, isLoading: configLoading } = useTrackerServerConfig();
  const { data: stats, isLoading: statsLoading } = useTrackerServerStats();
  const save = useSaveTrackerServerConfig();
  const [form, setForm] = useState<TrackerServerConfig>({
    id: 1,
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

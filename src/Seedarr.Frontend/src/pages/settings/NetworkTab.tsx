import { useState, useEffect } from 'react';
import { useNetworkStatus, useNetworkConfig, useSaveNetworkConfig } from '../../api/hooks';
import type { NetworkConfig } from '../../api/types';
import { SaveBar, Toggle, SelectInput, TextInput, NumberInput, SectionTitle } from './shared';

export function NetworkTab() {
  const { data: status } = useNetworkStatus();
  const { data: config, isLoading } = useNetworkConfig();
  const save = useSaveNetworkConfig();
  const [form, setForm] = useState<NetworkConfig>({
    id: 1,
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

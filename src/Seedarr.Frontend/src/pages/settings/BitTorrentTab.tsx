import { useState, useEffect } from 'react';
import { useBitTorrentConfig, useSaveBitTorrentConfig } from '../../api/hooks';
import type { BitTorrentConfig } from '../../api/types';
import { SaveBar, Toggle, SelectInput, TextInput, NumberInput, SectionTitle } from './shared';

export function BitTorrentTab() {
  const { data: config, isLoading } = useBitTorrentConfig();
  const save = useSaveBitTorrentConfig();
  const [form, setForm] = useState<BitTorrentConfig>({
    id: 1,
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

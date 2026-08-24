import { useState, useEffect } from 'react';
import { useProtocolsConfig, useSaveProtocolsConfig } from '../../api/hooks';
import type { ProtocolsConfig } from '../../api/types';
import { SaveBar, Toggle, NumberInput, SectionTitle } from './shared';

export function ProtocolsTab() {
  const { data: config, isLoading } = useProtocolsConfig();
  const save = useSaveProtocolsConfig();
  const [form, setForm] = useState<ProtocolsConfig>({
    id: 1,
    extensionUtMetadata: true,
    extensionUtPex: true,
    extensionLtDontHave: true,
    extensionFastExtension: true,
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

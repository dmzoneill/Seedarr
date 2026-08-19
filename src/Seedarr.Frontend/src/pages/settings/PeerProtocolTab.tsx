import { useState, useEffect } from 'react';
import { usePeerProtocolConfig, useSavePeerProtocolConfig } from '../../api/hooks';
import type { PeerProtocolConfig } from '../../api/types';
import { SaveBar, NumberInput, SectionTitle } from './shared';

export function PeerProtocolTab() {
  const { data: config, isLoading } = usePeerProtocolConfig();
  const save = useSavePeerProtocolConfig();
  const [form, setForm] = useState<PeerProtocolConfig>({
    id: 1,
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
